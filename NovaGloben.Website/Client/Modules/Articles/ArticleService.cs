using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Stomme.Services;
using Stomme.Shared;
using Microsoft.Extensions.DependencyInjection;
using NovaGloben.Website.Articles;

namespace NovaGloben.Website.Client.Modules.Articles
{
    /// <summary>
    /// The BROWSER's <see cref="IArticleService"/>, over <c>api/Articles</c>. The public pages render on the server, which
    /// registers its own service last, so this one serves only a page rendered in a browser.
    /// </summary>
    public class ArticleService : ServiceBase, IArticleService
    {
        public ArticleService(HttpClient http, SiteState siteState) : base(http, siteState)
        {
        }

        private string ApiUrl => CreateApiUrl("Articles");

        public async Task<ArticleListPage> ListAsync(int page, int pageSize, string tag) =>
            await GetJsonAsync<ArticleListPage>($"{ApiUrl}?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}&tag={Uri.EscapeDataString(tag ?? "")}") ?? new ArticleListPage();

        public async Task<List<ArticleSummary>> LatestAsync(int count) =>
            await GetJsonAsync<List<ArticleSummary>>($"{ApiUrl}/latest?count={count.ToString(CultureInfo.InvariantCulture)}") ?? new List<ArticleSummary>();

        public async Task<ArticleDetail> GetAsync(string slug) =>
            await GetJsonAsync<ArticleDetail>($"{ApiUrl}/{Uri.EscapeDataString(slug ?? "")}");
    }

    /// <summary>Registers the browser's service when nothing registered one (the server registers its own).</summary>
    public class ClientStartup : IClientStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            if (!services.Any(descriptor => descriptor.ServiceType == typeof(IArticleService)))
            {
                services.AddScoped<IArticleService, ArticleService>();
            }
        }
    }
}
