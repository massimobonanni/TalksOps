using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Web.Components.Layout;
using TalksOps.Web.Components.Pages;
using TalksOps.Web.Security;

namespace TalksOps.Web.Tests;

public sealed class PublicFeedbackUiTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousRoute_UsesPublicLayoutWithoutReadingUserOrSpeakerData(bool alreadySignedIn)
    {
        var authorization = this.AddAuthorization();
        if (alreadySignedIn) authorization.SetAuthorized("speaker");
        using var handler = new PublicFeedbackHandler();
        this.RegisterServices(handler);
        var component = this.Render<AuthorizeRouteView>(parameters => parameters
            .Add(view => view.RouteData, new RouteData(typeof(PublicFeedback), new Dictionary<string, object?>
            {
                [nameof(PublicFeedback.SessionId)] = handler.SessionId
            }))
            .Add(view => view.DefaultLayout, typeof(MainLayout)));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        Assert.Equal(handler.Title, component.Find("h1").TextContent);
        Assert.Empty(component.FindAll(".account-menu, nav"));
        Assert.DoesNotContain("Sign in required", component.Markup);
        Assert.All(handler.Requests, request => Assert.Equal($"/api/feedback/{handler.SessionId}", request));
    }

    [Fact]
    public async Task Loading_HidesTitleAndFormUntilAvailabilityIsConfirmed()
    {
        using var handler = new PublicFeedbackHandler { DelayedGet = new() };
        var component = this.RenderPage(handler);
        Assert.Contains("Loading feedback form", component.Find("[role=status]").TextContent);
        Assert.Empty(component.FindAll("form"));
        Assert.DoesNotContain(handler.Title, component.Markup);
        handler.DelayedGet.SetResult(handler.PublicResponse());

        await component.InvokeAsync(() => Task.CompletedTask);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public void Unavailable_HidesTitleAndAllInputs(HttpStatusCode status, bool available)
    {
        using var handler = new PublicFeedbackHandler { GetStatus = status, Available = available };
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Equal("Feedback is not available.", component.Find("[role=alert]").TextContent));
        Assert.Empty(component.FindAll("form, input, textarea"));
        Assert.DoesNotContain(handler.Title, component.Markup);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void LoadError_RetryRecoversWithoutExposingApiError(HttpStatusCode status)
    {
        using var handler = new PublicFeedbackHandler { GetStatus = status };
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Contains("Unable to load feedback", component.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Private speaker data", component.Markup);
        handler.GetStatus = HttpStatusCode.OK;
        component.Find("button").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void ConnectionFailure_ShowsErrorAndRetryRecovers()
    {
        using var handler = new PublicFeedbackHandler { Failure = new HttpRequestException("Private speaker data") };
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Contains("Unable to load feedback", component.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Private speaker data", component.Markup);
        handler.Failure = null;
        component.Find("button").Click();
        this.EnterRatings(component);
        handler.Failure = new HttpRequestException("Private speaker data");
        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains("Unable to send feedback", component.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Private speaker data", component.Markup);
        Assert.False(component.Find("fieldset").HasAttribute("disabled"));
        handler.Failure = null;
        component.Find("form").Submit();
        component.WaitForAssertion(() => Assert.Contains("Thank you", component.Find("[role=status]").TextContent));
    }

    [Fact]
    public void Ratings_StartEmptyWithTenRequiredStarsAndOptionalComment()
    {
        using var handler = new PublicFeedbackHandler();
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        var radios = component.FindAll("input[type=radio]");
        Assert.Equal(20, radios.Count);
        foreach (var radioGroup in new[] { "session-rating", "speaker-rating" })
        {
            var group = component.Find($"#{radioGroup}");
            Assert.Equal("radiogroup", group.GetAttribute("role"));
            var ratings = group.QuerySelectorAll("input[type=radio]");
            Assert.Equal(10, ratings.Length);
            Assert.All(ratings, radio =>
            {
                Assert.True(radio.HasAttribute("required"));
                Assert.False(radio.HasAttribute("checked"));
            });
            Assert.Equal(Enumerable.Range(1, 10).Select(rating => rating.ToString()), ratings.Select(radio => radio.GetAttribute("value")));
        }
        Assert.False(component.Find("textarea").HasAttribute("required"));
        Assert.Equal("4000", component.Find("textarea").GetAttribute("maxlength"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 8)]
    [InlineData(8, null)]
    public void InvalidRatings_DoNotPost(int? sessionRating, int? speakerRating)
    {
        using var handler = new PublicFeedbackHandler();
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        if (sessionRating.HasValue) component.Find($"#session-rating-{sessionRating.Value}").Change(sessionRating.Value);
        if (speakerRating.HasValue) component.Find($"#speaker-rating-{speakerRating.Value}").Change(speakerRating.Value);
        component.Find("form").Submit();

        Assert.Contains("Both ratings are required", component.Find("[role=alert]").TextContent);
        Assert.Equal(0, handler.Posts);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public void CommentLimit_IsValidatedBeforePost(int length, bool accepted)
    {
        using var handler = new PublicFeedbackHandler();
        var component = this.RenderPage(handler);
        this.EnterRatings(component, 1, 10);
        component.Find("textarea").Change(new string('a', length));
        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Equal(accepted ? 1 : 0, handler.Posts));
        if (accepted)
        {
            component.WaitForAssertion(() => Assert.Contains("Thank you", component.Find("[role=status]").TextContent));
        }
        else
        {
            Assert.Contains("4000", component.Find("[role=alert]").TextContent);
        }
    }

    [Theory]
    [InlineData(1, 10, null)]
    [InlineData(10, 1, "Useful session")]
    public void SuccessfulSubmit_SendsOnlyRatingsAndOptionalCommentThenRemovesForm(int sessionRating, int speakerRating, string? comment)
    {
        using var handler = new PublicFeedbackHandler();
        var component = this.RenderPage(handler);
        this.EnterRatings(component, sessionRating, speakerRating);
        if (comment is not null) component.Find("textarea").Change(comment);
        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains("Thank you", component.Find("[role=status]").TextContent));
        Assert.Empty(component.FindAll("form"));
        Assert.Equal(new SubmitFeedbackRequest(sessionRating, speakerRating, comment), handler.Submission);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(["sessionRating", "speakerRating", "comment"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, handler.Posts);
        Assert.Equal(1, handler.Gets);
    }

    [Fact]
    public async Task PendingSubmit_DisablesControlsAndRejectsDuplicateSubmit()
    {
        using var handler = new PublicFeedbackHandler { DelayedPost = new() };
        var component = this.RenderPage(handler);
        this.EnterRatings(component);
        component.Find("form").Submit();
        component.WaitForAssertion(() => Assert.True(component.Find("fieldset").HasAttribute("disabled")));
        component.Find("form").Submit();
        Assert.Equal(1, handler.Posts);
        handler.DelayedPost.SetResult(new HttpResponseMessage(HttpStatusCode.Created));

        await component.InvokeAsync(() => Task.CompletedTask);
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("form")));
    }

    [Fact]
    public void ClosedWindowOnPost_RemovesPreviouslyLoadedTitleAndForm()
    {
        using var handler = new PublicFeedbackHandler { PostStatus = HttpStatusCode.NotFound };
        var component = this.RenderPage(handler);
        this.EnterRatings(component);
        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Equal("Feedback is not available.", component.Find("[role=alert]").TextContent));
        Assert.Empty(component.FindAll("form"));
        Assert.DoesNotContain(handler.Title, component.Markup);
        Assert.Equal(1, handler.Posts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "not accepted")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "too large")]
    [InlineData(HttpStatusCode.TooManyRequests, "wait")]
    [InlineData(HttpStatusCode.InternalServerError, "Unable to send")]
    public void PostError_PreservesValuesAndAllowsRetry(HttpStatusCode status, string message)
    {
        using var handler = new PublicFeedbackHandler { PostStatus = status };
        var component = this.RenderPage(handler);
        this.EnterRatings(component);
        component.Find("textarea").Change("Keep this");
        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains(message, component.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Private speaker data", component.Markup);
        Assert.True(component.Find("#session-rating-8").HasAttribute("checked"));
        Assert.True(component.Find("#speaker-rating-9").HasAttribute("checked"));
        Assert.Equal("Keep this", component.Find("textarea").GetAttribute("value"));
        Assert.False(component.Find("fieldset").HasAttribute("disabled"));
        handler.PostStatus = HttpStatusCode.Created;
        component.Find("form").Submit();
        component.WaitForAssertion(() => Assert.Contains("Thank you", component.Find("[role=status]").TextContent));
        Assert.Equal(2, handler.Posts);
        Assert.Equal("Keep this", handler.Submission!.Comment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Navigation_DiscardsPendingLoadOrSubmitAndShowsUnavailableNewSession(bool pendingSubmit)
    {
        using var handler = new PublicFeedbackHandler();
        if (!pendingSubmit) handler.DelayedGet = new();
        var component = this.RenderPage(handler);
        if (pendingSubmit)
        {
            handler.DelayedPost = new();
            this.EnterRatings(component);
            component.Find("form").Submit();
        }

        var pendingGet = handler.DelayedGet;
        handler.DelayedGet = null;
        handler.GetStatus = HttpStatusCode.NotFound;
        component.Render(parameters => parameters.Add(page => page.SessionId, Guid.NewGuid()));
        component.WaitForAssertion(() => Assert.Contains("Feedback is not available", component.Markup));
        if (pendingSubmit) handler.DelayedPost!.SetResult(new HttpResponseMessage(HttpStatusCode.Created));
        else pendingGet!.SetResult(handler.PublicResponse());
        await component.InvokeAsync(() => Task.CompletedTask);

        Assert.Empty(component.FindAll("form"));
        Assert.DoesNotContain(handler.Title, component.Markup);
        Assert.DoesNotContain("Thank you", component.Markup);
    }

    private IRenderedComponent<PublicFeedback> RenderPage(PublicFeedbackHandler handler)
    {
        this.RegisterServices(handler);
        return this.Render<PublicFeedback>(parameters => parameters.Add(page => page.SessionId, handler.SessionId));
    }

    private void RegisterServices(PublicFeedbackHandler handler)
    {
        this.Services.AddLogging();
        this.Services.AddScoped<WebCurrentUserContext>(_ => throw new InvalidOperationException("Public pages must not resolve the user context."));
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") }, new UnusedFeedbackUserContext()));
    }

    private void EnterRatings(IRenderedComponent<PublicFeedback> component, int sessionRating = 8, int speakerRating = 9)
    {
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        component.Find($"#session-rating-{sessionRating}").Change(sessionRating);
        component.Find($"#speaker-rating-{speakerRating}").Change(speakerRating);
    }
}

internal sealed class UnusedFeedbackUserContext : ICurrentUserContext
{
    public string UserId => throw new InvalidOperationException("Public feedback must not read a user.");
}

internal sealed class PublicFeedbackHandler : HttpMessageHandler
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public string Title { get; } = "Public session title";
    public bool Available { get; set; } = true;
    public HttpStatusCode GetStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode PostStatus { get; set; } = HttpStatusCode.Created;
    public TaskCompletionSource<HttpResponseMessage>? DelayedGet { get; set; }
    public TaskCompletionSource<HttpResponseMessage>? DelayedPost { get; set; }
    public TaskCompletionSource GetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public HttpRequestException? Failure { get; set; }
    public List<string> Requests { get; } = [];
    public int Gets { get; private set; }
    public int Posts { get; private set; }
    public string? Body { get; private set; }
    public SubmitFeedbackRequest? Submission { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        Assert.StartsWith("/api/feedback/", path);
        Assert.DoesNotContain("?", path);
        this.Requests.Add(path);
        if (this.Failure is not null) throw this.Failure;
        if (request.Method == HttpMethod.Get)
        {
            this.Gets++;
            this.GetStarted.TrySetResult();
            return this.DelayedGet is not null ? await this.DelayedGet.Task : this.PublicResponse();
        }

        Assert.Equal(HttpMethod.Post, request.Method);
        this.Posts++;
        this.Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        this.Submission = JsonSerializer.Deserialize<SubmitFeedbackRequest>(this.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return this.DelayedPost is not null ? await this.DelayedPost.Task : Response(this.PostStatus);
    }

    public HttpResponseMessage PublicResponse() => this.GetStatus == HttpStatusCode.OK
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new PublicFeedbackDto(this.Title, this.Available)) }
        : Response(this.GetStatus);

    private static HttpResponseMessage Response(HttpStatusCode status) => new(status)
    {
        Content = JsonContent.Create(new ApiErrorDto(null, "Private speaker data must not appear in the page."))
    };
}
