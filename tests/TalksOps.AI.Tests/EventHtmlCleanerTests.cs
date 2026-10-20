using TalksOps.AI.Services;

namespace TalksOps.AI.Tests;

public sealed class EventHtmlCleanerTests
{
    [Fact]
    public void Clean_RemovesPageNoiseAndPreservesEventDataAndRelevantLinks()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <title>Example Conference</title>
                <meta name="description" content="Annual research conference">
                <link rel="canonical" href="/2027">
                <script type="application/ld+json">
                  {"@context":"https://schema.org","@type":"Event","name":"Example Conference"}
                </script>
              </head>
              <body>
                <nav>Unrelated navigation</nav>
                <main>
                  <h1>Example Conference 2027</h1>
                  <p>June 10-12, 2027 in Rome.</p>
                  <a href="/cfp">Call for Papers</a>
                  <script>alert('not page content');</script>
                </main>
                <footer>Unrelated footer</footer>
              </body>
            </html>
            """;
        var cleaner = new AngleSharpEventHtmlCleaner();

        var cleaned = cleaner.Clean(html, new Uri("https://example.org/conference"));

        Assert.Contains("Example Conference", cleaned);
        Assert.Contains("Annual research conference", cleaned);
        Assert.Contains("\"@type\":\"Event\"", cleaned);
        Assert.Contains("Call for Papers: https://example.org/cfp", cleaned);
        Assert.Contains("June 10-12, 2027 in Rome.", cleaned);
        Assert.DoesNotContain("<main>", cleaned);
        Assert.DoesNotContain("Unrelated navigation", cleaned);
        Assert.DoesNotContain("Unrelated footer", cleaned);
        Assert.DoesNotContain("alert(", cleaned);
    }
}