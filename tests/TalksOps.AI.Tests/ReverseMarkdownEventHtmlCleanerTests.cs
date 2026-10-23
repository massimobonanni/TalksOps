using TalksOps.AI.Services;

namespace TalksOps.AI.Tests;

public sealed class ReverseMarkdownEventHtmlCleanerTests
{
    [Fact]
    public void Clean_RemovesPageChromeAndStyles_AndConvertsEventContentToMarkdown()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <title>Example Conference</title>
                <link rel="stylesheet" href="/styles.css">
                <style>.event { color: red; }</style>
                <script>alert('head script');</script>
              </head>
              <body>
                <header>Unrelated header</header>
                <nav>Unrelated navigation</nav>
                <main>
                  <h1>Example Conference</h1>
                  <p>Submit your work by <strong>June 1, 2027</strong>.</p>
                  <a href="/cfp">Call for Papers</a>
                  <table>
                    <tr><th>Date</th><th>Event</th></tr>
                    <tr><td>June 10</td><td>Conference</td></tr>
                  </table>
                  <script>alert('body script');</script>
                </main>
                <footer>Unrelated footer</footer>
              </body>
            </html>
            """;
        var cleaner = new ReverseMarkdownEventHtmlCleaner();

        var markdown = cleaner.Clean(html, new Uri("https://example.org/conference"));

        Assert.Contains("# Example Conference", markdown);
        Assert.Contains("**June 1, 2027**", markdown);
        Assert.Contains("[Call for Papers](https://example.org/cfp)", markdown);
        Assert.Contains("| Date | Event |", markdown);
        Assert.DoesNotContain("Unrelated header", markdown);
        Assert.DoesNotContain("Unrelated navigation", markdown);
        Assert.DoesNotContain("Unrelated footer", markdown);
        Assert.DoesNotContain("alert(", markdown);
        Assert.DoesNotContain("color: red", markdown);
        Assert.DoesNotContain("styles.css", markdown);
        Assert.DoesNotContain("<h1>", markdown);
    }
}
