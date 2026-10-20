using System.Net;
using TalksOps.AI;
using TalksOps.AI.Services;

namespace TalksOps.AI.Tests;

public sealed class PublicEventPageFetcherTests
{
    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[3fff::1]/")]
    public async Task FetchAsync_RejectsNonPublicAddresses(string url)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var fetcher = CreateFetcher(handler);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => fetcher.FetchAsync(url));

        Assert.Equal(EventDataExtractionErrorCode.UnsafeSourceAddress, exception.ErrorCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_ValidatesRedirectDestinationBeforeRequestingIt()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("http://127.0.0.1/admin") }
            });
        using var fetcher = CreateFetcher(handler);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => fetcher.FetchAsync("https://8.8.8.8/cfp"));

        Assert.Equal(EventDataExtractionErrorCode.UnsafeSourceAddress, exception.ErrorCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_StopsAfterMaximumRedirects()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("/next", UriKind.Relative) }
            });
        using var fetcher = CreateFetcher(handler);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => fetcher.FetchAsync("https://8.8.8.8/cfp"));

        Assert.Equal(EventDataExtractionErrorCode.TooManyRedirects, exception.ErrorCode);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_RejectsOversizedContent()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', (2 * 1024 * 1024) + 1))
            });
        using var fetcher = CreateFetcher(handler);

        var exception = await Assert.ThrowsAsync<EventDataExtractionException>(
            () => fetcher.FetchAsync("https://8.8.8.8/cfp"));

        Assert.Equal(EventDataExtractionErrorCode.PageTooLarge, exception.ErrorCode);
    }

    [Fact]
    public async Task FetchAsync_ReturnsPageFromPublicAddress()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>CFP</html>")
            });
        using var fetcher = CreateFetcher(handler);

        var html = await fetcher.FetchAsync("https://8.8.8.8/cfp");

        Assert.Equal("<html>CFP</html>", html);
        Assert.Equal(1, handler.RequestCount);
    }

    private static PublicEventPageFetcher CreateFetcher(StubHttpMessageHandler handler) =>
        new(
            handler,
            (host, _) => Task.FromResult(
                IPAddress.TryParse(host, out var address)
                    ? new[] { address }
                    : new[] { IPAddress.Parse("8.8.8.8") }));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}