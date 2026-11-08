using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using QRCoder;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Web.Components.Pages;
using SessionFeedback = TalksOps.Web.Components.Pages.SessionFeedback;

namespace TalksOps.Web.Tests;

public sealed class SessionFeedbackUiTests : BunitContext
{
    [Fact]
    public void AnonymousRoute_ShowsSignInWithoutCallingApi()
    {
        this.AddAuthorization();
        var component = this.Render<AuthorizeRouteView>(parameters => parameters
            .Add(view => view.RouteData, new RouteData(typeof(SessionFeedback), new Dictionary<string, object?>
            {
                [nameof(SessionFeedback.EventId)] = Guid.NewGuid(),
                [nameof(SessionFeedback.ProposalId)] = Guid.NewGuid()
            }))
            .Add(view => view.NotAuthorized, _ => builder => builder.AddContent(0, "Sign in required")));

        component.WaitForAssertion(() => Assert.Contains("Sign in required", component.Markup));
        Assert.Empty(component.FindAll("form"));
    }

    [Fact]
    public void AuthorizedRoute_LoadsManagementPage()
    {
        this.AddAuthorization().SetAuthorized("test-user");
        using var handler = new FeedbackHandler();
        this.RegisterServices(handler, "https://public.example.com");
        var component = this.Render<AuthorizeRouteView>(parameters => parameters
            .Add(view => view.RouteData, new RouteData(typeof(SessionFeedback), new Dictionary<string, object?>
            {
                [nameof(SessionFeedback.EventId)] = handler.Proposal.EventId,
                [nameof(SessionFeedback.ProposalId)] = handler.Proposal.Id
            })));

        component.WaitForAssertion(() => Assert.Equal(handler.Proposal.Title, component.Find("h1").TextContent));
        Assert.Single(component.FindAll("form"));
    }

    [Fact]
    public async Task Loading_ShowsStatusAndNoActionsUntilAuthorizedSessionLoads()
    {
        using var handler = new FeedbackHandler { DelayedProposal = new() };
        var component = this.RenderPage(handler);
        Assert.Contains("Loading session feedback", component.Find("[role=status]").TextContent);
        Assert.Empty(component.FindAll("form"));
        Assert.Equal(0, handler.SettingsRequests);
        handler.DelayedProposal.SetResult(FeedbackHandler.Json(handler.Proposal));

        await component.InvokeAsync(() => Task.CompletedTask);
        component.WaitForAssertion(() => Assert.Contains(handler.Proposal.Title, component.Find("h1").TextContent));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void UnavailableOrForbiddenSession_DoesNotLoadSettingsOrFeedback(HttpStatusCode status)
    {
        using var handler = new FeedbackHandler { ProposalResponseStatus = status };
        var component = this.RenderPage(handler);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[role=alert]")));
        Assert.Empty(component.FindAll("form"));
        Assert.Equal(0, handler.SettingsRequests);
        Assert.Equal(0, handler.FeedbackRequests);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("feedback")]
    public void LoadFailure_ShowsErrorAndRetryRecovers(string failingEndpoint)
    {
        using var handler = new FeedbackHandler { FailingEndpoint = failingEndpoint };
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Contains("Unable to load session feedback", component.Find("[role=alert]").TextContent));
        handler.FailingEndpoint = null;

        component.Find("button").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void MissingSettings_ShowsEmptyEditableFormAndEmptyFeedbackState()
    {
        using var handler = new FeedbackHandler { Settings = null, Feedback = [] };
        var component = this.RenderPage(handler);

        component.WaitForAssertion(() => Assert.Contains("Feedback is not configured", component.Markup));
        Assert.True(string.IsNullOrEmpty(component.Find("#feedback-from").GetAttribute("value")));
        Assert.Contains("No feedback received yet", component.Markup);
        Assert.False(component.Find("#export-feedback").HasAttribute("disabled"));
        Assert.Single(component.FindAll("form"));
    }

    [Theory]
    [InlineData(2001)]
    [InlineData(2099)]
    public void OutsideValidity_StillShowsTitleSettingsAndNewestFirstFeedback(int year)
    {
        using var handler = new FeedbackHandler();
        handler.Settings = handler.Settings! with
        {
            ValidFrom = new DateTimeOffset(year, 1, 1, 10, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(year, 1, 1, 11, 0, 0, TimeSpan.Zero)
        };
        var component = this.RenderPage(handler);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));
        Assert.Equal(handler.Proposal.Title, component.Find("h1").TextContent);
        Assert.Equal($"{year}-01-01T11:00:00", component.Find("#feedback-from").GetAttribute("value"));
        Assert.Equal("Newest", component.FindAll("tbody tr")[0].Children[3].TextContent);
        Assert.Equal("Oldest", component.FindAll("tbody tr")[1].Children[3].TextContent);
        Assert.DoesNotContain("192.0.2.10", component.Markup);
        Assert.DoesNotContain("Example agent", component.Markup);
        Assert.All(handler.GetRequests, uri => Assert.Contains("userId=test-user", uri.Query));
        Assert.DoesNotContain(handler.GetRequests, uri => uri.AbsolutePath.StartsWith("/api/feedback/", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_ConvertsNamedZoneToUtcAndReloadsFeedback()
    {
        using var handler = new FeedbackHandler { Settings = null };
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, "2027-07-01T12:30:00", "2027-07-01T14:00:00", "Europe/Rome");
        handler.Feedback = [handler.Feedback[0] with { Comment = "Refreshed" }];

        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains("Feedback settings saved", component.Find(".notice-success").TextContent));
        Assert.Equal("test-user", handler.SavedUserId);
        Assert.Equal(new DateTimeOffset(2027, 7, 1, 10, 30, 0, TimeSpan.Zero), handler.SavedRequest!.ValidFrom);
        Assert.Equal(new DateTimeOffset(2027, 7, 1, 12, 0, 0, TimeSpan.Zero), handler.SavedRequest.ValidTo);
        Assert.Equal("Europe/Rome", handler.SavedRequest.TimeZoneId);
        Assert.Equal(2, handler.FeedbackRequests);
        Assert.Contains("Refreshed", component.Find("tbody").TextContent);
        Assert.Equal("2027-07-01T12:30:00", component.Find("#feedback-from").GetAttribute("value"));
    }

    [Fact]
    public void TimeZoneChange_KeepsLocalTimesAndSavesReinterpretedInstants()
    {
        using var handler = new FeedbackHandler();
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        Assert.Equal("2027-01-01T11:00:00", component.Find("#feedback-from").GetAttribute("value"));
        component.Find("#feedback-timezone").Change("America/New_York");
        Assert.Equal("2027-01-01T11:00:00", component.Find("#feedback-from").GetAttribute("value"));

        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains("Feedback settings saved", component.Markup));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 16, 0, 0, TimeSpan.Zero), handler.SavedRequest!.ValidFrom);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 17, 0, 0, TimeSpan.Zero), handler.SavedRequest.ValidTo);
    }

    [Theory]
    [InlineData("", "2027-07-01T14:00:00", "UTC", "Both start and end")]
    [InlineData("2027-07-01T14:00:00", "2027-07-01T14:00:00", "UTC", "must precede")]
    [InlineData("2027-07-01T15:00:00", "2027-07-01T14:00:00", "UTC", "must precede")]
    [InlineData("2027-03-28T02:30:00", "2027-03-28T04:00:00", "Europe/Rome", "daylight saving")]
    [InlineData("2027-10-31T02:30:00", "2027-10-31T04:00:00", "Europe/Rome", "daylight saving")]
    [InlineData("2027-07-01T12:00:00", "2027-07-01T14:00:00", "Unknown/Zone", "Unable to save")]
    public void InvalidPeriod_ShowsErrorWithoutSaving(string from, string to, string zone, string error)
    {
        using var handler = new FeedbackHandler();
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, from, to, zone);

        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Contains(error, component.Find("[role=alert]").TextContent));
        Assert.Null(handler.SavedRequest);
        Assert.False(component.Find("fieldset").HasAttribute("disabled"));
    }

    [Fact]
    public void SaveFailure_PreservesDraftAndShowsServerErrorThenAllowsRetry()
    {
        using var handler = new FeedbackHandler { FailSave = true };
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, "2027-07-01T12:00:00", "2027-07-01T14:00:00", "UTC");
        component.Find("form").Submit();
        component.WaitForAssertion(() => Assert.Contains("Server rejected settings", component.Find("[role=alert]").TextContent));
        Assert.Equal("2027-07-01T12:00:00", component.Find("#feedback-from").GetAttribute("value"));
        Assert.False(component.Find("fieldset").HasAttribute("disabled"));
        handler.FailSave = false;

        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=alert]")));
        Assert.Contains("Feedback settings saved", component.Markup);
    }

    [Fact]
    public void SaveRefreshFailure_DisablesStaleExportAndRefreshRecovers()
    {
        using var handler = new FeedbackHandler();
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, "2027-07-01T12:00:00", "2027-07-01T14:00:00", "UTC");
        handler.FailingEndpoint = "feedback";

        component.Find("form").Submit();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[role=alert]")));
        Assert.Contains("Feedback settings saved", component.Markup);
        Assert.True(component.Find("#export-feedback").HasAttribute("disabled"));
        Assert.Empty(component.FindAll("tbody"));
        handler.FailingEndpoint = null;
        component.Find("#refresh-feedback").Click();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("tbody tr").Count));
        Assert.False(component.Find("#export-feedback").HasAttribute("disabled"));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public async Task PendingSave_DisablesControlsAndDoesNotDuplicateSubmission()
    {
        using var handler = new FeedbackHandler { DelayedSave = new() };
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, "2027-07-01T12:00:00", "2027-07-01T14:00:00", "UTC");
        var submit = component.Find("form").SubmitAsync();
        component.WaitForAssertion(() => Assert.True(component.Find("fieldset").HasAttribute("disabled")));
        Assert.True(component.Find("#export-feedback").HasAttribute("disabled"));
        Assert.True(component.Find("#download-feedback-qr").HasAttribute("disabled"));
        await component.Find("form").SubmitAsync();
        Assert.Equal(1, handler.SaveRequests);
        handler.DelayedSave.SetResult(FeedbackHandler.Json(new FeedbackSettingsDto(handler.Proposal.Id,
            handler.SavedRequest!.ValidFrom, handler.SavedRequest.ValidTo, handler.SavedRequest.TimeZoneId)));
        await submit;
        Assert.False(component.Find("fieldset").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/relative")]
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com?query=value")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://name:password@example.com")]
    public void InvalidQrConfiguration_ShowsErrorWithoutBlockingManagement(string? baseUrl)
    {
        using var handler = new FeedbackHandler();
        var component = this.RenderPage(handler, baseUrl);

        component.WaitForAssertion(() => Assert.Contains("Feedback:PublicBaseUrl", component.Find("[role=alert]").TextContent));
        Assert.True(component.Find("#download-feedback-qr").HasAttribute("disabled"));
        Assert.False(component.Find("#export-feedback").HasAttribute("disabled"));
        Assert.Single(component.FindAll("form"));
        Assert.Empty(component.FindAll("img"));
    }

    [Theory]
    [InlineData("https://public.example.com")]
    [InlineData("https://public.example.com/app/")]
    public void QrDownload_EncodesConfiguredAbsoluteUrlAndDownloadsMatchingPng(string baseUrl)
    {
        using var handler = new FeedbackHandler();
        var url = $"{baseUrl.TrimEnd('/')}/feedback/{handler.Proposal.Id:D}";
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        var expected = png.GetGraphic(8);
        byte[]? downloaded = null;
        var module = this.JSInterop.SetupModule(_ => true);
        module.SetupVoid("downloadFile", invocation =>
        {
            Assert.Equal($"talksops-feedback-{handler.Proposal.Id:D}.png", invocation.Arguments[0]);
            Assert.Equal("image/png", invocation.Arguments[2]);
            var reference = Assert.IsType<DotNetStreamReference>(invocation.Arguments[1]);
            using var stream = new MemoryStream();
            reference.Stream.CopyTo(stream);
            downloaded = stream.ToArray();
            return true;
        }).SetVoidResult();
        var component = this.RenderPage(handler, baseUrl);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("img")));
        Assert.Equal(url, component.Find("#feedback-public-link").GetAttribute("href"));
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(expected)}", component.Find("img").GetAttribute("src"));

        component.Find("#download-feedback-qr").Click();

        component.WaitForAssertion(() => Assert.Equal(expected, downloaded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonExport_ContainsOnlyTransferableFieldsWithOriginalTimestampsAndNewestFirst(bool empty)
    {
        using var handler = new FeedbackHandler();
        if (empty) handler.Feedback = [];
        JsonElement[]? downloaded = null;
        var module = this.JSInterop.SetupModule(_ => true);
        module.SetupVoid("downloadFile", invocation =>
        {
            Assert.Equal($"talksops-feedback-{handler.Proposal.Id:D}.json", invocation.Arguments[0]);
            Assert.Equal("application/json", invocation.Arguments[2]);
            var reference = Assert.IsType<DotNetStreamReference>(invocation.Arguments[1]);
            using var document = JsonDocument.Parse(reference.Stream);
            downloaded = document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
            return true;
        }).SetVoidResult();
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("#export-feedback")));

        component.Find("#export-feedback").Click();

        component.WaitForAssertion(() => Assert.NotNull(downloaded));
        Assert.Equal(handler.Feedback.Count, downloaded!.Length);
        var expected = handler.Feedback.OrderByDescending(item => item.Timestamp).ToArray();
        for (var index = 0; index < downloaded.Length; index++)
        {
            var item = downloaded[index];
            Assert.Equal(["sessionRating", "speakerRating", "comment", "timestamp"], item.EnumerateObject().Select(property => property.Name));
            Assert.Equal(expected[index].Timestamp, item.GetProperty("timestamp").GetDateTimeOffset());
            Assert.Equal(expected[index].SessionRating, item.GetProperty("sessionRating").GetInt32());
            Assert.Equal(expected[index].SpeakerRating, item.GetProperty("speakerRating").GetInt32());
            Assert.Equal(expected[index].Comment, item.GetProperty("comment").GetString());
        }
    }

    [Theory]
    [InlineData("export-feedback")]
    [InlineData("download-feedback-qr")]
    public void BrowserDownloadFailure_ShowsErrorAndAllowsRetry(string buttonId)
    {
        var module = this.JSInterop.SetupModule(_ => true);
        var download = module.SetupVoid("downloadFile", _ => true);
        download.SetException(new JSException("Download failed"));
        using var handler = new FeedbackHandler();
        var component = this.RenderPage(handler);
        component.WaitForAssertion(() => Assert.Single(component.FindAll($"#{buttonId}")));
        component.Find($"#{buttonId}").Click();
        component.WaitForAssertion(() => Assert.Contains("Unable to download", component.Find("[role=alert]").TextContent));
        Assert.False(component.Find($"#{buttonId}").HasAttribute("disabled"));
        download.SetVoidResult();

        component.Find($"#{buttonId}").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=alert]")));
        Assert.Equal(2, module.Invocations["downloadFile"].Count);
    }

    [Fact]
    public async Task RouteChangeDuringLoad_DoesNotRestorePreviousSession()
    {
        using var handler = new FeedbackHandler { DelayedProposal = new() };
        var component = this.RenderPage(handler);
        var pending = handler.DelayedProposal;
        handler.DelayedProposal = null;
        handler.ProposalResponseStatus = HttpStatusCode.NotFound;

        await component.InvokeAsync(() => component.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(SessionFeedback.ProposalId)] = Guid.NewGuid() })));
        component.WaitForAssertion(() => Assert.Contains("unavailable", component.Find("[role=alert]").TextContent));
        pending.SetResult(FeedbackHandler.Json(handler.Proposal));
        await component.InvokeAsync(() => Task.CompletedTask);

        Assert.Equal("Session feedback", component.Find("h1").TextContent);
        Assert.Empty(component.FindAll("form"));
        Assert.Equal(0, handler.SettingsRequests);
    }

    [Fact]
    public async Task RouteChangeDuringSave_DoesNotRefreshOrRestorePreviousSession()
    {
        using var handler = new FeedbackHandler { DelayedSave = new() };
        var component = this.RenderPage(handler);
        this.EnterPeriod(component, "2027-07-01T12:00:00", "2027-07-01T14:00:00", "UTC");
        var submit = component.Find("form").SubmitAsync();
        component.WaitForAssertion(() => Assert.Equal(1, handler.SaveRequests));
        handler.ProposalResponseStatus = HttpStatusCode.NotFound;

        await component.InvokeAsync(() => component.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(SessionFeedback.ProposalId)] = Guid.NewGuid() })));
        component.WaitForAssertion(() => Assert.Contains("unavailable", component.Find("[role=alert]").TextContent));
        handler.DelayedSave.SetResult(FeedbackHandler.Json(handler.Settings));
        await submit;

        Assert.Equal("Session feedback", component.Find("h1").TextContent);
        Assert.Empty(component.FindAll("form"));
        Assert.Equal(1, handler.FeedbackRequests);
        Assert.Empty(component.FindAll(".notice-success"));
    }

    private IRenderedComponent<SessionFeedback> RenderPage(FeedbackHandler handler, string? baseUrl = "https://public.example.com")
    {
        this.RegisterServices(handler, baseUrl);
        return this.Render<SessionFeedback>(parameters => parameters
            .Add(page => page.EventId, handler.Proposal.EventId)
            .Add(page => page.ProposalId, handler.Proposal.Id));
    }

    private void RegisterServices(FeedbackHandler handler, string? baseUrl)
    {
        this.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Feedback:PublicBaseUrl"] = baseUrl }).Build());
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com/") }, new TestUser()));
    }

    private void EnterPeriod(IRenderedComponent<SessionFeedback> component, string from, string to, string zone)
    {
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        component.Find("#feedback-from").Change(from);
        component.Find("#feedback-to").Change(to);
        component.Find("#feedback-timezone").Change(zone);
    }

    private sealed class TestUser : ICurrentUserContext
    {
        public string UserId => "test-user";
    }

    private sealed class FeedbackHandler : HttpMessageHandler
    {
        public SessionProposalDto Proposal { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "test-user",
            "Feedback session", "Abstract", "Notes", ProposalStatus.Submitted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        public FeedbackSettingsDto? Settings { get; set; }
        public IReadOnlyList<SessionFeedbackDto> Feedback { get; set; }
        public List<Uri> GetRequests { get; } = [];
        public HttpStatusCode ProposalResponseStatus { get; set; } = HttpStatusCode.OK;
        public string? FailingEndpoint { get; set; }
        public bool FailSave { get; set; }
        public int SettingsRequests { get; private set; }
        public int FeedbackRequests { get; private set; }
        public int SaveRequests { get; private set; }
        public string? SavedUserId { get; private set; }
        public SaveFeedbackSettingsRequest? SavedRequest { get; private set; }
        public TaskCompletionSource<HttpResponseMessage>? DelayedProposal { get; set; }
        public TaskCompletionSource<HttpResponseMessage>? DelayedSave { get; set; }

        public FeedbackHandler()
        {
            this.Settings = new(this.Proposal.Id, new(2027, 1, 1, 10, 0, 0, TimeSpan.Zero),
                new(2027, 1, 1, 11, 0, 0, TimeSpan.Zero), "Europe/Rome");
            var metadata = new FeedbackMetadataDto("192.0.2.10", "Example agent");
            this.Feedback =
            [
                new(Guid.NewGuid(), this.Proposal.Id, 7, 8, "Oldest", new(2027, 1, 1, 10, 0, 0, TimeSpan.Zero), metadata),
                new(Guid.NewGuid(), this.Proposal.Id, 9, 10, "Newest", new(2027, 1, 1, 11, 0, 0, TimeSpan.Zero), metadata)
            ];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                this.SaveRequests++;
                var envelope = await request.Content!.ReadFromJsonAsync<ApiRequest<SaveFeedbackSettingsRequest>>(cancellationToken);
                this.SavedRequest = envelope!.Payload;
                this.SavedUserId = envelope.UserId;
                if (this.DelayedSave is not null) return await this.DelayedSave.Task;
                if (this.FailSave) return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { message = "Server rejected settings" }) };
                this.Settings = new(this.Proposal.Id, this.SavedRequest.ValidFrom, this.SavedRequest.ValidTo, this.SavedRequest.TimeZoneId);
                return Json(this.Settings);
            }

            this.GetRequests.Add(uri);
            if (path.EndsWith("/feedback/settings", StringComparison.Ordinal))
            {
                this.SettingsRequests++;
                if (this.FailingEndpoint == "settings") return new(HttpStatusCode.ServiceUnavailable);
                return this.Settings is null ? new(HttpStatusCode.NotFound) : Json(this.Settings);
            }
            if (path.EndsWith("/feedback", StringComparison.Ordinal))
            {
                this.FeedbackRequests++;
                return this.FailingEndpoint == "feedback" ? new(HttpStatusCode.ServiceUnavailable) : Json(this.Feedback);
            }
            if (this.DelayedProposal is not null) return await this.DelayedProposal.Task;
            return this.ProposalResponseStatus == HttpStatusCode.OK ? Json(this.Proposal) : new(this.ProposalResponseStatus);
        }

        public static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
