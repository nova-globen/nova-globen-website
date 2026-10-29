using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NovaGloben.Website.Articles
{
    /// <summary>An article as a list shows it.</summary>
    public class ArticleSummary
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public DateTime PublishedOn { get; set; }

        /// <summary>Root-relative URL of the PNG cover, or empty when the article has none.</summary>
        public string CoverPng { get; set; } = "";

        /// <summary>Root-relative URL of the WebP cover, or empty when there is none.</summary>
        public string CoverWebp { get; set; } = "";
    }

    /// <summary>An article with its body, rendered to HTML when it was imported.</summary>
    public class ArticleDetail : ArticleSummary
    {
        public string Html { get; set; } = "";
    }

    /// <summary>One page of the article list.</summary>
    public class ArticleListPage
    {
        public List<ArticleSummary> Items { get; set; } = new();
        public int Page { get; set; } = 1;
        public int PageCount { get; set; } = 1;
        public int Total { get; set; }
        public string Tag { get; set; } = "";

        /// <summary>Every tag in use, with the number of articles carrying it, most used first.</summary>
        public List<TagCount> Tags { get; set; } = new();
    }

    public class TagCount
    {
        public string Tag { get; set; } = "";
        public int Count { get; set; }
    }

    /// <summary>
    /// The articles of the site being served. The server answers from the module's store; a browser (an interactive or
    /// WebAssembly page) over HTTP. Every public page of the sites renders statically, so the server answers them.
    /// </summary>
    public interface IArticleService
    {
        Task<ArticleListPage> ListAsync(int page, int pageSize, string tag);

        Task<List<ArticleSummary>> LatestAsync(int count);

        Task<ArticleDetail> GetAsync(string slug);
    }
}
