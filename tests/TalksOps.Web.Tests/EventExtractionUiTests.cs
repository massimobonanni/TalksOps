using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalksOps.ApiClient;
using TalksOps.ApiClient.Contracts;
using TalksOps.Web.Components.Pages;
using TalksOps.Core.Export;
using Xunit;

namespace TalksOps.Web.Tests;

public class EventExtractionUiTests
{
    private sealed class StubApiClient : ITalksOpsApiClient
    {
        private readonly ExtractEventDataResultDto _extractResult;

        public int CreateEventCallCount { get; private set; }
        public int UpdateEventCallCount { get; private set; }
        public int ExtractEventDataCallCount { get; private set; }
        public SaveEventRequest? CreatedEventRequest { get; private set; }
        public ExtractEventDataRequest? ExtractionRequest { get; private set; }
        public Exception? ExtractionException { get; set; }
        public Task<ExtractEventDataResultDto>? PendingExtraction { get; set; }
        public IReadOnlyList<EventDto> Events { get; init; } = [];

        public StubApiClient(ExtractEventDataResultDto extractResult)
        {
            _extractResult = extractResult;
        }

        public Task<PagedResponse<EventDto>> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PagedResponse<EventDto>(Events, 1, 20, Events.Count));
        }

        public Task<EventDto?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default) => Task.FromResult<EventDto?>(null);
                public Task<EventDto> CreateEventAsync(SaveEventRequest request, CancellationToken cancellationToken = default)
                {
                    CreateEventCallCount++;
                    CreatedEventRequest = request;
                    var created = new EventDto(
                        Guid.NewGuid(),
                        "test-owner",
                        request.Name,
                        request.Location,
                        request.StartDate,
                        request.EndDate,
                        request.Costs,
                        request.OfficialWebsiteUrl,
                        request.CallForPapersUrl,
                        request.CallForPapersDeadline);
                    return Task.FromResult(created);
                }

                public Task<EventDto> UpdateEventAsync(Guid eventId, SaveEventRequest request, CancellationToken cancellationToken = default)
                {
                    UpdateEventCallCount++;
                    var updated = new EventDto(
                        eventId,
                        "test-owner",
                        request.Name,
                        request.Location,
                        request.StartDate,
                        request.EndDate,
                        request.Costs,
                        request.OfficialWebsiteUrl,
                        request.CallForPapersUrl,
                        request.CallForPapersDeadline);
                    return Task.FromResult(updated);
                }
        public Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SessionProposalDto>> ListProposalsAsync(Guid eventId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SessionProposalDto?> GetProposalAsync(Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SessionProposalDto> CreateProposalAsync(Guid eventId, SaveSessionProposalRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SessionProposalDto> UpdateProposalAsync(Guid eventId, Guid proposalId, SaveSessionProposalRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SessionProposalDto> ChangeProposalStatusAsync(Guid eventId, Guid proposalId, ChangeProposalStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteProposalAsync(Guid eventId, Guid proposalId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(int year, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<CalendarEventDto>)Array.Empty<CalendarEventDto>());
        public Task<EventsExportDocument> ExportEventsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<EventImportResult> ImportEventsAsync(EventsExportDocument document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ExtractEventDataResultDto> ExtractEventDataAsync(ExtractEventDataRequest request, CancellationToken cancellationToken = default)
        {
            ExtractEventDataCallCount++;
            ExtractionRequest = request;
            return ExtractionException is not null
                ? Task.FromException<ExtractEventDataResultDto>(ExtractionException)
                : PendingExtraction ?? Task.FromResult(_extractResult);
        }
    }

    [Fact]
    public void ShowsExtractionDialogFromDropdown_WhenFeatureFlagEnabled()
    {
        using var ctx = new BunitContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", "true")
        }).Build();

        ctx.Services.AddSingleton<IConfiguration>(config);
        ctx.Services.AddSingleton<ITalksOpsApiClient>(new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null)));

        var comp = ctx.Render<Events>();

        Assert.Empty(comp.FindAll("[role=dialog]"));
        Assert.DoesNotContain("Extract from URL", comp.Markup);

        var dropdown = comp.Find("button[aria-label='More event creation options']");
        Assert.Equal("false", dropdown.GetAttribute("aria-expanded"));
        dropdown.Click();
        Assert.Equal("true", dropdown.GetAttribute("aria-expanded"));
        comp.FindAll("button").Single(button => button.TextContent.Trim() == "Extract from URL").Click();

        var dialog = Assert.Single(comp.FindAll("[role=dialog]"));
        Assert.Contains("Call for paper website", dialog.TextContent);
        Assert.Single(comp.FindAll("#extract-source-url"));
        Assert.Empty(comp.FindAll("#event-editor-title"));
        Assert.Equal("false", dropdown.GetAttribute("aria-expanded"));
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    [InlineData("invalid")]
    public void DoesNotShowExtractionDropdown_WhenFeatureFlagIsNotEnabled(string? featureFlag)
    {
        using var ctx = new BunitContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", featureFlag)
        }).Build();

        ctx.Services.AddSingleton<IConfiguration>(config);
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));
        ctx.Services.AddSingleton<ITalksOpsApiClient>(apiClient);

        var comp = ctx.Render<Events>();

        Assert.Empty(comp.FindAll(".event-create-toggle"));
        var newEventButton = comp.Find("button.button-primary");
        newEventButton.Click();

        var extractButtons = comp.FindAll("button").Where(b => b.TextContent.Contains("Extract from URL")).ToArray();
        Assert.Empty(extractButtons);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void SavesEventManuallyWithoutExtraction(string featureFlag)
    {
        using var ctx = new BunitContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", featureFlag)
        }).Build();
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));

        ctx.Services.AddSingleton<IConfiguration>(config);
        ctx.Services.AddSingleton<ITalksOpsApiClient>(apiClient);

        var comp = ctx.Render<Events>();
        comp.Find("button.button-primary").Click();
        Assert.DoesNotContain("Extract from URL", comp.Find("[role=dialog]").TextContent);
        Assert.Empty(comp.FindAll("#extract-source-url"));
        comp.FindAll("label").Last(label => label.TextContent.Contains("Event name"))
            .GetElementsByTagName("input").First().Change("Manual event");
        comp.FindAll("label").Last(label => label.TextContent.Contains("Location"))
            .GetElementsByTagName("input").First().Change("Rome");

        comp.FindAll("button").First(button => button.TextContent.Trim() == "Save event").Click();

        Assert.Equal(1, apiClient.CreateEventCallCount);
        Assert.Equal(0, apiClient.ExtractEventDataCallCount);
        Assert.Equal("Manual event", apiClient.CreatedEventRequest?.Name);
        Assert.Equal("Rome", apiClient.CreatedEventRequest?.Location);
    }

    [Fact]
    public void ExtractEventData_OpensPrefilledNewEventEditorOnlyAfterSuccess()
    {
        using var ctx = new BunitContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", "true")
        }).Build();

        var extractResult = new ExtractEventDataResultDto(
            "Extracted Event Name",
            "Extracted Location",
            "https://official.example/",
            "https://cfp.example/",
            "01/09/2026",
            "2026-09-10",
            "2026-09-12");
        var apiClient = new StubApiClient(extractResult);

        ctx.Services.AddSingleton<IConfiguration>(config);
        ctx.Services.AddSingleton<ITalksOpsApiClient>(apiClient);

        var comp = ctx.Render<Events>();

        OpenExtractionDialog(comp);
        comp.Find("#extract-source-url").Input(" https://cfp.example/ ");

        comp.Find(".extraction-dialog form").Submit();
        Assert.Equal(1, apiClient.ExtractEventDataCallCount);
        Assert.Equal(0, apiClient.CreateEventCallCount);
        Assert.Equal("https://cfp.example/", apiClient.ExtractionRequest?.SourcePageUrl);

        comp.WaitForAssertion(() =>
        {
            Assert.Single(comp.FindAll("[role=dialog]"));
            Assert.Empty(comp.FindAll("#extract-source-url"));
            Assert.Equal("New event", comp.Find("#event-editor-title").TextContent);
            var label = comp.FindAll("label").Last(l => l.TextContent.Contains("Event name"));
            var input = label.GetElementsByTagName("input").First();
            Assert.Equal("Extracted Event Name", input.GetAttribute("value"));

            var locLabel = comp.FindAll("label").Last(l => l.TextContent.Contains("Location"));
            var locInput = locLabel.GetElementsByTagName("input").First();
            Assert.Equal("Extracted Location", locInput.GetAttribute("value"));

            var officialWebsiteInput = comp.Find("#event-official-website");
            Assert.Equal("https://official.example/", officialWebsiteInput.GetAttribute("value"));
            var cfpInput = comp.Find("#event-call-for-papers-website");
            Assert.Equal("https://cfp.example/", cfpInput.GetAttribute("value"));
            Assert.Equal("2026-09-10", comp.FindAll("input[type=date]")[0].GetAttribute("value"));
            Assert.Equal("2026-09-12", comp.FindAll("input[type=date]")[1].GetAttribute("value"));
            Assert.Equal("2026-09-01", comp.FindAll("input[type=date]")[2].GetAttribute("value"));
        }, timeout: TimeSpan.FromSeconds(2));

        comp.Find(".event-dialog form").Submit();
        Assert.Equal(1, apiClient.CreateEventCallCount);
        Assert.Equal(0, apiClient.UpdateEventCallCount);
        Assert.Equal("Extracted Event Name", apiClient.CreatedEventRequest?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://cfp.example/")]
    public void InvalidUrl_KeepsExtractionDialogOpenWithoutCallingApi(string sourcePageUrl)
    {
        using var context = new BunitContext();
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));
        var component = RenderExtractionEnabledEvents(context, apiClient);
        OpenExtractionDialog(component);
        component.Find("#extract-source-url").Input(sourcePageUrl);

        component.Find(".extraction-dialog form").Submit();

        Assert.Single(component.FindAll("[role=dialog]"));
        Assert.Contains("valid HTTP or HTTPS URL", component.Find(".extraction-dialog [role=alert]").TextContent);
        Assert.Equal(sourcePageUrl, component.Find("#extract-source-url").GetAttribute("value"));
        Assert.Empty(component.FindAll("#event-editor-title"));
        Assert.Equal(0, apiClient.ExtractEventDataCallCount);
        Assert.Equal(0, apiClient.CreateEventCallCount);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("invalid-response")]
    public void ExtractionFailure_ShowsErrorInsideDialogAndAllowsRetry(string failure)
    {
        using var context = new BunitContext();
        const string errorMessage = "Unable to extract the call for papers page.";
        var apiClient = new StubApiClient(new ExtractEventDataResultDto("Recovered event", null, null, null, null, null, null))
        {
            ExtractionException = failure switch
            {
                "http" => new HttpRequestException(errorMessage),
                "timeout" => new TaskCanceledException(errorMessage),
                _ => new InvalidOperationException(errorMessage)
            }
        };
        var component = RenderExtractionEnabledEvents(context, apiClient);
        OpenExtractionDialog(component);
        component.Find("#extract-source-url").Input("https://cfp.example/");

        component.Find(".extraction-dialog form").Submit();

        Assert.Single(component.FindAll("[role=dialog]"));
        Assert.Equal(errorMessage, component.Find(".extraction-dialog [role=alert]").TextContent);
        Assert.Single(component.FindAll("[role=alert]"));
        Assert.Equal("https://cfp.example/", component.Find("#extract-source-url").GetAttribute("value"));
        Assert.Empty(component.FindAll("#event-editor-title"));
        Assert.Equal(0, apiClient.CreateEventCallCount);
        Assert.False(component.Find(".extraction-dialog button[type=submit]").HasAttribute("disabled"));

        apiClient.ExtractionException = null;
        component.Find(".extraction-dialog form").Submit();

        Assert.Equal(2, apiClient.ExtractEventDataCallCount);
        Assert.Empty(component.FindAll(".extraction-dialog"));
        Assert.Empty(component.FindAll("[role=alert]"));
        Assert.Equal("New event", component.Find("#event-editor-title").TextContent);
    }

    [Fact]
    public async Task PendingExtraction_DisablesControlsAndIgnoresDuplicateSubmission()
    {
        using var context = new BunitContext();
        var result = new ExtractEventDataResultDto("Extracted event", null, null, null, null, null, null);
        var pendingExtraction = new TaskCompletionSource<ExtractEventDataResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiClient = new StubApiClient(result) { PendingExtraction = pendingExtraction.Task };
        var component = RenderExtractionEnabledEvents(context, apiClient);
        OpenExtractionDialog(component);
        component.Find("#extract-source-url").Input("https://cfp.example/");

        var submission = component.Find(".extraction-dialog form").SubmitAsync();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find(".extraction-dialog").GetAttribute("aria-busy"));
            Assert.True(component.Find("#extract-source-url").HasAttribute("disabled"));
            Assert.All(component.FindAll(".extraction-dialog button"), button => Assert.True(button.HasAttribute("disabled")));
            Assert.Contains("Extracting", component.Find(".extraction-dialog [role=status]").TextContent);
            Assert.Empty(component.FindAll("#event-editor-title"));
        });

        await component.Find(".extraction-dialog form").SubmitAsync();
        Assert.Equal(1, apiClient.ExtractEventDataCallCount);

        pendingExtraction.SetResult(result);
        await submission;

        Assert.Empty(component.FindAll(".extraction-dialog"));
        Assert.Equal("New event", component.Find("#event-editor-title").TextContent);
        Assert.Equal(0, apiClient.CreateEventCallCount);
    }

    [Fact]
    public void PartialExtraction_CreatesFreshDraftAfterEditingAnotherEvent()
    {
        using var context = new BunitContext();
        var previousEvent = new EventDto(Guid.NewGuid(), "test-owner", "Previous event", "Rome",
            new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 2), new Dictionary<string, decimal> { ["Travel"] = 200 },
            "https://previous.example/", "https://previous.example/cfp", new DateOnly(2026, 3, 1));
        var apiClient = new StubApiClient(new ExtractEventDataResultDto("New extracted event", null, null, null, null, "2027-06-12", null))
        {
            Events = [previousEvent]
        };
        var component = RenderExtractionEnabledEvents(context, apiClient);
        component.Find("button[aria-label='Edit Previous event']").Click();
        Assert.DoesNotContain("Extract from URL", component.Find(".event-dialog").TextContent);
        component.Find("button[aria-label=Close]").Click();

        OpenExtractionDialog(component);
        Assert.Equal(string.Empty, component.Find("#extract-source-url").GetAttribute("value"));
        component.Find("#extract-source-url").Input("https://cfp.example/");
        component.Find(".extraction-dialog form").Submit();

        Assert.Equal("New event", component.Find("#event-editor-title").TextContent);
        Assert.Equal(string.Empty, component.Find("#event-official-website").GetAttribute("value"));
        Assert.Equal(string.Empty, component.Find("#event-call-for-papers-website").GetAttribute("value"));
        Assert.Equal("2027-06-12", component.FindAll("input[type=date]")[0].GetAttribute("value"));
        Assert.Equal("2027-06-12", component.FindAll("input[type=date]")[1].GetAttribute("value"));
        Assert.True(string.IsNullOrEmpty(component.FindAll("input[type=date]")[2].GetAttribute("value")));
        Assert.Equal(string.Empty, component.Find(".event-dialog textarea").TextContent);
        var location = component.FindAll(".event-dialog label").Single(label => label.TextContent == "Location")
            .GetElementsByTagName("input").Single();
        Assert.Equal(string.Empty, location.GetAttribute("value"));
        location.Change("Milan");
        component.Find(".event-dialog form").Submit();

        Assert.Equal(1, apiClient.CreateEventCallCount);
        Assert.Equal(0, apiClient.UpdateEventCallCount);
        Assert.Equal("New extracted event", apiClient.CreatedEventRequest?.Name);
    }

    [Fact]
    public void CancelExtraction_ReturnsToEventsAndClearsInputOnReopen()
    {
        using var context = new BunitContext();
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));
        var component = RenderExtractionEnabledEvents(context, apiClient);
        OpenExtractionDialog(component);
        component.Find("#extract-source-url").Input("invalid-url");
        component.Find(".extraction-dialog form").Submit();

        component.Find(".extraction-dialog button[type=button]").Click();

        Assert.Empty(component.FindAll("[role=dialog]"));
        Assert.Equal(0, apiClient.ExtractEventDataCallCount);
        OpenExtractionDialog(component);
        Assert.Equal(string.Empty, component.Find("#extract-source-url").GetAttribute("value"));
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void Dropdown_ClosesOnEscapeOutsideClickAndManualCreation()
    {
        using var context = new BunitContext();
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));
        var component = RenderExtractionEnabledEvents(context, apiClient);
        var toggle = component.Find(".event-create-toggle");

        toggle.Click();
        component.Find(".event-create").KeyDown("Escape");
        Assert.Empty(component.FindAll("#event-create-options"));

        toggle.Click();
        component.Find(".event-create-menu-backdrop").Click();
        Assert.Empty(component.FindAll("#event-create-options"));

        toggle.Click();
        component.Find(".event-create-buttons > button").Click();
        Assert.Empty(component.FindAll("#event-create-options"));
        Assert.Equal("New event", component.Find("#event-editor-title").TextContent);
    }

    private static IRenderedComponent<Events> RenderExtractionEnabledEvents(BunitContext context, StubApiClient apiClient)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:AiExtraction"] = "true"
        }).Build();
        context.Services.AddSingleton<IConfiguration>(configuration);
        context.Services.AddSingleton<ITalksOpsApiClient>(apiClient);
        return context.Render<Events>();
    }

    private static void OpenExtractionDialog(IRenderedComponent<Events> component)
    {
        component.Find(".event-create-toggle").Click();
        component.FindAll("button").Single(button => button.TextContent.Trim() == "Extract from URL").Click();
    }
}
