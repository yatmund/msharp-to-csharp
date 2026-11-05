using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Platform.Entities;

namespace Platform.Email
{
    public class Constants
    {
        public const string EMAIL_MESSAGE_ID_HEADER_KEY = "X-Olive-Email-Item-Id";
    }

    public interface IEmailMessage : IEntity
    {
        string Body { get; set; }
        DateTime SendableDate { get; set; }
        bool Html { get; set; }
        string FromAddress { get; set; }
        string FromName { get; set; }
        string ReplyToAddress { get; set; }
        string ReplyToName { get; set; }
        string Subject { get; set; }
        string To { get; set; }
        string Attachments { get; set; }
        string Bcc { get; set; }
        string Cc { get; set; }
        int Retries { get; set; }
        string VCalendarView { get; set; }
    }

    public interface IEmailTemplate : IEntity
    {
        string Body { get; set; }
        string Key { get; set; }
        string MandatoryPlaceholders { get; set; }
        string Subject { get; set; }
    }

    public class EmailConfiguration
    {
        public bool EnableSsl { get; set; }
        public int SmtpPort { get; set; } = 25;
        public int MaxRetries { get; set; } = 4;
        public string AutoAddCc { get; set; }
        public string AutoAddBcc { get; set; }
        public string SmtpHost { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public MailAddress From { get; set; }
        public MailAddress ReplyTo { get; set; }
        public WhiteListSettings Permitted { get; set; }

        public class MailAddress
        {
            public string Address { get; set; }
            public string Name { get; set; }
        }

        public class WhiteListSettings
        {
            public string Domains { get; set; }
            public string Addresses { get; set; }
        }

        internal static EmailConfiguration From_(IConfiguration config)
        {
            var result = new EmailConfiguration();
            config.GetSection("Email").Bind(result);
            return result;
        }
    }

    public class EmailSendingEventArgs
    {
        public EmailSendingEventArgs(IEmailMessage item, MailMessage message)
        {
            Item = item;
            MailMessage = message;
        }

        public MailMessage MailMessage { get; }
        public IEmailMessage Item { get; }
        public Exception Error { get; internal set; }
    }

    public interface IEmailAttachmentSerializer
    {
        Task<IEnumerable<Attachment>> Extract(IEmailMessage message);
        Attachment Parse(string attachmentInfo);
        IEnumerable<LinkedResource> GetLinkedResources(IEmailMessage message);
    }

    // Olive's stored format, so messages queued before the switch still send.
    public class EmailAttachmentSerializer : IEmailAttachmentSerializer
    {
        internal class Entry
        {
            public string Contents { get; set; }
            public string Name { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string ContentId { get; set; }
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool IsLinkedResource { get; set; }
        }

        static IEnumerable<string> Entries(IEmailMessage message) => (message.Attachments ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

        static Entry Read(string info) => info.TrimStart().StartsWith("{") ? JsonConvert.DeserializeObject<Entry>(info) : null;

        public Task<IEnumerable<Attachment>> Extract(IEmailMessage message) =>
            Task.FromResult<IEnumerable<Attachment>>(Entries(message).Where(e => Read(e)?.IsLinkedResource != true).Select(Parse).Where(a => a != null).ToList());

        public Attachment Parse(string attachmentInfo)
        {
            var entry = Read(attachmentInfo);
            if (entry != null) return new Attachment(new MemoryStream(Convert.FromBase64String(entry.Contents)), entry.Name) { ContentId = entry.ContentId };
            var path = Path.Combine(WebsiteRoot, attachmentInfo);
            return File.Exists(path) ? new Attachment(path) : null;
        }

        public IEnumerable<LinkedResource> GetLinkedResources(IEmailMessage message) =>
            Entries(message).Select(Read).Where(e => e?.IsLinkedResource == true)
                .Select(e => new LinkedResource(new MemoryStream(Convert.FromBase64String(e.Contents))) { ContentId = e.ContentId, ContentType = new ContentType(MimeOf(e.Name)) }).ToList();

        // Olive stores file paths relative to app root, which is above bin/Debug/netX when run from a build.
        internal static string WebsiteRoot
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                var match = Regex.Match(dir, @"^(.*?)[\\/]bin[\\/](Debug|Release)[\\/]net[^\\/]*[\\/]?$", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : dir;
            }
        }

        static string MimeOf(string name) => new Blob(name).GetMimeType();
    }

    public interface IMailMessageCreator
    {
        Task<MailMessage> Create(IEmailMessage message);
    }

    sealed class MailMessageCreator : IMailMessageCreator
    {
        readonly EmailConfiguration config;
        readonly IEmailAttachmentSerializer attachments;

        public MailMessageCreator(IConfiguration config, IEmailAttachmentSerializer attachments)
        {
            this.config = EmailConfiguration.From_(config);
            this.attachments = attachments;
        }

        static IEnumerable<string> Addresses(string list) => (list ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).Where(a => a.Length > 0);

        public async Task<MailMessage> Create(IEmailMessage message)
        {
            var mail = new MailMessage
            {
                Subject = message.Subject?.Replace("\r", " ").Replace("\n", " "),
                IsBodyHtml = message.Html,
                From = new System.Net.Mail.MailAddress(message.FromAddress.Or(config.From?.Address), message.FromName.Or(config.From?.Name)),
            };
            foreach (var to in Addresses(message.To)) mail.To.Add(to);
            foreach (var cc in Addresses(message.Cc).Concat(Addresses(config.AutoAddCc))) mail.CC.Add(cc);
            foreach (var bcc in Addresses(message.Bcc).Concat(Addresses(config.AutoAddBcc))) mail.Bcc.Add(bcc);
            var replyTo = message.ReplyToAddress.Or(config.ReplyTo?.Address);
            if (!string.IsNullOrEmpty(replyTo)) mail.ReplyToList.Add(new System.Net.Mail.MailAddress(replyTo, message.ReplyToName.Or(config.ReplyTo?.Name)));
            mail.Headers.Add(Constants.EMAIL_MESSAGE_ID_HEADER_KEY, message.GetId()?.ToString());

            var view = AlternateView.CreateAlternateViewFromString(message.Body ?? string.Empty, Encoding.UTF8, message.Html ? MediaTypeNames.Text.Html : MediaTypeNames.Text.Plain);
            foreach (var resource in attachments.GetLinkedResources(message)) view.LinkedResources.Add(resource);
            mail.AlternateViews.Add(view);
            if (!string.IsNullOrEmpty(message.VCalendarView))
                mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.VCalendarView, new ContentType("text/calendar; method=REQUEST")));
            foreach (var attachment in await attachments.Extract(message)) mail.Attachments.Add(attachment);
            return mail;
        }
    }

    public interface IEmailDispatcher
    {
        Task Dispatch(MailMessage mail, IEmailMessage message);
    }

    public class EmailDispatcher : IEmailDispatcher
    {
        readonly EmailConfiguration config;

        public EmailDispatcher(IConfiguration config) => this.config = EmailConfiguration.From_(config);

        public async Task Dispatch(MailMessage mail, IEmailMessage _)
        {
            using (var smtp = new SmtpClient(config.SmtpHost, config.SmtpPort) { EnableSsl = config.EnableSsl })
            {
                if (!string.IsNullOrEmpty(config.Username)) smtp.Credentials = new NetworkCredential(config.Username, config.Password);
                await smtp.SendMailAsync(mail);
            }
        }
    }

    public interface IEmailRepository
    {
        Task<IEnumerable<IEmailMessage>> GetUnsentEmails();
        Task<IEnumerable<T>> GetSentEmails<T>() where T : IEmailMessage;
        Task RecordEmailSent(IEmailMessage message);
        Task RecordRetry(IEmailMessage message);
        Task SaveForFutureSend(IEmailMessage message);
    }

    public class EmailRepository : IEmailRepository
    {
        static readonly List<IEmailMessage> SentInThisProcess = new List<IEmailMessage>();
        readonly EmailConfiguration config;

        public EmailRepository(IConfiguration config) => this.config = EmailConfiguration.From_(config);

        static IDatabase Database => Context.Current.Database();

        static Type MessageType => EntityFinder.FindPossibleTypes(typeof(IEmailMessage), mustFind: true).First(t => !t.IsAbstract);

        public async Task<IEnumerable<IEmailMessage>> GetUnsentEmails() =>
            (await Database.Of(MessageType).GetList()).Cast<IEmailMessage>()
                .Where(m => m.SendableDate <= LocalTime.Now && m.Retries < config.MaxRetries)
                .OrderBy(m => m.SendableDate).ToList();

        public Task<IEnumerable<T>> GetSentEmails<T>() where T : IEmailMessage
        {
            lock (SentInThisProcess) return Task.FromResult<IEnumerable<T>>(SentInThisProcess.OfType<T>().ToList());
        }

        public async Task RecordEmailSent(IEmailMessage message)
        {
            lock (SentInThisProcess) SentInThisProcess.Add(message);
            if (!message.IsNew) await Database.Delete(message);
        }

        public async Task RecordRetry(IEmailMessage message)
        {
            if (message.IsNew) return;
            await Database.Update(message, m => m.Retries++);
        }

        public Task SaveForFutureSend(IEmailMessage message) => Database.Save(message);
    }

    public interface IEmailOutbox
    {
        Task SendAll(TimeSpan? delayPerSend = null);
        Task<bool> Send(IEmailMessage message);
        Task<IEnumerable<T>> GetSentEmails<T>() where T : IEmailMessage;
        event AwaitableEventHandler<EmailSendingEventArgs> Sending;
        event AwaitableEventHandler<EmailSendingEventArgs> Sent;
        event AwaitableEventHandler<EmailSendingEventArgs> SendError;
    }

    public class EmailOutbox : IEmailOutbox
    {
        readonly ILogger<EmailOutbox> log;
        readonly EmailConfiguration config;

        public EmailOutbox(IEmailDispatcher dispatcher, IMailMessageCreator messageCreator, IEmailRepository repository, ILogger<EmailOutbox> log)
        {
            Dispatcher = dispatcher;
            MessageCreator = messageCreator;
            Repository = repository;
            this.log = log;
            config = EmailConfiguration.From_(Context.Current.Config);
        }

        public IEmailDispatcher Dispatcher { get; }
        public IMailMessageCreator MessageCreator { get; }
        public IEmailRepository Repository { get; }

        public event AwaitableEventHandler<EmailSendingEventArgs> Sending;
        public event AwaitableEventHandler<EmailSendingEventArgs> Sent;
        public event AwaitableEventHandler<EmailSendingEventArgs> SendError;

        public async Task SendAll(TimeSpan? delayPerSend = null)
        {
            foreach (var message in await Repository.GetUnsentEmails())
            {
                await Send(message);
                if (delayPerSend.HasValue) await Task.Delay(delayPerSend.Value);
            }
        }

        public Task<IEnumerable<T>> GetSentEmails<T>() where T : IEmailMessage => Repository.GetSentEmails<T>();

        // Outside the whitelist (Email:Permitted), messages are recorded as sent but never leave the server.
        bool IsPermitted(string address)
        {
            var permitted = config.Permitted;
            if (permitted == null || (string.IsNullOrEmpty(permitted.Domains) && string.IsNullOrEmpty(permitted.Addresses))) return true;
            var domains = (permitted.Domains ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim());
            var addresses = (permitted.Addresses ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim());
            return addresses.Contains(address, StringComparer.OrdinalIgnoreCase) || domains.Any(d => address.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> Send(IEmailMessage message)
        {
            if (message.SendableDate > LocalTime.Now)
            {
                await Repository.SaveForFutureSend(message);
                return false;
            }

            MailMessage mail = null;
            try
            {
                mail = await MessageCreator.Create(message);
                var args = new EmailSendingEventArgs(message, mail);
                await Sending.Raise(args);
                if (mail.To.Concat(mail.CC).Concat(mail.Bcc).All(a => IsPermitted(a.Address))) await Dispatcher.Dispatch(mail, message);
                else log?.LogWarning($"Email to {message.To} was not sent: the address isn't in Email:Permitted.");
                await Repository.RecordEmailSent(message);
                await Sent.Raise(args);
                return true;
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Error in sending an email for this EmailQueueItem of '" + message.GetId() + "'");
                await SendError.Raise(new EmailSendingEventArgs(message, mail) { Error = ex });
                await Repository.RecordRetry(message);
                return false;
            }
            finally
            {
                mail?.Dispose();
            }
        }
    }

    public class EmailTestDevCommand : IDevCommand
    {
        readonly IEmailAttachmentSerializer attachmentSerializer;
        readonly IMailMessageCreator messageCreator;

        public EmailTestDevCommand(IEmailAttachmentSerializer attachmentSerializer, IMailMessageCreator messageCreator)
        {
            this.attachmentSerializer = attachmentSerializer;
            this.messageCreator = messageCreator;
        }

        public string Name => "outbox";

        public string Title => "Outbox...";

        public bool IsEnabled() => !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

        public Task Initialize() => Task.CompletedTask;

        public Task<string> Process() => Task.FromResult<string>(null);

        public async Task<string> Run()
        {
            await Initialize();
            return await Process();
        }
    }

    public static class EmailExtensions
    {
        public static IServiceCollection AddEmail(this IServiceCollection @this) => @this
            .AddSingleton<IEmailAttachmentSerializer, EmailAttachmentSerializer>()
            .AddSingleton<IMailMessageCreator, MailMessageCreator>()
            .AddSingleton<IEmailDispatcher, EmailDispatcher>()
            .AddSingleton<IEmailRepository, EmailRepository>()
            .AddSingleton<IEmailOutbox, EmailOutbox>();

        static void Add(IEmailMessage message, string entry) =>
            message.Attachments = string.IsNullOrEmpty(message.Attachments) ? entry : message.Attachments + "|" + entry;

        static string Json(byte[] data, string name, string contentId = null, bool linked = false) =>
            JsonConvert.SerializeObject(new EmailAttachmentSerializer.Entry { Contents = Convert.ToBase64String(data), Name = name, ContentId = contentId, IsLinkedResource = linked });

        public static async Task Attach(this IEmailMessage @this, Blob file) => Add(@this, Json(await file.GetFileDataAsync(), file.FileName));

        public static void Attach(this IEmailMessage @this, FileInfo file) => Add(@this, Path.GetRelativePath(EmailAttachmentSerializer.WebsiteRoot, file.FullName));

        public static void Attach(this IEmailMessage @this, byte[] fileData, string name) => Add(@this, Json(fileData, name));

        public static void AttachLinkedResource(this IEmailMessage @this, byte[] fileData, string name, string contentId) => Add(@this, Json(fileData, name, contentId, linked: true));

        public static string AddVCalendarView(this IEmailMessage @this, string meetingUniqueIdentifier, DateTime start, DateTime end, string subject, string description, string location)
        {
            const string format = "yyyyMMdd'T'HHmmss'Z'";
            var lines = new[]
            {
                "BEGIN:VCALENDAR", "PRODID:-//Microsoft Corporation//Outlook 12.0 MIMEDIR//EN", "VERSION:1.0", "BEGIN:VEVENT",
                "DTSTART:" + start.ToString(format), "DTEND:" + end.ToString(format), "UID:" + meetingUniqueIdentifier,
                "SUMMARY:" + subject, "LOCATION:" + location, "DESCRIPTION:" + description, "END:VEVENT", "END:VCALENDAR",
            };
            return @this.VCalendarView = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        public static IEmailMessage SendAsNotification(this Exception error) => error.SendAsNotification(Config.Get("Email:ErrorNotificationRecipient"));

        public static IEmailMessage SendAsNotification(this Exception error, string toNotify)
        {
            var type = EntityFinder.FindPossibleTypes(typeof(IEmailMessage), mustFind: true).First(t => !t.IsAbstract);
            var message = (IEmailMessage)Activator.CreateInstance(type);
            message.Html = true;
            message.Subject = "Error In Application";
            // ponytail: no request URL/IP/user in the body, keeps Email free of ASP.NET.
            message.Body = error.ToString().HtmlEncode().Replace("\n", "<BR>");
            message.To = toNotify;
            message.SendableDate = LocalTime.Now;
            Context.Current.Database().Save(message).GetAwaiter().GetResult();
            return message;
        }

        static readonly Regex Token = new Regex(@"\[#[A-Z0-9_]+#\]");

        public static IEnumerable<string> GetPlaceholderTokens(this IEmailTemplate template) =>
            (template.MandatoryPlaceholders ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => "[#" + p.Trim().ToUpperInvariant() + "#]");

        public static void EnsurePlaceholders(this IEmailTemplate template)
        {
            var text = template.Subject + template.Body;
            var missing = template.GetPlaceholderTokens().Where(t => !text.Contains(t)).ToArray();
            if (missing.Any())
                throw new ValidationException($"Email template subject or body must have all place-holders for {template.Key}. The missing ones are: {string.Join(", ", missing)}");
        }

        public static string MergeSubject(this IEmailTemplate template, object mergeData) => Merge(template.Subject, mergeData);

        public static string MergeBody(this IEmailTemplate template, object mergeData) => Merge(template.Body, mergeData);

        static string Merge(string text, object data)
        {
            if (text == null || data == null) return text;
            foreach (var property in data.GetType().GetProperties())
                text = text.Replace("[#" + property.Name.ToUpperInvariant() + "#]", property.GetValue(data)?.ToString() ?? string.Empty);
            return text;
        }
    }
}
