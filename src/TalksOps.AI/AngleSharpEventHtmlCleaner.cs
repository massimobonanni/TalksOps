using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TalksOps.Core.Contracts;

namespace TalksOps.AI;

/// <summary>Cleans event-page HTML using the AngleSharp HTML5 parser.</summary>
public sealed class AngleSharpEventHtmlCleaner : IEventHtmlCleaner
{
    private const int MaximumOutputLength = 40_000;
    private const int MaximumPageTextLength = 30_000;
    private const int MaximumStructuredDataLength = 8_000;
    private const int MaximumRelevantLinks = 30;
    private static readonly string[] RelevantLinkTerms =
    [
        "call for papers",
        "cfp",
        "submission",
        "submit",
        "deadline",
        "important dates",
        "dates",
        "venue",
        "location",
        "program",
        "schedule"
    ];

    /// <inheritdoc />
    public string Clean(string html, Uri sourcePageUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentNullException.ThrowIfNull(sourcePageUri);

        var document = new HtmlParser().ParseDocument(html);
        var structuredData = ExtractStructuredData(document);
        var relevantLinks = ExtractRelevantLinks(document, sourcePageUri);
        var contentRoot = document.QuerySelector("main")
            ?? document.QuerySelector("article")
            ?? document.Body;

        foreach (var element in document.QuerySelectorAll(
            "script, style, noscript, template, nav, footer, aside, form, button, input, select, textarea, iframe, svg, [hidden], [aria-hidden='true']").ToArray())
        {
            element.Parent?.RemoveChild(element);
        }

        var pageText = NormalizeWhitespace(contentRoot?.TextContent ?? string.Empty);
        var output = new StringBuilder();
        AppendSection(output, "Page title", NormalizeWhitespace(document.Title), 500);
        AppendSection(output, "Page description", GetDescription(document), 1_000);
        AppendSection(output, "Canonical URL", GetCanonicalUrl(document, sourcePageUri), 2_000);
        AppendSection(output, "Structured event data (JSON-LD)", structuredData, MaximumStructuredDataLength);
        AppendSection(output, "Relevant links", relevantLinks, 8_000);
        AppendSection(output, "Page content", Truncate(pageText, MaximumPageTextLength), MaximumPageTextLength);

        return Truncate(output.ToString(), MaximumOutputLength);
    }

    /// <summary>Reads JSON-LD blocks before scripts are removed from the page content.</summary>
    private static string ExtractStructuredData(IDocument document)
    {
        var blocks = new StringBuilder();
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                AppendLimited(blocks, json.RootElement.GetRawText(), MaximumStructuredDataLength);
            }
            catch (JsonException)
            {
                // Ignore malformed structured data and continue with the visible page content.
            }
        }

        return blocks.ToString();
    }

    /// <summary>Returns a bounded list of event-related links with absolute URLs.</summary>
    private static IReadOnlyList<string> ExtractRelevantLinks(IDocument document, Uri sourcePageUri)
    {
        var links = new List<string>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)
                || !Uri.TryCreate(sourcePageUri, href, out var absoluteUri)
                || absoluteUri is null
                || absoluteUri.Scheme is not ("http" or "https")
                || absoluteUri.UserInfo.Length > 0)
            {
                continue;
            }

            var label = NormalizeWhitespace(anchor.TextContent);
            if (label.Length is 0)
            {
                label = NormalizeWhitespace(anchor.GetAttribute("aria-label") ?? string.Empty);
            }

            var url = absoluteUri.AbsoluteUri;
            var description = $"{label} {url}";
            if (!RelevantLinkTerms.Any(term => description.Contains(term, StringComparison.OrdinalIgnoreCase))
                || !seenUrls.Add(url))
            {
                continue;
            }

            var link = $"- {Truncate(label, 160)}: {Truncate(url, 500)}";
            links.Add(link);
            if (links.Count >= MaximumRelevantLinks)
            {
                break;
            }
        }

        return links;
    }

    /// <summary>Gets the standard page description or its Open Graph equivalent.</summary>
    private static string GetDescription(IDocument document) =>
        NormalizeWhitespace(
            document.QuerySelector("meta[name='description']")?.GetAttribute("content")
            ?? document.QuerySelector("meta[property='og:description']")?.GetAttribute("content")
            ?? string.Empty);

    /// <summary>Resolves the canonical link relative to the source page when necessary.</summary>
    private static string GetCanonicalUrl(IDocument document, Uri sourcePageUri)
    {
        var href = document.QuerySelector("link[rel='canonical']")?.GetAttribute("href");
        return !string.IsNullOrWhiteSpace(href)
            && Uri.TryCreate(sourcePageUri, href, out var canonicalUri)
            && canonicalUri is not null
            && canonicalUri.Scheme is "http" or "https"
                ? canonicalUri.AbsoluteUri
                : string.Empty;
    }

    /// <summary>Appends a labeled section without exceeding its individual character budget.</summary>
    private static void AppendSection(StringBuilder output, string heading, string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AppendSection(output, heading, [value], maximumLength);
    }

    /// <summary>Appends a labeled section containing multiple values.</summary>
    private static void AppendSection(
        StringBuilder output,
        string heading,
        IEnumerable<string> values,
        int maximumLength)
    {
        var section = new StringBuilder();
        foreach (var value in values)
        {
            AppendLimited(section, value, maximumLength);
            if (section.Length < maximumLength)
            {
                section.AppendLine();
            }
        }

        if (section.Length is 0)
        {
            return;
        }

        output.AppendLine($"{heading}:");
        AppendLimited(output, section.ToString(), MaximumOutputLength);
        output.AppendLine();
    }

    /// <summary>Appends text only while the destination remains within its character budget.</summary>
    private static void AppendLimited(StringBuilder output, string value, int maximumLength)
    {
        var remaining = maximumLength - output.Length;
        if (remaining <= 0)
        {
            return;
        }

        output.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
    }

    /// <summary>Collapses HTML whitespace and trims the resulting text.</summary>
    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Truncates long extracted text to keep model input bounded.</summary>
    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength - 3), "...");
}