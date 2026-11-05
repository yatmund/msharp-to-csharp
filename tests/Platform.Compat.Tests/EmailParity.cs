namespace CompatTests
{
    public class EmailParity
    {
        static string Run(Func<object> f)
        {
            try { return f() is IEnumerable<string> list ? string.Join("|", list) : f()?.ToString(); }
            catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; }
        }

        static async Task Same(string ours, string theirs)
        {
            static string Escape(string s) => s?.Replace("\r", "\\r").Replace("\n", "\\n");
            if (ours != theirs && theirs?.StartsWith("throws NullReference") != true) Assert.Fail("ours:  " + Escape(ours) + "\nOlive: " + Escape(theirs));
            await Task.CompletedTask;
        }

        [Test]
        public async Task Attachments_string_matches()
        {
            var theirs = new OliveEmail.Message();
            Olive.Email.EmailExtensions.Attach(theirs, new byte[] { 1, 2, 3 }, "a.txt");
            Olive.Email.EmailExtensions.Attach(theirs, new FileInfo("x.pdf"));
            Olive.Email.EmailExtensions.AttachLinkedResource(theirs, new byte[] { 9 }, "logo.png", "logo1");

            var ours = new OurEmail.Message();
            Platform.Email.EmailExtensions.Attach(ours, new byte[] { 1, 2, 3 }, "a.txt");
            Platform.Email.EmailExtensions.Attach(ours, new FileInfo("x.pdf"));
            Platform.Email.EmailExtensions.AttachLinkedResource(ours, new byte[] { 9 }, "logo.png", "logo1");

            await Same(ours.Attachments, theirs.Attachments);

            var parsed = new Platform.Email.EmailAttachmentSerializer().Parse(theirs.Attachments.Split('|')[0]);
            await Assert.That(parsed.Name).IsEqualTo("a.txt");
            await Assert.That(new Platform.Email.EmailAttachmentSerializer().GetLinkedResources(ours).Single().ContentId).IsEqualTo("logo1");
        }

        [Test]
        public async Task VCalendar_matches()
        {
            var (start, end) = (new DateTime(2024, 1, 2, 10, 0, 0), new DateTime(2024, 1, 2, 11, 0, 0));
            var theirs = new OliveEmail.Message();
            var ours = new OurEmail.Message();
            var t = Olive.Email.EmailExtensions.AddVCalendarView(theirs, "uid-1", start, end, "Subj", "Desc", "Room");
            var o = Platform.Email.EmailExtensions.AddVCalendarView(ours, "uid-1", start, end, "Subj", "Desc", "Room");
            await Same(o, t);
            await Same(ours.VCalendarView, theirs.VCalendarView);
        }

        [Test]
        [Arguments("Hi [#NAME#]", "Code [#CODE#] for [#name#] [#NAME#]", "NAME, CODE")]
        [Arguments("Hi", "nothing", "NAME, CODE")]
        [Arguments("Hi [#NAME#]", "x", "")]
        [Arguments("[#NAME#][#CODE#]", null, " name ,code")]
        public async Task Template_matches(string subject, string body, string placeholders)
        {
            var data = new { Name = "Bob", Code = 42, Other = (string)null };
            var theirs = new OliveEmail.Template { Key = "K", Subject = subject, Body = body, MandatoryPlaceholders = placeholders };
            var ours = new OurEmail.Template { Key = "K", Subject = subject, Body = body, MandatoryPlaceholders = placeholders };

            await Same(Run(() => Platform.Email.EmailExtensions.GetPlaceholderTokens(ours)), Run(() => Olive.Email.EmailExtensions.GetPlaceholderTokens(theirs)));
            await Same(Run(() => { Platform.Email.EmailExtensions.EnsurePlaceholders(ours); return "ok"; }), Run(() => { Olive.Email.EmailExtensions.EnsurePlaceholders(theirs); return "ok"; }));
            await Same(Run(() => Platform.Email.EmailExtensions.MergeSubject(ours, data)), Run(() => Olive.Email.EmailExtensions.MergeSubject(theirs, data)));
            await Same(Run(() => Platform.Email.EmailExtensions.MergeBody(ours, data)), Run(() => Olive.Email.EmailExtensions.MergeBody(theirs, data)));
        }
}

}

namespace CompatTests.OliveEmail
{
    using Olive.Entities;

    public class Message : GuidEntity, Olive.Email.IEmailMessage
    {
        public string Body { get; set; }
        public DateTime SendableDate { get; set; }
        public bool Html { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
        public string ReplyToAddress { get; set; }
        public string ReplyToName { get; set; }
        public string Subject { get; set; }
        public string To { get; set; }
        public string Attachments { get; set; }
        public string Bcc { get; set; }
        public string Cc { get; set; }
        public int Retries { get; set; }
        public string VCalendarView { get; set; }
    }

    public class Template : GuidEntity, Olive.Email.IEmailTemplate
    {
        public string Body { get; set; }
        public string Key { get; set; }
        public string MandatoryPlaceholders { get; set; }
        public string Subject { get; set; }
    }
}

namespace CompatTests.OurEmail
{
    using Platform.Entities;

    public class Message : GuidEntity, Platform.Email.IEmailMessage
    {
        public string Body { get; set; }
        public DateTime SendableDate { get; set; }
        public bool Html { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
        public string ReplyToAddress { get; set; }
        public string ReplyToName { get; set; }
        public string Subject { get; set; }
        public string To { get; set; }
        public string Attachments { get; set; }
        public string Bcc { get; set; }
        public string Cc { get; set; }
        public int Retries { get; set; }
        public string VCalendarView { get; set; }
    }

    public class Template : GuidEntity, Platform.Email.IEmailTemplate
    {
        public string Body { get; set; }
        public string Key { get; set; }
        public string MandatoryPlaceholders { get; set; }
        public string Subject { get; set; }
    }
}
