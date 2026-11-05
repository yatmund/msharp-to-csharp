using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.IdentityModel.Tokens;
using Platform.Security;

namespace Platform.Security
{
    public interface ILoginInfo
    {
        IEnumerable<string> GetRoles();
        string DisplayName { get; }
        string ID { get; }
        string Email { get; }
        TimeSpan? Timeout { get; }
    }

    public class GenericLoginInfo : ILoginInfo
    {
        public GenericLoginInfo() { }

        public GenericLoginInfo(ILoginInfo copyFrom)
        {
            Roles = copyFrom.GetRoles()?.ToArray();
            DisplayName = copyFrom.DisplayName;
            ID = copyFrom.ID;
            Email = copyFrom.Email;
            Timeout = copyFrom.Timeout;
        }

        public IEnumerable<string> Roles { get; set; }
        public string DisplayName { get; set; }
        public string ID { get; set; }
        public string Email { get; set; }
        public TimeSpan? Timeout { get; set; }

        public IEnumerable<string> GetRoles() => Roles ?? Enumerable.Empty<string>();
    }

    public class ExternalLoginInfo
    {
        public bool IsAuthenticated { get; set; }
        public string Issuer { get; set; }
        public string Email { get; set; }
        public string NameIdentifier { get; set; }
        public string UserName { get; set; }
    }

    static class Jwt
    {
        // Config "Authentication:JWT:Secret"; the key must be at least 32 characters for HMAC-SHA256.
        internal static SymmetricSecurityKey Key() =>
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Config.Get("Authentication:JWT:Secret") ?? throw new InvalidOperationException("Authentication:JWT:Secret is not configured.")));
    }

    /// <summary>Signs in the user from a JWT in the Authorization header or the "jwt" query parameter.</summary>
    public class JwtAuthenticateAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var request = context.HttpContext.Request;
            var header = request.Headers["Authorization"].ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7) : request.Query["jwt"].ToString();
            if (string.IsNullOrEmpty(token)) return;
            var user = OAuth.Instance.DecodeJwt(token);
            if (user != null) context.HttpContext.User = user;
        }
    }

    public class OAuth
    {
        public static readonly OAuth Instance = new OAuth();

        public event AwaitableEventHandler<ExternalLoginInfo> ExternalLoginAuthenticated;

        static Microsoft.AspNetCore.Http.HttpContext Http => Context.Current.Http();

        public Task LogOff() => Http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        public Task LoginBy(string provider) =>
            Http.ChallengeAsync(provider, new AuthenticationProperties { RedirectUri = "/ExternalLoginCallback?provider=" + Uri.EscapeDataString(provider) });

        public Task NotifyExternalLoginAuthenticated(ExternalLoginInfo info) => ExternalLoginAuthenticated.Raise(info);

        public ClaimsPrincipal DecodeJwt(string jwt)
        {
            try
            {
                var parameters = new TokenValidationParameters
                {
                    IssuerSigningKey = Jwt.Key(),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                };
                return new JwtSecurityTokenHandler().ValidateToken(jwt, parameters, out _);
            }
            catch (Exception ex) when (ex is SecurityTokenException || ex is ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Protects data with AES-256 keyed from a fixed secret, so every server shares keys without a key ring.
    /// Not wire-compatible with Olive's: cookies protected before the switch can't be read, so users sign in again.
    /// </summary>
    public class SymmetricKeyDataProtector : IDataProtector, IDataProtectionProvider
    {
        readonly byte[] key;
        readonly string purpose;

        public SymmetricKeyDataProtector(string encryptionKey) : this("default", encryptionKey) { }

        public SymmetricKeyDataProtector(string purpose, string encryptionKey)
        {
            this.purpose = purpose;
            using (var sha = SHA256.Create()) key = sha.ComputeHash(Encoding.UTF8.GetBytes(purpose + "|" + encryptionKey));
        }

        public IDataProtector CreateProtector(string purpose) => new SymmetricKeyDataProtector(this.purpose + "." + purpose, Convert.ToBase64String(key));

        public byte[] Protect(byte[] plaintext)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();
                var cipher = aes.CreateEncryptor().TransformFinalBlock(plaintext, 0, plaintext.Length);
                return aes.IV.Concat(cipher).ToArray();
            }
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = protectedData.Take(16).ToArray();
                return aes.CreateDecryptor().TransformFinalBlock(protectedData, 16, protectedData.Length - 16);
            }
        }
    }
}

namespace Platform
{
    public static class SecurityExtensions
    {
        const string TimeoutClaim = "Timeout";

        public static ClaimsIdentity ToClaimsIdentity(this ILoginInfo @this)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, @this.DisplayName ?? string.Empty),
                new Claim(ClaimTypes.NameIdentifier, @this.ID ?? string.Empty),
            };
            if (!string.IsNullOrEmpty(@this.Email)) claims.Add(new Claim(ClaimTypes.Email, @this.Email));
            if (@this.Timeout.HasValue) claims.Add(new Claim(TimeoutClaim, ((int)@this.Timeout.Value.TotalMinutes).ToString()));
            claims.AddRange(@this.GetRoles().Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public static string CreateJwtToken(this ILoginInfo @this, IEnumerable<Claim> additionalClaims = null, bool remember = false)
        {
            var identity = @this.ToClaimsIdentity();
            if (additionalClaims != null) identity.AddClaims(additionalClaims);
            var expiry = remember ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.Add(@this.Timeout ?? TimeSpan.FromMinutes(20));
            var token = new JwtSecurityTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = identity,
                Expires = expiry,
                SigningCredentials = new SigningCredentials(Jwt.Key(), SecurityAlgorithms.HmacSha256Signature),
            });
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static async Task LogOn(this ILoginInfo @this, IEnumerable<Claim> additionalClaims = null, bool remember = false)
        {
            var identity = @this.ToClaimsIdentity();
            if (additionalClaims != null) identity.AddClaims(additionalClaims);
            var properties = new AuthenticationProperties
            {
                IsPersistent = remember,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(remember ? TimeSpan.FromDays(30) : @this.Timeout ?? TimeSpan.FromMinutes(20)),
            };
            await Context.Current.Http().SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
        }

        public static bool IsPersistent(this ClaimsPrincipal @this) => @this.FindFirst(ClaimTypes.IsPersistent)?.Value == "true";

        public static DateTimeOffset GetExpiration(this ClaimsPrincipal @this) =>
            DateTimeOffset.TryParse(@this.FindFirst(ClaimTypes.Expiration)?.Value, out var value) ? value : DateTimeOffset.MaxValue;

        public static bool Is(this ILoginInfo @this, ClaimsPrincipal loggedInUser) => loggedInUser.Is(@this);

        public static bool Is(this ClaimsPrincipal @this, ILoginInfo loginInfo) =>
            loginInfo != null && @this?.FindFirst(ClaimTypes.NameIdentifier)?.Value == loginInfo.ID;

        public static GenericLoginInfo Clone(this ILoginInfo @this, Action<GenericLoginInfo> change = null)
        {
            var clone = new GenericLoginInfo(@this);
            change?.Invoke(clone);
            return clone;
        }
    }
}
