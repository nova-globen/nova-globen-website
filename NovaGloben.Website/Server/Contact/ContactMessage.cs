#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace NovaGloben.Website.Contact
{
    /// <summary>
    /// A contact-form post, read and validated on the server. The page's HTML attributes check the same
    /// rules in the browser, but only this decides. Text is trimmed and stripped of control characters (line breaks stay in
    /// the message), so nothing a visitor types can add a mail header.
    /// </summary>
    public sealed record ContactMessage(string Name, string Email, string Company, string Topic, string Message, string Language)
    {
        private static readonly Regex EmailPattern = new(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]{2,}$", RegexOptions.CultureInvariant);
        private static readonly Regex ControlCharacters = new(@"[\u0000-\u0009\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.CultureInvariant);

        /// <summary>The message, or the names of the fields that are not valid (empty when it is).</summary>
        public static (ContactMessage? Message, IReadOnlyList<string> Invalid) Read(IFormCollection form, string language)
        {
            var name = Single(form, ContactKeys.Name);
            var email = Single(form, ContactKeys.Email);
            var company = Single(form, ContactKeys.Company);
            var topic = Single(form, ContactKeys.Topic);
            var message = Text(form, ContactKeys.Message);

            var invalid = new List<string>();
            if (name.Length is 0 or > ContactKeys.NameMax) invalid.Add(ContactKeys.Name);
            if (email.Length > ContactKeys.EmailMax || !EmailPattern.IsMatch(email)) invalid.Add(ContactKeys.Email);
            if (company.Length > ContactKeys.CompanyMax) invalid.Add(ContactKeys.Company);
            if (topic.Length > 0 && !ContactKeys.Topics.Contains(topic, StringComparer.Ordinal)) invalid.Add(ContactKeys.Topic);
            if (message.Length < ContactKeys.MessageMin || message.Length > ContactKeys.MessageMax) invalid.Add(ContactKeys.Message);

            return invalid.Count == 0
                ? (new ContactMessage(name, email, company, topic, message, language), invalid)
                : (null, invalid);
        }

        /// <summary>One line: trimmed, every control character (line breaks included) replaced by a space.</summary>
        private static string Single(IFormCollection form, string key) =>
            Regex.Replace(ControlCharacters.Replace(form[key].ToString(), " "), @"[\r\n]+", " ").Trim();

        /// <summary>Several lines: trimmed, control characters other than line breaks removed, line breaks normalised.</summary>
        private static string Text(IFormCollection form, string key) =>
            ControlCharacters.Replace(form[key].ToString().Replace("\r\n", "\n").Replace('\r', '\n'), "").Trim();
    }
}
