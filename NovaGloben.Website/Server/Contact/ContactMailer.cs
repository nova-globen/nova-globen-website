#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stomme.Repository;
using Stomme.Shared;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace NovaGloben.Website.Contact
{
    /// <summary>
    /// Sends a contact message as one email to the company's inbox (Contact:Recipient, info@nova-globen.se), through the
    /// SMTP settings of the site it was sent from - the same SMTPHost, SMTPPort, SMTPSSL, SMTPUsername, SMTPPassword and
    /// SMTPSender settings Stomme's own mail uses, written by the site reconciliation from configuration. It is sent
    /// while the visitor waits, so the page can say whether it went; and it is not stored anywhere on the
    /// site: Stomme's notification queue, which keeps a copy, is not used.
    /// </summary>
    public sealed class ContactMailer(ISettingRepository settings, IConfiguration configuration, ILogger<ContactMailer> logger)
    {
        public const string DefaultRecipient = "info@nova-globen.se";

        public async Task<bool> SendAsync(int siteId, ContactMessage message, CancellationToken ct)
        {
            var site = (await settings.GetSettingsAsync(EntityNames.Site, siteId)).ToDictionary(s => s.SettingName, s => s.SettingValue ?? "", StringComparer.Ordinal);
            string Setting(string name) => site.TryGetValue(name, out var value) ? value : "";

            var host = Setting("SMTPHost");
            var sender = Setting("SMTPSender");
            if (host.Length == 0 || !int.TryParse(Setting("SMTPPort"), out var port) || !MailboxAddress.TryParse(sender, out var from))
            {
                logger.LogError("Contact form: site {SiteId} has no complete SMTP settings (host, port, sender); the message was not sent.", siteId);
                return false;
            }
            from.Name = "Nova Globen AB website";

            var recipient = configuration["Contact:Recipient"];
            if (!MailboxAddress.TryParse(string.IsNullOrWhiteSpace(recipient) ? DefaultRecipient : recipient, out var to))
            {
                logger.LogError("Contact form: Contact:Recipient is not an email address; the message was not sent.");
                return false;
            }

            var mail = Compose(from, to, message, configuration["Contact:SubjectPrefix"]);
            try
            {
                using var client = new SmtpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await client.ConnectAsync(host, port, Ssl(Setting("SMTPSSL")), timeout.Token);
                var user = Setting("SMTPUsername");
                if (user.Length > 0)
                {
                    await client.AuthenticateAsync(user, Setting("SMTPPassword"), timeout.Token);
                }
                await client.SendAsync(mail, timeout.Token);
                await client.DisconnectAsync(true, timeout.Token);
                logger.LogInformation("Contact form: a message from site {SiteId} was handed to {Host}.", siteId, host);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The message itself is not logged: it is personal data the site does not keep.
                logger.LogError(ex, "Contact form: sending through {Host}:{Port} failed.", host, port);
                return false;
            }
        }

        /// <summary>
        /// The email: from the site's sender, to the inbox, Reply-To the visitor, as plain text. A subject prefix
        /// (Contact:SubjectPrefix, set on a testing host) marks where the message came from.
        /// </summary>
        public static MimeMessage Compose(MailboxAddress from, MailboxAddress to, ContactMessage message, string? subjectPrefix = null)
        {
            var mail = new MimeMessage { MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(from.Domain) };
            mail.From.Add(from);
            mail.To.Add(to);
            mail.ReplyTo.Add(new MailboxAddress(message.Name, message.Email));
            var who = message.Company.Length > 0 ? $"{message.Name} ({message.Company})" : message.Name;
            var prefix = string.IsNullOrWhiteSpace(subjectPrefix) ? "" : subjectPrefix.Replace('\r', ' ').Replace('\n', ' ').Trim() + " ";
            mail.Subject = $"{prefix}Website contact: {who}";

            var body = new StringBuilder()
                .AppendLine("A message from the contact form on nova-globen.se.")
                .AppendLine("Reply to this email to answer the sender.")
                .AppendLine()
                .AppendLine($"Name:     {message.Name}")
                .AppendLine($"Email:    {message.Email}")
                .AppendLine($"Company:  {(message.Company.Length > 0 ? message.Company : "-")}")
                .AppendLine($"Topic:    {(message.Topic.Length > 0 ? message.Topic : "-")}")
                .AppendLine($"Language: {message.Language}")
                .AppendLine()
                .AppendLine(message.Message);
            mail.Body = new TextPart("plain") { Text = body.ToString() };
            return mail;
        }

        private static SecureSocketOptions Ssl(string value) => value switch
        {
            "None" => SecureSocketOptions.None,
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" or "True" => SecureSocketOptions.SslOnConnect,
            "StartTlsWhenAvailable" or "False" => SecureSocketOptions.StartTlsWhenAvailable,
            _ => SecureSocketOptions.Auto
        };
    }
}
