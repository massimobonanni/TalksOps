using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient;
using TalksOps.Web.Components;
using TalksOps.Web.Security;

namespace TalksOps.Web.Tests;

public sealed class PublicFeedbackSsrTests
{
    [Fact]
    public async Task InitialAnonymousRequest_RendersFormWithNoLoginOrSpeakerContext()
    {
        using var handler = new PublicFeedbackHandler();
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/feedback/{handler.SessionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains(handler.Title, html);
        Assert.Contains("id=\"session-rating\"", html);
        Assert.Contains("id=\"speaker-rating\"", html);
        Assert.DoesNotContain("Sign in required", html);
        Assert.DoesNotContain("account-menu", html);
        Assert.DoesNotContain("x-functions-key", html);
        Assert.Equal(1, handler.Gets);
    }

    [Fact]
    public async Task InteractiveServerInitializer_IsAvailableWithoutSigningIn()
    {
        using var handler = new PublicFeedbackHandler();
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/_blazor/initializers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task InitialUnavailableRequest_ReturnsActual404WithSsrAndNoTitleOrForm(HttpStatusCode apiStatus, bool available)
    {
        using var handler = new PublicFeedbackHandler { GetStatus = apiStatus, Available = available };
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/feedback/{handler.SessionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("Feedback is not available.", html);
        Assert.DoesNotContain(handler.Title, html);
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("Sign in required", html);
        Assert.Equal(1, handler.Gets);
    }

    [Fact]
    public async Task DelayedAvailabilityCheck_FinishesBefore404HeadersAndBodyAreSent()
    {
        using var handler = new PublicFeedbackHandler { DelayedGet = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        var request = client.GetAsync($"/feedback/{handler.SessionId}", HttpCompletionOption.ResponseHeadersRead);
        await handler.GetStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(request.IsCompleted);
        handler.DelayedGet.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        using var response = await request;

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Feedback is not available.", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, handler.Gets);
    }

    [Fact]
    public async Task InitialApiFailure_Returns503AndVisibleRetryWithoutPrivateData()
    {
        using var handler = new PublicFeedbackHandler { GetStatus = HttpStatusCode.ServiceUnavailable };
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/feedback/{handler.SessionId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Unable to load feedback", html);
        Assert.Contains("Retry", html);
        Assert.DoesNotContain("Private speaker data", html);
        Assert.DoesNotContain(handler.Title, html);
        Assert.DoesNotContain("<form", html);
    }

    [Fact]
    public async Task SpeakerRoute_RemainsProtectedByFallbackPolicy()
    {
        using var handler = new PublicFeedbackHandler();
        await using var factory = CreateFactory(handler);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/events");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.AbsolutePath);
        Assert.Equal(0, handler.Gets);
    }

    private static HttpClient CreateClient(WebApplicationFactory<App> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static WebApplicationFactory<App> CreateFactory(PublicFeedbackHandler handler) =>
        new WebApplicationFactory<App>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Authentication:UseFakeAccount", "true");
            builder.ConfigureServices(services =>
            {
                services.AddScoped<WebCurrentUserContext>(_ => throw new InvalidOperationException("Public SSR must not resolve the speaker context."));
                services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(
                    new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") },
                    new UnusedFeedbackUserContext()));
            });
        });
}
