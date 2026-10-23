using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ReverseMarkdown;
using TalksOps.Core.Contracts;

namespace TalksOps.AI.Services;

/// <summary>Removes page chrome and converts event-page HTML into Markdown.</summary>
public sealed class ReverseMarkdownEventHtmlCleaner : IEventHtmlCleaner
{
    private const int MaximumOutputLength = 40_000;

    /// <inheritdoc />
    public string Clean(string html, Uri sourcePageUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentNullException.ThrowIfNull(sourcePageUri);

        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll(
            "head, script, style, noscript, template, link[rel~='stylesheet'], header, nav, footer, aside, form, button, input, select, textarea, iframe, svg, [hidden], [aria-hidden='true']").ToArray())
        {
            element.Remove();
        }

        foreach (var element in document.QuerySelectorAll("*"))
        {
            foreach (var attribute in element.Attributes
                .Where(attribute => attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                element.RemoveAttribute(attribute.Name);
            }
        }

        ResolveUrls(document, sourcePageUri);

        var contentRoot = document.QuerySelector("main")
            ?? document.QuerySelector("article")
            ?? document.Body;
        var contentHtml = contentRoot?.InnerHtml ?? string.Empty;
        var markdown = new Converter().Convert(contentHtml).Trim();
        return markdown.Length <= MaximumOutputLength
            ? markdown
            : string.Concat(markdown.AsSpan(0, MaximumOutputLength - 3), "...");
    }

    private static void ResolveUrls(IDocument document, Uri sourcePageUri)
    {
        foreach (var element in document.QuerySelectorAll("a[href], img[src], source[src]"))
        {
            var attributeName = element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? "href"
                : "src";
            var value = element.GetAttribute(attributeName);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Uri.TryCreate(sourcePageUri, value, out var absoluteUri)
                && absoluteUri is not null
                && absoluteUri.Scheme is "http" or "https"
                && absoluteUri.UserInfo.Length is 0)
            {
                element.SetAttribute(attributeName, absoluteUri.AbsoluteUri);
            }
            else
            {
                element.RemoveAttribute(attributeName);
            }
        }
    }
}
