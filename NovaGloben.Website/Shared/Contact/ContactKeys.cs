namespace NovaGloben.Website.Contact
{
    /// <summary>
    /// The contact form's contract between its page (Client/Modules/Contact) and its handler (Server/Contact): the post
    /// address, the field names, and the status the handler sends the visitor back with. A status carries no personal
    /// data - nothing a visitor typed ever goes into an address.
    /// </summary>
    public static class ContactKeys
    {
        /// <summary>The form posts here, below the site's root ("/contact-form" or "/sv/contact-form").</summary>
        public const string PostPath = "contact-form";

        public const string Name = "name";
        public const string Email = "email";
        public const string Company = "company";
        public const string Topic = "topic";
        public const string Message = "message";

        /// <summary>The honeypot: hidden from people, filled in by simple bots. A post that fills it is dropped.</summary>
        public const string Honeypot = "website";

        /// <summary>The Turnstile widget's own field.</summary>
        public const string TurnstileResponse = "cf-turnstile-response";

        public const string StatusParameter = "status";
        public const string StatusSent = "sent";
        public const string StatusInvalid = "invalid";
        public const string StatusVerify = "verify";
        public const string StatusLimit = "limit";
        public const string StatusError = "error";

        public const int NameMax = 100;
        public const int EmailMax = 200;
        public const int CompanyMax = 150;
        public const int MessageMin = 10;
        public const int MessageMax = 5000;

        /// <summary>The module setting the site reconciliation writes from configuration: the public Turnstile site key.</summary>
        public const string TurnstileSiteKeySetting = "NovaGloben:TurnstileSiteKey";

        /// <summary>
        /// The topics a visitor may pick (the four services, the four products and "general"); anything else is refused. The contact page
        /// preselects one named in its address (<c>/contact?topic=vacha</c>), which is how a product links to the form.
        /// </summary>
        public static readonly string[] Topics =
        {
            "architecture", "modernization", "integrations", "ai", "stomme", "fravashi", "vacha", "didin", "general"
        };
    }
}
