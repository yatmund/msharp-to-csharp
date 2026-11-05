using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Platform
{
    public static partial class Extensions
    {
        public static bool HasValue(this string text) => !string.IsNullOrEmpty(text);

        public static bool IsEmpty(this string text) => string.IsNullOrEmpty(text);

        public static string OrEmpty(this string @this) => @this ?? string.Empty;

        public static string Or(this string @this, string defaultValue) => string.IsNullOrEmpty(@this) ? defaultValue : @this;

        public static string Or(this string @this, Func<string> defaultValueProvider) => string.IsNullOrEmpty(@this) ? defaultValueProvider() : @this;

        public static string OrNullIfEmpty(this string @this) => string.IsNullOrEmpty(@this) ? null : @this;

        public static string TrimOrNull(this string @this) => @this?.Trim();

        public static string TrimOrEmpty(this string @this) => @this?.Trim() ?? string.Empty;

        public static string ToLowerOrEmpty(this string @this) => @this?.ToLower() ?? string.Empty;

        public static string ToUpperOrEmpty(this string @this) => @this?.ToUpper() ?? string.Empty;

        public static string ToStringOrEmpty(this object @this) => @this?.ToString() ?? string.Empty;

        public static bool IsAnyOf(this string @this, params string[] items) => items.Contains(@this);

        public static bool IsAnyOf(this string @this, IEnumerable<string> items) => items.Contains(@this);

        public static bool IsNoneOf(this string @this, params string[] items) => !items.Contains(@this);

        static StringComparison Comparison(bool caseSensitive) => caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        public static bool Contains(this string @this, string subString, bool caseSensitive)
        {
            if (subString == null) return true;
            if (@this == null) return false;
            return @this.IndexOf(subString, Comparison(caseSensitive)) > -1;
        }

        public static bool Lacks(this string @this, string phrase, bool caseSensitive = false) =>
            !string.IsNullOrEmpty(phrase) && !@this.Contains(phrase, caseSensitive);

        public static bool LacksAll(this string @this, params string[] phrases) => @this.LacksAll(false, phrases);

        public static bool LacksAll(this string @this, bool caseSensitive, params string[] phrases) =>
            phrases.Where(p => !string.IsNullOrEmpty(p)).All(p => @this.Lacks(p, caseSensitive));

        public static bool ContainsAll(this string @this, string[] keywords, bool caseSensitive) => keywords.All(k => @this.Contains(k, caseSensitive));

        public static bool ContainsAny(this string @this, IEnumerable<string> keywords, bool caseSensitive = true)
        {
            if (string.IsNullOrEmpty(@this)) return false;
            foreach (var keyword in keywords)
            {
                if (keyword == null) throw new ArgumentException("Keywords can't contain null.");
                if (@this.Contains(keyword, caseSensitive)) return true;
            }
            return false;
        }

        public static bool StartsWithAny(this string @this, params string[] listOfBeginnings) => @this.StartsWithAny(true, listOfBeginnings);

        public static bool StartsWithAny(this string @this, bool caseSensitive, params string[] listOfBeginnings) =>
            listOfBeginnings.Any(b => @this.StartsWith(b, Comparison(caseSensitive)));

        public static bool EndsWithAny(this string @this, params string[] listOfEndings) => listOfEndings.Any(e => @this.EndsWith(e));

        public static string EnsureStartsWith(this string @this, string expression, bool caseSensitive = true)
        {
            if (string.IsNullOrEmpty(expression)) return @this;
            if (string.IsNullOrEmpty(@this)) return expression;
            return @this.StartsWith(expression, Comparison(caseSensitive)) ? @this : expression + @this;
        }

        public static string EnsureEndsWith(this string @this, string expression, bool caseSensitive = true)
        {
            if (string.IsNullOrEmpty(expression)) return @this;
            if (string.IsNullOrEmpty(@this)) return expression;
            return @this.EndsWith(expression, Comparison(caseSensitive)) ? @this : @this + expression;
        }

        public static string WithPrefix(this string @this, string prefix) => string.IsNullOrEmpty(@this) ? string.Empty : prefix + @this;

        public static string WithSuffix(this string @this, string suffix) => string.IsNullOrEmpty(@this) ? string.Empty : @this + suffix;

        public static string WithWrappers(this string @this, string left, string right) => string.IsNullOrEmpty(@this) ? string.Empty : left + @this + right;

        public static string Left(this string @this, int length) => length <= 0 ? string.Empty : @this.Length <= length ? @this : @this.Substring(0, length);

        public static string Right(this string @this, int length) => length <= 0 ? string.Empty : @this.Length <= length ? @this : @this.Substring(@this.Length - length);

        public static string GetLastChar(this string @this) => string.IsNullOrEmpty(@this) ? null : @this.Substring(@this.Length - 1);

        public static string TrimStart(this string @this, string textToTrim) => @this == null ? string.Empty : @this.TrimStart(textToTrim, true);

        public static string TrimStart(this string @this, string search, bool caseSensitive)
        {
            if (caseSensitive && @this == null) return string.Empty;
            if (string.IsNullOrEmpty(search)) return @this;
            while (@this.StartsWith(search, Comparison(caseSensitive))) @this = @this.Substring(search.Length);
            return @this;
        }

        public static string TrimEnd(this string @this, int numberOfCharacters)
        {
            if (numberOfCharacters < 0) throw new ArgumentException("numberOfCharacters must be greater than 0.");
            if (numberOfCharacters == 0) return @this;
            if (string.IsNullOrEmpty(@this) || @this.Length <= numberOfCharacters) return string.Empty;
            return @this.Substring(0, @this.Length - numberOfCharacters);
        }

        public static string TrimEnd(this string @this, string unnecessaryText) => @this.TrimEnd(unnecessaryText, true);

        public static string TrimEnd(this string @this, string unnecessaryText, bool caseSensitive)
        {
            if (@this == null) return string.Empty;
            if (string.IsNullOrEmpty(unnecessaryText)) return @this;
            return @this.EndsWith(unnecessaryText, Comparison(caseSensitive)) ? @this.Substring(0, @this.Length - unnecessaryText.Length) : @this;
        }

        public static string TrimBefore(this string @this, string search, bool trimPhrase = false, bool caseSensitive = false)
        {
            if (@this == null) return null;
            var index = @this.IndexOf(search, Comparison(caseSensitive));
            if (index == -1) return @this;
            return trimPhrase ? @this.Substring(index + search.Length) : @this.Substring(index);
        }

        public static string TrimAfter(this string @this, string phrase, bool trimPhrase = true, bool caseSensitive = false)
        {
            if (@this == null) return null;
            var index = @this.IndexOf(phrase, Comparison(caseSensitive));
            if (index == -1) return @this;
            return trimPhrase ? @this.Substring(0, index) : @this.Substring(0, index + phrase.Length);
        }

        public static string RemoveBefore(this string @this, string phrase, bool caseSensitive = false) => @this.TrimBefore(phrase, false, caseSensitive);

        public static string RemoveFrom(this string @this, string phrase, bool caseSensitive = false) => @this.TrimAfter(phrase, true, caseSensitive);

        public static string Remove(this string text, string substringToRemove) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substringToRemove) ? text : text.Replace(substringToRemove, string.Empty);

        // The substring is a regex pattern, as in Olive.
        public static string Remove(this string text, string substringToRemove, bool caseSensitive) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substringToRemove) ? text : Regex.Replace(text, substringToRemove, string.Empty, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        public static string Remove(this string text, string[] substringsToRemove) => text.Remove(substringsToRemove, true);

        public static string Remove(this string text, string[] substringsToRemove, bool caseSensitive) =>
            substringsToRemove.Aggregate(text, (t, s) => t.Remove(s, caseSensitive));

        public static string Remove(this string @this, string firstSubstringsToRemove, params string[] otherSubstringsToRemove) =>
            @this.Remove(new[] { firstSubstringsToRemove }.Concat(otherSubstringsToRemove).ToArray(), true);

        public static string Remove(this string @this, string firstSubstringsToRemove, bool caseSensitive, params string[] otherSubstringsToRemove) =>
            @this.Remove(new[] { firstSubstringsToRemove }.Concat(otherSubstringsToRemove).ToArray(), caseSensitive);

        public static string KeepReplacing(this string @this, string original, string substitute)
        {
            if (string.IsNullOrEmpty(@this) || string.IsNullOrEmpty(original)) return @this.KeepReplacing(original, substitute, true);
            substitute = substitute ?? string.Empty;
            if (substitute.Contains(original)) return @this.Replace(original, substitute);
            while (@this.Contains(original)) @this = @this.Replace(original, substitute);
            return @this;
        }

        public static string KeepReplacing(this string @this, string original, string substitute, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(@this) || original == null) return @this;
            substitute = substitute ?? string.Empty;
            // The original is a regex pattern, as in Olive.
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            string Once(string text) => Regex.Replace(text, original, substitute.Replace("$", "$$"), options);
            if (original.Length == 0 || substitute.Contains(original, caseSensitive)) return Once(@this);
            for (var pass = 0; pass < 10000 && Regex.IsMatch(@this, original, options); pass++)
            {
                var next = Once(@this);
                if (next == @this) break;
                @this = next;
            }
            return @this;
        }

        public static string Repeat(this string @this, int times) => string.Concat(Enumerable.Repeat(@this, times));

        public static string Repeat(this string @this, int times, string separator)
        {
            if (times < 0) throw new ArgumentOutOfRangeException(nameof(times));
            return string.Concat(Enumerable.Repeat(@this + separator, times));
        }

        public static IEnumerable<int> AllIndicesOf(this string @this, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) throw new ArgumentNullException(nameof(pattern));
            return Indices(@this, pattern);
        }

        static IEnumerable<int> Indices(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            for (var index = text.IndexOf(pattern, StringComparison.Ordinal); index > -1; index = text.IndexOf(pattern, index + pattern.Length, StringComparison.Ordinal))
                yield return index;
        }

        public static string[] ToLines(this string @this) =>
            @this == null ? new string[0] : @this.Split('\n').Select(l => l.Trim('\r')).ToArray();

        public static string ToHtmlLines(this string @this) =>
            @this == null ? string.Empty : @this.Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>");

        public static string Summarize(this string @this, int maximumLength)
        {
            if (string.IsNullOrEmpty(@this) || @this.Length <= maximumLength) return @this;
            var cut = @this.Substring(0, maximumLength);
            var result = cut;
            // Don't end mid-word: back up to the last whitespace when the next character continues the word.
            if (char.IsLetterOrDigit(@this[maximumLength]))
            {
                var space = -1;
                for (var i = cut.Length - 1; i > 0 && space < 0; i--) if (char.IsWhiteSpace(cut[i])) space = i;
                if (space > 0) result = cut.Substring(0, space);
            }
            result = result.TrimEnd(' ');
            return (result.Length == 0 ? cut : result) + "...";
        }

        public static string Summarize(this string @this, int maximumLength, bool enforceMaxLength)
        {
            var result = @this.Summarize(maximumLength);
            if (!enforceMaxLength || result == null || result.Length <= maximumLength) return result;
            return @this.Substring(0, maximumLength - 3) + "...";
        }

        public static string FormatWith(this string @this, object arg, params object[] additionalArgs)
        {
            try
            {
                return string.Format(@this, new[] { arg }.Concat(additionalArgs).ToArray());
            }
            catch (Exception ex)
            {
                throw new FormatException("Cannot format the text '" + @this + "' with the given arguments.", ex);
            }
        }

        public static string ToLiteralFromPascalCase(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return string.Empty;
            var result = new StringBuilder();
            for (var i = 0; i < @this.Length; i++)
            {
                var c = @this[i];
                if (i > 0 && char.IsUpper(c) && !(i + 1 < @this.Length && char.IsUpper(@this[i + 1])))
                    result.Append(' ').Append(char.ToLower(c));
                else
                    result.Append(c);
            }
            return result.ToString().Trim();
        }

        static string IdentifierWords(string text, bool capitaliseAfterSpace)
        {
            var result = new StringBuilder();
            string pending = null;
            foreach (var c in text.Replace("&", "And"))
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (pending == "_") result.Append('_').Append(c);
                    else if (pending == " " && capitaliseAfterSpace) result.Append(char.ToUpper(c));
                    else result.Append(c);
                    pending = null;
                }
                else if (result.Length > 0)
                    pending = c == ' ' && pending != "_" ? " " : "_";
            }
            return result.ToString();
        }

        public static string ToPascalCaseId(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return @this;
            var id = IdentifierWords(@this, true);
            if (id.Length == 0) return id;
            id = char.ToUpper(id[0]) + id.Substring(1);
            return char.IsDigit(id[0]) ? "_" + id : id;
        }

        public static string ToCamelCaseId(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return string.Empty;
            var id = IdentifierWords(@this, true);
            if (id.Length == 0) return id;
            id = char.ToLower(id[0]) + id.Substring(1);
            return char.IsDigit(id[0]) ? "_" + id : id;
        }

        public static string ToSnakeCase(this string @this) =>
            string.IsNullOrEmpty(@this) ? @this : Regex.Replace(@this, "(?<!^)([A-Z])", "_$1").ToLower();

        public static string ToKebabCase(this string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var words = Regex.Split(Regex.Replace(str, "(?<=[a-z0-9])([A-Z])", " $1"), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0);
            return string.Join("-", words).ToLower();
        }

        public static string CapitaliseFirstLetters(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return @this;
            return string.Join(" ", @this.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpper(w[0]) + w.Substring(1)));
        }

        public static string ToProperCase(this string @this) =>
            string.IsNullOrEmpty(@this) ? @this : char.ToUpper(@this[0]) + @this.Substring(1).ToLower();

        public static string ToPlural(this string @this)
        {
            if (string.IsNullOrWhiteSpace(@this)) return string.Empty;
            var word = @this.Trim();
            var lower = word.ToLower();
            if (lower.EndsWith("y") && word.Length > 1 && "aeiou".IndexOf(lower[lower.Length - 2]) < 0) return word.Substring(0, word.Length - 1) + "ies";
            if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") || lower.EndsWith("ch") || lower.EndsWith("sh")) return word + "es";
            return word + "s";
        }

        public static string ToCountString(this string @this, int count, string format = null) =>
            @this.ToCountString(count, !string.IsNullOrEmpty(@this) && char.IsUpper(@this[0]) ? "No" : "no", format);

        public static string ToCountString(this string @this, int count, string zeroQualifier, string format = null)
        {
            if (string.IsNullOrWhiteSpace(@this)) throw new ArgumentNullException(nameof(@this));
            if (count < 0) throw new ArgumentException("count must be zero or more.");
            if (count == 0) return zeroQualifier + " " + @this;
            if (count == 1) return "1 " + @this;
            return (string.IsNullOrEmpty(format) ? count.ToString() : count.ToString(format)) + " " + @this.ToPlural();
        }

        public static bool ContainsWholeWord(this string @this, string word, bool caseSensitive = true) =>
            @this != null && word != null && Regex.IsMatch(@this, $@"\b{Regex.Escape(word)}\b", caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        public static string ReplaceWholeWord(this string @this, string word, string replacement, bool caseSensitive = true) =>
            Regex.Replace(@this, $@"\b{Regex.Escape(word)}\b", replacement, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        public static string ToSafeFileName(this string @this, char replacement = '-')
        {
            if (@this == null) return string.Empty;
            var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\r', '\n', '\t' }));
            var result = new string(@this.Select(c => invalid.Contains(c) ? replacement : c).ToArray());
            var doubled = new string(replacement, 2);
            while (result.Contains(doubled)) result = result.Replace(doubled, replacement.ToString());
            return result;
        }

        public static string HtmlEncode(this string @this) => @this == null ? string.Empty : WebUtility.HtmlEncode(@this);

        public static string HtmlDecode(this string @this) => @this == null ? string.Empty : WebUtility.HtmlDecode(@this);

        public static string UrlEncode(this string @this) => @this == null ? string.Empty : WebUtility.UrlEncode(@this);

        public static string UrlDecode(this string @this) => @this == null ? string.Empty : WebUtility.UrlDecode(@this);

        public static string XmlEncode(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return string.Empty;
            return @this.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        public static string XmlDecode(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return string.Empty;
            return @this.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
        }

        public static string JavascriptEncode(this string @this) =>
            @this.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\x22").Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

        public static string EscapeCsvValue(this string @this)
        {
            if (string.IsNullOrEmpty(@this)) return string.Empty;
            var text = Regex.Replace(@this, "(?<!\r)\n", "\r\n");
            return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) > -1 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }

        public static bool IsEmailAddress(this string @this) =>
            Regex.IsMatch(@this, @"^[\w!#$%&'*+/=?^`{|}~-]+(\.[\w!#$%&'*+/=?^`{|}~-]+)*@([\w-]+\.)+[\w-]+$");

        public static bool IsUrl(this string @this) =>
            !string.IsNullOrEmpty(@this) && Uri.TryCreate(@this, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        public static string ToASCII(this string @this) => Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(@this));

        public static byte[] ToBytes(this string @this, Encoding encoding) => encoding.GetBytes(@this);

        public static byte[] ToBytesFromBase64(this string @this) => @this == null ? new byte[0] : Convert.FromBase64String(@this);

        public static string ToBase64String(this byte[] @this) => @this == null ? null : Convert.ToBase64String(@this);

        public static string ToString(this byte[] @this, Encoding encoding) => encoding.GetString(@this);

        public static IEnumerable<string> Split(this string @this, int chunkSize)
        {
            if (string.IsNullOrEmpty(@this)) yield break;
            for (var i = 0; i < @this.Length; i += chunkSize)
                yield return @this.Substring(i, Math.Min(chunkSize, @this.Length - i));
        }

        public static string CreateMD5Hash(this string @this) => @this.CreateMD5Hash(false);

        public static string CreateMD5Hash(this string @this, bool asHex)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(@this));
                return asHex ? string.Concat(hash.Select(b => b.ToString("X2"))) : Convert.ToBase64String(hash);
            }
        }

        public static string CreateSHA1Hash(this string @this)
        {
            using (var sha = SHA1.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(@this))).TrimEnd('=');
        }

        public static string CreateSHA256Hash(this string @this)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(@this)).Select(b => b.ToString("x2")));
        }

        public static bool IsUpper(this char @this) => char.IsUpper(@this);

        public static bool IsLower(this char @this) => char.IsLower(@this);

        public static bool IsLetter(this char @this) => char.IsLetter(@this);

        public static bool IsDigit(this char @this) => char.IsDigit(@this);

        public static bool IsWhiteSpace(this char @this) => char.IsWhiteSpace(@this);

        public static bool IsLetterOrDigit(this char @this) => char.IsLetterOrDigit(@this);

        public static bool IsAnyOf(this char @this, params char[] characters) => characters.Contains(@this);
    }
}
