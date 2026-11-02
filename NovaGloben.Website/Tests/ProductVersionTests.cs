using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>Vacha's update check reads releases.json, so the version there and the version its pages state move together.</summary>
public class ProductVersionTests
{
    [Fact]
    public void Vachas_pages_in_both_languages_state_the_version_its_update_check_reads()
    {
        var root = ContentTests.RepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "content", "products", "vacha", "releases.json")));
        var version = manifest.RootElement.GetProperty("version").GetString();
        Assert.Equal(version, manifest.RootElement.GetProperty("platforms").GetProperty("win32-x64").GetProperty("version").GetString());

        foreach (var language in new[] { "en", "sv" })
        {
            var pages = Path.Combine(root, "content", "sites", "company-" + language, "pages");
            var stated = Regex.Matches(File.ReadAllText(Path.Combine(pages, "product-vacha.html")), @"Vacha (\d+\.\d+\.\d+)")
                .Concat(Regex.Matches(File.ReadAllText(Path.Combine(pages, "product-vacha-download.html")), @"(?:Version |releases/tag/v)(\d+\.\d+\.\d+)"))
                .Concat(Regex.Matches(File.ReadAllText(Path.Combine(root, "content", "sites", "company-" + language, "site.json")), @"Vacha (\d+\.\d+\.\d+)"))
                .Select(match => match.Groups[1].Value)
                .ToList();

            Assert.NotEmpty(stated);
            Assert.All(stated, found => Assert.Equal(version, found));
        }
    }
}
