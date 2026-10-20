using System.Net;
using System.Net.Sockets;
using System.Text;
using TalksOps.AI;
using TalksOps.Core.Contracts;

namespace TalksOps.AI.Services;

/// <summary>Downloads bounded HTML from public HTTP(S) addresses without following unchecked redirects.</summary>
public sealed class PublicEventPageFetcher : IEventPageFetcher, IDisposable
{
    private const int MaximumHtmlBytes = 2 * 1024 * 1024;
    private const int MaximumRedirects = 3;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient httpClient;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses;

    /// <summary>Creates a fetcher whose connections are pinned to validated public addresses.</summary>
    public PublicEventPageFetcher()
        : this(CreateHandler(), ResolveAddressesAsync)
    {
    }

    /// <summary>Creates a fetcher with a custom transport and resolver, primarily for tests.</summary>
    public PublicEventPageFetcher(
        HttpMessageHandler handler,
        Func<string, CancellationToken, Task<IPAddress[]>> addressResolver)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(addressResolver);
        this.httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        this.resolveAddresses = addressResolver;
    }

    /// <inheritdoc />
    public async Task<string> FetchAsync(
        string sourcePageUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(sourcePageUrl, UriKind.Absolute, out var currentUri)
            || !IsAllowedUri(currentUri))
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.InvalidSourceUrl,
                "The source page URL must be an absolute HTTP or HTTPS URL.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);

        for (var redirectCount = 0; ; redirectCount++)
        {
            await EnsurePublicHostAsync(currentUri, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("TalksOps-Event-Extractor/1.0");

            HttpResponseMessage response;
            try
            {
                response = await this.httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EventDataExtractionException(
                    EventDataExtractionErrorCode.FetchFailed,
                    "The source page could not be fetched before the timeout.");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= MaximumRedirects
                        || response.Headers.Location is not { } location)
                    {
                        throw new EventDataExtractionException(
                            EventDataExtractionErrorCode.TooManyRedirects,
                            "The source page redirected too many times or returned an invalid redirect.");
                    }

                    currentUri = location.IsAbsoluteUri
                        ? location
                        : new Uri(currentUri, location);
                    if (!IsAllowedUri(currentUri))
                    {
                        throw new EventDataExtractionException(
                            EventDataExtractionErrorCode.InvalidSourceUrl,
                            "The source page redirected to a URL that is not allowed.");
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new EventDataExtractionException(
                        EventDataExtractionErrorCode.FetchFailed,
                        $"The source page returned HTTP {(int)response.StatusCode}.");
                }

                return await ReadBoundedHtmlAsync(response.Content, timeout.Token);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.httpClient.Dispose();

    private static SocketsHttpHandler CreateHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = ConnectToPublicAddressAsync
        };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        EnsureAllAddressesArePublic(addresses);
        Exception? lastException = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException)
                {
                    throw;
                }

                lastException = exception;
            }
        }

        throw new EventDataExtractionException(
            EventDataExtractionErrorCode.FetchFailed,
            "A connection to the source page could not be established.",
            lastException);
    }

    private static async Task<IPAddress[]> ResolveAddressesAsync(
        string host,
        CancellationToken cancellationToken) =>
        IPAddress.TryParse(host, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsAllowedUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme is "http" or "https"
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0
        && uri.Host.Contains('%', StringComparison.Ordinal) is false;

    private async Task EnsurePublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await this.resolveAddresses(uri.Host, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.FetchFailed,
                "The source page host could not be resolved.",
                exception);
        }

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.UnsafeSourceAddress,
                "The source page host must resolve only to public IP addresses.");
        }
    }

    private static void EnsureAllAddressesArePublic(IPAddress[] addresses)
    {
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.UnsafeSourceAddress,
                "The source page host must resolve only to public IP addresses.");
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily is AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return first is not (0 or 10 or 127)
                && first < 224
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 0 && third == 0)
                && !(first == 192 && second == 0 && third == 2)
                && !(first == 192 && second == 88 && third == 99)
                && !(first == 192 && second == 168)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && third == 100)
                && !(first == 203 && second == 0 && third == 113);
        }

        if (address.AddressFamily is not AddressFamily.InterNetworkV6
            || (bytes[0] & 0xE0) != 0x20)
        {
            return false;
        }

        return !(bytes[0] == 0x20 && bytes[1] == 0x01
                && (bytes[2] == 0x0D && bytes[3] == 0xB8
                    || bytes[2] == 0x00 && bytes[3] <= 0x01))
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x3F && bytes[1] == 0xFF
                && (bytes[2] & 0xF0) == 0x00);
    }

    private static async Task<string> ReadBoundedHtmlAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumHtmlBytes)
        {
            throw new EventDataExtractionException(
                EventDataExtractionErrorCode.PageTooLarge,
                "The source page exceeds the maximum allowed size.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(chunk, cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (buffer.Length + count > MaximumHtmlBytes)
            {
                throw new EventDataExtractionException(
                    EventDataExtractionErrorCode.PageTooLarge,
                    "The source page exceeds the maximum allowed size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        var encoding = Encoding.UTF8;
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Use UTF-8 when a page declares an unsupported or invalid character set.
            }
        }

        return encoding.GetString(buffer.ToArray());
    }
}