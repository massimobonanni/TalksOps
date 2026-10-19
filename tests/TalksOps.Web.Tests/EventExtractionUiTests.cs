using System.Globalization;
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
        public int ExtractEventDataCallCount { get; private set; }
        public SaveEventRequest? CreatedEventRequest { get; private set; }

        public StubApiClient(ExtractEventDataResultDto extractResult)
        {
            _extractResult = extractResult;
        }

        public Task<PagedResponse<EventDto>> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PagedResponse<EventDto>(Array.Empty<EventDto>(), 1, 20, 0));
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
            return Task.FromResult(_extractResult);
        }
    }

    [Fact]
    public void ShowsExtractButton_WhenFeatureFlagEnabled()
    {
        using var ctx = new TestContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", "true")
        }).Build();

        ctx.Services.AddSingleton<IConfiguration>(config);
        // Minimal API stub that returns empty search results
        ctx.Services.AddSingleton<ITalksOpsApiClient>(new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null)));

        var comp = ctx.Render<Events>();

        // Open create event editor by clicking New event button
        var newEventButton = comp.Find("button.button-primary");
        newEventButton.Click();

        // The extract control should be present
        var extractButtons = comp.FindAll("button").Where(b => b.TextContent.Contains("Extract from URL")).ToArray();
        Assert.NotEmpty(extractButtons);
    }

    [Fact]
    public void DoesNotShowExtractButton_WhenFeatureFlagDisabled()
    {
        using var ctx = new TestContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", "false")
        }).Build();

        ctx.Services.AddSingleton<IConfiguration>(config);
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));
        ctx.Services.AddSingleton<ITalksOpsApiClient>(apiClient);

        var comp = ctx.Render<Events>();

        var newEventButton = comp.Find("button.button-primary");
        newEventButton.Click();

        var extractButtons = comp.FindAll("button").Where(b => b.TextContent.Contains("Extract from URL")).ToArray();
        Assert.Empty(extractButtons);
    }

    [Fact]
    public async Task SavesEventManuallyWhenExtractionFeatureIsDisabled()
    {
        using var ctx = new TestContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("Features:AiExtraction", "false")
        }).Build();
        var apiClient = new StubApiClient(new ExtractEventDataResultDto(null, null, null, null, null, null, null));

        ctx.Services.AddSingleton<IConfiguration>(config);
        ctx.Services.AddSingleton<ITalksOpsApiClient>(apiClient);

        var comp = ctx.Render<Events>();
        comp.Find("button.button-primary").Click();
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
    public async Task ExtractEventData_PrefillsEmptyFields()
    {
        using var ctx = new TestContext();

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

        // Open create event editor
        var newEventButton = comp.Find("button.button-primary");
        newEventButton.Click();

        // Click Extract from URL
        var extractFromUrlButton = comp.FindAll("button").First(b => b.TextContent.Contains("Extract from URL"));
        extractFromUrlButton.Click();

        // Fill the URL input and click Extract
                var urlInput = comp.Find("#extract-source-url");
        urlInput.Input("https://cfp.example/");

        var extractButton = comp.FindAll("button").First(b => b.TextContent.Trim() == "Extract");
        extractButton.Click();
        Assert.Equal(1, apiClient.ExtractEventDataCallCount);
        Assert.Equal(0, apiClient.CreateEventCallCount);

        // Wait for async operations to complete and assert eventual values
        comp.WaitForAssertion(() =>
        {
            // Find Event name label and input value
            var label = comp.FindAll("label").Last(l => l.TextContent.Contains("Event name"));
            var input = label.GetElementsByTagName("input").First();
            Assert.Equal("Extracted Event Name", input.GetAttribute("value"));

            // Location input
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
    }
}
