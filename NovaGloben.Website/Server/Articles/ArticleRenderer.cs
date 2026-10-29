#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using ColorCode;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// Renders an article's markdown to HTML at import: Markdig with the extensions the old site used
    /// (UseAdvancedExtensions), code highlighted on the server by ColorCode into CSS classes the theme colours (no
    /// script in the browser), and two fixes the old pages needed:
    /// <list type="bullet">
    /// <item>the article's own leading "# Title" is dropped and any other level-1 heading becomes level 2, because the
    /// page's one h1 is the article's title (the old pages carried two);</item>
    /// <item>a code block can take keyboard focus (tabindex="0"), so a reader can scroll a wide one with the keyboard
    /// (axe: scrollable-region-focusable).</item>
    /// </list>
    /// </summary>
    public static class ArticleRenderer
    {
        /// <summary>Part of every article's source hash: raise it when the output changes, and every article is re-rendered.</summary>
        public const string Version = "1";

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static string Render(string markdown)
        {
            var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

            if (document.Count > 0 && document[0] is HeadingBlock { Level: 1 } leading)
            {
                document.Remove(leading);
            }
            foreach (var heading in document.Descendants<HeadingBlock>().Where(h => h.Level == 1))
            {
                heading.Level = 2;
            }

            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            var stock = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
            if (stock != null)
            {
                renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new HighlightingCodeBlockRenderer(stock));
            }
            renderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }

        /// <summary>
        /// Fenced code with a language ColorCode knows is highlighted into &lt;span class="..."&gt;; any other code block is
        /// written escaped and plain. Either way it is &lt;pre class="ng-code" tabindex="0"&gt;&lt;code&gt;.
        /// </summary>
        private sealed class HighlightingCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
        {
            private readonly CodeBlockRenderer _fallback;

            public HighlightingCodeBlockRenderer(CodeBlockRenderer fallback)
            {
                _fallback = fallback;
            }

            protected override void Write(HtmlRenderer renderer, CodeBlock block)
            {
                var info = (block as FencedCodeBlock)?.Info?.Trim() ?? string.Empty;
                if (block is FencedCodeBlock fenced && _fallback.BlocksAsDiv.Contains(info))
                {
                    _fallback.Write(renderer, fenced);
                    return;
                }

                var code = string.Join("\n", block.Lines.Lines.Take(block.Lines.Count).Select(line => line.ToString()));
                var language = CodeLanguages.Find(info);
                var label = info.Length > 0 ? WebUtility.HtmlEncode(info.Split(' ')[0].ToLowerInvariant()) : null;

                renderer.EnsureLine();
                renderer.Write("<pre class=\"ng-code\" tabindex=\"0\"");
                if (label != null)
                {
                    renderer.Write($" data-lang=\"{label}\"");
                }
                renderer.Write("><code");
                if (label != null)
                {
                    renderer.Write($" class=\"language-{label}\"");
                }
                renderer.Write(">");
                renderer.Write(language == null ? WebUtility.HtmlEncode(code) : CodeLanguages.Highlight(code, language));
                renderer.WriteLine("</code></pre>");
            }
        }
    }

    /// <summary>The fence names the articles use, mapped to ColorCode's languages, and the highlighter itself.</summary>
    public static class CodeLanguages
    {
        private static readonly Dictionary<string, ILanguage> ByName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = Languages.CSharp,
            ["cs"] = Languages.CSharp,
            ["c#"] = Languages.CSharp,
            ["xml"] = Languages.Xml,
            ["html"] = Languages.Html,
            ["sql"] = Languages.Sql,
            ["powershell"] = Languages.PowerShell,
            ["ps1"] = Languages.PowerShell,
            ["javascript"] = Languages.JavaScript,
            ["js"] = Languages.JavaScript,
            ["typescript"] = Languages.Typescript,
            ["ts"] = Languages.Typescript,
            ["css"] = Languages.Css,
            ["python"] = Languages.Python,
            ["java"] = Languages.Java,
            ["fsharp"] = Languages.FSharp,
            ["cpp"] = Languages.Cpp,
            ["php"] = Languages.Php,
        };

        public static ILanguage? Find(string info)
        {
            var name = (info ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return ByName.TryGetValue(name, out var language) ? language : null;
        }

        /// <summary>The highlighted code's inner HTML: ColorCode's &lt;span class&gt; markup without its own div/pre wrapper.</summary>
        public static string Highlight(string code, ILanguage language)
        {
            var html = new HtmlClassFormatter().GetHtmlString(code, language);
            var start = html.IndexOf("<pre>", StringComparison.Ordinal);
            var end = html.LastIndexOf("</pre>", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                html = html.Substring(start + "<pre>".Length, end - start - "<pre>".Length);
            }
            return html.Trim('\r', '\n');
        }
    }
}
