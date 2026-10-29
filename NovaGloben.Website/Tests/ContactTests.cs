using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using MimeKit;
using NovaGloben.Website.Contact;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>The contact form's server side: what it accepts, what it refuses, and the email it writes.</summary>
public class ContactTests
{
    private static IFormCollection Form(string name = "Ada Lovelace", string email = "ada@nova-globen.test", string company = "",
        string topic = "", string message = "I would like to discuss a project.") =>
        new FormCollection(new Dictionary<string, StringValues>
        {
            [ContactKeys.Name] = name,
            [ContactKeys.Email] = email,
            [ContactKeys.Company] = company,
            [ContactKeys.Topic] = topic,
            [ContactKeys.Message] = message
        });

    [Fact]
    public void A_complete_message_is_accepted()
    {
        var (message, invalid) = ContactMessage.Read(Form(company: "Analytical Engines", topic: "ai"), "en");

        Assert.Empty(invalid);
        Assert.NotNull(message);
        Assert.Equal("Analytical Engines", message!.Company);
        Assert.Equal("ai", message.Topic);
    }

    [Theory]
    [InlineData("", "a@b.test", "Long enough message", "name")]
    [InlineData("Ada", "not-an-address", "Long enough message", "email")]
    [InlineData("Ada", "a@b", "Long enough message", "email")]
    [InlineData("Ada", "a@b.test", "short", "message")]
    public void A_missing_or_wrong_field_is_refused(string name, string email, string text, string field)
    {
        var (message, invalid) = ContactMessage.Read(Form(name: name, email: email, message: text), "en");

        Assert.Null(message);
        Assert.Contains(field, invalid);
    }

    [Fact]
    public void An_unknown_topic_and_overlong_fields_are_refused()
    {
        Assert.Contains(ContactKeys.Topic, ContactMessage.Read(Form(topic: "free-money"), "en").Invalid);
        Assert.Contains(ContactKeys.Name, ContactMessage.Read(Form(name: new string('a', ContactKeys.NameMax + 1)), "en").Invalid);
        Assert.Contains(ContactKeys.Message, ContactMessage.Read(Form(message: new string('a', ContactKeys.MessageMax + 1)), "en").Invalid);
    }

    [Theory]
    [InlineData(null, "Website contact: Ada Lovelace")]
    [InlineData("", "Website contact: Ada Lovelace")]
    [InlineData("[Test]", "[Test] Website contact: Ada Lovelace")]
    [InlineData("[Test]\r\nBcc: x@nova-globen.test", "[Test]  Bcc: x@nova-globen.test Website contact: Ada Lovelace")]
    public void A_subject_prefix_marks_a_testing_host(string? prefix, string subject)
    {
        var (message, _) = ContactMessage.Read(Form(), "en");
        var mail = ContactMailer.Compose(new MailboxAddress("Site", "website@nova-globen.test"), new MailboxAddress("", "info@nova-globen.se"), message!, prefix);

        Assert.Equal(subject, mail.Subject);
        Assert.Empty(mail.Bcc);
    }

    [Fact]
    public void A_line_break_cannot_reach_a_header()
    {
        var (message, _) = ContactMessage.Read(Form(name: "Ada\r\nBcc: victim@nova-globen.test", company: "X\nY"), "en");
        var mail = ContactMailer.Compose(new MailboxAddress("Site", "website@nova-globen.test"), new MailboxAddress("", "info@nova-globen.se"), message!);

        Assert.DoesNotContain("\n", mail.Subject);
        Assert.Empty(mail.Bcc);
        Assert.Equal("Ada Bcc: victim@nova-globen.test", message!.Name);
    }

    [Fact]
    public void The_email_goes_to_the_inbox_with_the_sender_as_reply_to_in_plain_text()
    {
        var (message, _) = ContactMessage.Read(Form(company: "Engines"), "sv");
        var mail = ContactMailer.Compose(new MailboxAddress("Site", "website@nova-globen.test"), new MailboxAddress("", "info@nova-globen.se"), message!);

        Assert.Equal("info@nova-globen.se", ((MailboxAddress)mail.To.Single()).Address);
        Assert.Equal("ada@nova-globen.test", ((MailboxAddress)mail.ReplyTo.Single()).Address);
        Assert.Equal("Website contact: Ada Lovelace (Engines)", mail.Subject);
        Assert.IsType<TextPart>(mail.Body);
        Assert.Equal("plain", ((TextPart)mail.Body).ContentType.MediaSubtype);
        Assert.Contains("Language: sv", ((TextPart)mail.Body).Text);
    }

    [Theory]
    [InlineData("/contact-form", "")]
    [InlineData("/sv/contact-form", "sv")]
    [InlineData("/contact", null)]
    [InlineData("/other/contact-form", null)]
    public void The_handler_answers_only_its_own_paths(string path, string? folder) => Assert.Equal(folder, ContactEndpoint.Folder(path));

    [Fact]
    public void A_vacha_message_is_accepted()
    {
        var (message, invalid) = ContactMessage.Read(Form(topic: "vacha"), "en");

        Assert.Empty(invalid);
        Assert.Equal("vacha", message!.Topic);
    }

    [Theory]
    [InlineData("/products/vacha/issues", "/contact?topic=vacha")]
    [InlineData("/products/vacha/issues/", "/contact?topic=vacha")]
    [InlineData("/sv/products/vacha/issues", "/sv/contact?topic=vacha")]
    [InlineData("/products/stomme/issues", "/contact?topic=stomme")]
    [InlineData("/products/fravashi/issues", "/contact?topic=fravashi")]
    [InlineData("/products/vacha/licence", "https://github.com/nova-globen/vacha/blob/main/LICENSE.txt")]
    [InlineData("/products/vacha/license", "https://github.com/nova-globen/vacha/blob/main/LICENSE.txt")]
    [InlineData("/products/vacha/privacy", "https://github.com/nova-globen/vacha/blob/main/PRIVACY.md")]
    [InlineData("/products/stomme/licence", "https://github.com/nova-globen/stomme/blob/main/LICENSE.md")]
    [InlineData("/products/stomme/privacy", null)] // none in Stomme's public repository
    [InlineData("/products/fravashi/licence", null)] // no public repository yet
    // Installed copies: Vacha 1.1.0 and 1.1.1 under /vacha, Tond 1.0.0 under /tond - straight to the target, one hop.
    [InlineData("/vacha/issues", "/contact?topic=vacha")]
    [InlineData("/sv/vacha/issues", "/sv/contact?topic=vacha")]
    [InlineData("/tond/issues", "/contact?topic=vacha")]
    [InlineData("/sv/tond/issues", "/sv/contact?topic=vacha")]
    [InlineData("/vacha/license", "https://github.com/nova-globen/vacha/blob/main/LICENSE.txt")]
    [InlineData("/vacha/privacy", "https://github.com/nova-globen/vacha/blob/main/PRIVACY.md")]
    [InlineData("/tond/license", "https://github.com/nova-globen/vacha/blob/main/LICENSE.txt")]
    [InlineData("/products/vacha", null)]
    [InlineData("/vacha", null)]
    [InlineData("/contact", null)]
    public void A_product_address_leads_to_its_target(string path, string? target) =>
        Assert.Equal(target, NovaGloben.Website.Sites.ProductLinks.Target(path));

    [Fact]
    public void The_rate_limit_allows_a_few_posts_per_window()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Contact:RateLimit:Count"] = "3" }).Build();
        var limit = new ContactRateLimit(configuration, TimeProvider.System);

        Assert.True(limit.TryAcquire("198.51.100.7"));
        Assert.True(limit.TryAcquire("198.51.100.7"));
        Assert.True(limit.TryAcquire("198.51.100.7"));
        Assert.False(limit.TryAcquire("198.51.100.7"));
        Assert.True(limit.TryAcquire("198.51.100.8"));
    }

    [Fact]
    public void The_site_wide_ceiling_holds_across_addresses()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Contact:RateLimit:SiteWidePerHour"] = "4" }).Build();
        var limit = new ContactRateLimit(configuration, TimeProvider.System);

        for (var i = 1; i <= 4; i++)
        {
            Assert.True(limit.TryAcquire($"198.51.100.{i}"));
        }
        Assert.False(limit.TryAcquire("198.51.100.99"));
    }

    [Fact]
    public async Task Turnstile_fails_closed_without_a_secret()
    {
        var configuration = new ConfigurationBuilder().Build();
        var turnstile = new Turnstile(new NoHttp(), configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<Turnstile>.Instance);

        Assert.False(await turnstile.VerifyAsync("a-token", "198.51.100.7", CancellationToken.None));
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No request may be made without a secret.");
    }
}
