using Delato.Server.Startup;
using Microsoft.Extensions.Configuration;
using Stomme.Models;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>The site Delato sets up: open sign-up, sign-in only with a confirmed e-mail, English and Persian.</summary>
public sealed class SiteTests
{
    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void A_new_Delato_site_is_open_to_sign_up_interactive_and_untracked()
    {
        var site = DelatoSiteReconciler.NewSite(new Site { TenantId = 2, AdminContainerType = "admin" });

        site.Name.Should().Be("Delato");
        site.AllowRegistration.Should().BeTrue();
        site.CultureCode.Should().Be("en");
        site.RenderMode.Should().Be(RenderModes.Interactive);
        site.Runtime.Should().Be(Runtimes.Server);
        site.VisitorTracking.Should().BeFalse();
        site.TenantId.Should().Be(2);
        site.AdminContainerType.Should().Be("admin");
    }

    [Fact]
    public void An_account_confirms_its_email_before_it_signs_in() =>
        DelatoSiteReconciler.SiteSettings(Settings(new())).Should().ContainSingle()
            .Which.Should().Be(("LoginOptions:RequireConfirmedEmail", "true"));

    [Fact]
    public void The_mail_settings_come_from_configuration_with_plain_smtp_defaults()
    {
        var mail = DelatoSiteReconciler.MailSettings(Settings(new() { ["Delato:Mail:Host"] = " relay ", ["Delato:Mail:Sender"] = "delato@mail.example" }));

        mail.Should().BeEquivalentTo(new List<(string, string)>
        {
            ("SMTPEnabled", "True"), ("SMTPHost", "relay"), ("SMTPPort", "25"), ("SMTPSSL", "None"),
            ("SMTPAuthentication", "Basic"), ("SMTPSender", "delato@mail.example"),
        });
        DelatoSiteReconciler.MailSettings(Settings(new() { ["Delato:Mail:Host"] = "relay" })).Should().BeEmpty();
        DelatoSiteReconciler.SiteSettings(Settings(new() { ["Delato:Mail:Host"] = "relay", ["Delato:Mail:Sender"] = "d@mail.example" }))
            .Should().Contain(("LoginOptions:RequireConfirmedEmail", "true")).And.HaveCount(7);
    }

    [Fact]
    public void The_site_languages_are_English_by_default_and_Persian()
    {
        DelatoSiteReconciler.Languages.Should().Equal(("en", true), ("fa", false));

        var added = DelatoSiteReconciler.LanguageChanges(5, []);

        added.Select(l => (l.SiteId, l.Code, l.IsDefault, l.LanguageId))
            .Should().Equal((5, "en", true, 0), (5, "fa", false, 0));
    }

    [Fact]
    public void A_site_with_its_languages_right_is_left_alone() =>
        DelatoSiteReconciler.LanguageChanges(5,
        [
            new Language { LanguageId = 1, SiteId = 5, Code = "en", IsDefault = true },
            new Language { LanguageId = 2, SiteId = 5, Code = "fa", IsDefault = false },
        ]).Should().BeEmpty();

    [Fact]
    public void A_wrong_default_is_corrected_and_another_language_keeps_its_place()
    {
        var changes = DelatoSiteReconciler.LanguageChanges(5,
        [
            new Language { LanguageId = 3, SiteId = 5, Code = "fa", IsDefault = true },
            new Language { LanguageId = 4, SiteId = 5, Code = "sv", IsDefault = true },
        ]);

        changes.Select(l => (l.LanguageId, l.Code, l.IsDefault))
            .Should().BeEquivalentTo(new[] { (0, "en", true), (3, "fa", false), (4, "sv", false) });
    }
}
