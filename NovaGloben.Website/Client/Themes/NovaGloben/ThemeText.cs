using System;

namespace NovaGloben.Website.Client.Themes.NovaGloben
{
    /// <summary>
    /// The theme's own words in English and Swedish. The company facts are verified ones;
    /// the Swedish wording is the earlier site's own.
    /// </summary>
    public sealed class ThemeText
    {
        public string SkipToContent { get; private init; }
        public string Home { get; private init; }
        public string MainNavigation { get; private init; }
        public string MenuLabel { get; private init; }
        public string SiteFooter { get; private init; }
        public string ContactUs { get; private init; }
        public string Tagline { get; private init; }
        public string Country { get; private init; }
        public string Phone { get; private init; }
        public string OrgNumberLabel { get; private init; }
        public string VatLabel { get; private init; }
        public string ServicesHeading { get; private init; }
        public string CompanyHeading { get; private init; }
        public string AllRightsReserved { get; private init; }
        public string OtherLanguageName { get; private init; }
        public string OtherLanguageCode { get; private init; }
        public string SwitchLanguageLabel { get; private init; }

        public static readonly ThemeText English = new ThemeText
        {
            SkipToContent = "Skip to main content",
            Home = "home",
            MainNavigation = "Main navigation",
            MenuLabel = "Menu",
            SiteFooter = "Site footer",
            ContactUs = "Contact us",
            Tagline = "Nova Globen: did a bit.",
            Country = "Sweden",
            Phone = "+46 8 504 313 12",
            OrgNumberLabel = "Org. nr",
            VatLabel = "VAT",
            ServicesHeading = "Services",
            CompanyHeading = "Company",
            AllRightsReserved = "All rights reserved.",
            OtherLanguageName = "Svenska",
            OtherLanguageCode = "sv",
            SwitchLanguageLabel = "Svenska - byt till svenska"
        };

        public static readonly ThemeText Swedish = new ThemeText
        {
            SkipToContent = "Hoppa till huvudinnehåll",
            Home = "startsida",
            MainNavigation = "Huvudmeny",
            MenuLabel = "Meny",
            SiteFooter = "Sidfot",
            ContactUs = "Kontakta oss",
            Tagline = "Nova Globen: did a bit.", // the company's tagline, a name-pun kept in English
            Country = "Sverige",
            Phone = "08-504 313 12",
            OrgNumberLabel = "Org. nr",
            VatLabel = "Moms",
            ServicesHeading = "Tjänster",
            CompanyHeading = "Företag",
            AllRightsReserved = "Alla rättigheter förbehållna.",
            OtherLanguageName = "English",
            OtherLanguageCode = "en",
            SwitchLanguageLabel = "English - switch to English"
        };

        public static ThemeText For(string cultureCode)
        {
            return cultureCode != null && cultureCode.StartsWith("sv", StringComparison.OrdinalIgnoreCase) ? Swedish : English;
        }
    }
}
