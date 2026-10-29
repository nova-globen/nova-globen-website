using System;
using System.Collections.Generic;

namespace NovaGloben.Website.Client.Modules.Contact
{
    /// <summary>The contact form's words in English and Swedish.</summary>
    public sealed class ContactText
    {
        public string Heading { get; private init; }
        public string Intro { get; private init; }
        public string Name { get; private init; }
        public string Email { get; private init; }
        public string Company { get; private init; }
        public string Optional { get; private init; }
        public string Topic { get; private init; }
        public string TopicNone { get; private init; }
        public string Message { get; private init; }
        public string MessageHint { get; private init; }
        public string Send { get; private init; }
        public string Privacy { get; private init; }
        public string PrivacyLink { get; private init; }
        public string NoScript { get; private init; }
        public string HoneypotLabel { get; private init; }
        public IReadOnlyDictionary<string, string> Topics { get; private init; }
        public IReadOnlyDictionary<string, string> Status { get; private init; }

        public static readonly ContactText English = new()
        {
            Heading = "Send us a message",
            Intro = "Fields marked * are required.",
            Name = "Name",
            Email = "Email",
            Company = "Company",
            Optional = "optional",
            Topic = "What is it about?",
            TopicNone = "Choose a topic",
            Message = "Message",
            MessageHint = "At least 10 characters.",
            Send = "Send message",
            Privacy = "We use your details only to answer your message. The website does not keep a copy; see our",
            PrivacyLink = "Privacy Policy",
            NoScript = "The form's spam check needs JavaScript. Without it, email us at",
            HoneypotLabel = "Leave this field empty",
            Topics = new Dictionary<string, string>
            {
                ["architecture"] = "Solution architecture and implementation",
                ["modernization"] = "Modernization and maintenance",
                ["integrations"] = "Integrations and automation",
                ["ai"] = "AI enablement",
                ["stomme"] = "Stomme",
                ["fravashi"] = "Fravashi",
                ["vacha"] = "Vacha: a question or an issue",
                ["didin"] = "Didin",
                ["general"] = "General question"
            },
            Status = new Dictionary<string, string>
            {
                ["sent"] = "Thank you. Your message has been sent, and we will reply as soon as possible.",
                ["invalid"] = "Your message could not be sent: some fields were not filled in correctly. Please check them and try again.",
                ["verify"] = "Your message could not be sent: the spam check did not complete. Please try again.",
                ["limit"] = "Your message could not be sent: too many messages were sent from your connection in a short time. Please try again later, or email us.",
                ["error"] = "Your message could not be sent because of an error on our side. Please try again later, or email us."
            }
        };

        public static readonly ContactText Swedish = new()
        {
            Heading = "Skicka ett meddelande",
            Intro = "Fält markerade med * är obligatoriska.",
            Name = "Namn",
            Email = "E-post",
            Company = "Företag",
            Optional = "valfritt",
            Topic = "Vad gäller det?",
            TopicNone = "Välj ett ämne",
            Message = "Meddelande",
            MessageHint = "Minst 10 tecken.",
            Send = "Skicka meddelande",
            Privacy = "Vi använder dina uppgifter bara för att svara på ditt meddelande. Webbplatsen sparar ingen kopia; se vår",
            PrivacyLink = "integritetspolicy",
            NoScript = "Formulärets skräppostkontroll kräver JavaScript. Utan det kan du mejla oss på",
            HoneypotLabel = "Lämna det här fältet tomt",
            Topics = new Dictionary<string, string>
            {
                ["architecture"] = "Lösningsarkitektur och implementation",
                ["modernization"] = "Modernisering och underhåll",
                ["integrations"] = "Integrationer och automatisering",
                ["ai"] = "AI i verksamheten",
                ["stomme"] = "Stomme",
                ["fravashi"] = "Fravashi",
                ["vacha"] = "Vacha: en fråga eller ett problem",
                ["didin"] = "Didin",
                ["general"] = "Allmän fråga"
            },
            Status = new Dictionary<string, string>
            {
                ["sent"] = "Tack. Ditt meddelande har skickats, och vi återkommer så snart som möjligt.",
                ["invalid"] = "Ditt meddelande kunde inte skickas: några fält var inte korrekt ifyllda. Kontrollera dem och försök igen.",
                ["verify"] = "Ditt meddelande kunde inte skickas: skräppostkontrollen blev inte klar. Försök igen.",
                ["limit"] = "Ditt meddelande kunde inte skickas: för många meddelanden har skickats från din anslutning på kort tid. Försök igen senare, eller mejla oss.",
                ["error"] = "Ditt meddelande kunde inte skickas på grund av ett fel hos oss. Försök igen senare, eller mejla oss."
            }
        };

        public static ContactText For(string cultureCode) =>
            cultureCode != null && cultureCode.StartsWith("sv", StringComparison.OrdinalIgnoreCase) ? Swedish : English;
    }
}
