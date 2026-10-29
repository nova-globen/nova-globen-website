namespace NovaGloben.Website
{
    /// <summary>
    /// The names the server's site reconciliation writes and the theme and modules read: module definition names and
    /// the NovaGloben:* settings on sites, pages and modules.
    /// </summary>
    public static class WebsiteKeys
    {
        public const string ClientAssembly = "NovaGloben.Website.Client.Stomme";

        /// <summary>The page-content module (Client/Modules/PageContent).</summary>
        public const string PageContentModule = "NovaGloben.Website.Client.Modules.PageContent, " + ClientAssembly;

        /// <summary>The articles module (Client/Modules/Articles).</summary>
        public const string ArticlesModule = "NovaGloben.Website.Client.Modules.Articles, " + ClientAssembly;

        /// <summary>The contact form module (Client/Modules/Contact).</summary>
        public const string ContactModule = "NovaGloben.Website.Client.Modules.Contact, " + ClientAssembly;

        public const string ThemeType = "NovaGloben.Website.Client.Themes.NovaGloben.Default, " + ClientAssembly;
        public const string ContainerType = "NovaGloben.Website.Client.Themes.NovaGloben.Container, " + ClientAssembly;

        // Site settings.
        public const string SiteKey = "NovaGloben:SiteKey";

        // Page settings (the theme renders the head from them).
        public const string Managed = "NovaGloben:Managed";
        public const string Description = "NovaGloben:Description";
        public const string CanonicalPath = "NovaGloben:CanonicalPath";
        public const string OgType = "NovaGloben:OgType";
        public const string OgImage = "NovaGloben:OgImage";
        public const string JsonLd = "NovaGloben:JsonLd";
        public const string NoIndex = "NovaGloben:NoIndex";

        // Module settings.
        public const string ContentKey = "NovaGloben:Content";
        public const string ArticlesView = "NovaGloben:ArticlesView";
        public const string ArticlesCount = "NovaGloben:Count";
        public const string ArticleSlug = "NovaGloben:ArticleSlug";
    }
}
