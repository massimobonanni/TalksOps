using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TalksOps.ApiClient;
using TalksOps.Core.Contracts;
using TalksOps.Core.Domain;
using TalksOps.Core.Export;
using TalksOps.Web.Components.Pages;

namespace TalksOps.Web.Tests;

public sealed class ImportExportTests : BunitContext
{
    [Fact]
    public void DownloadTemplate_ProducesValidImportDocumentWithoutSelectingAFile()
    {
        EventsExportDocument? downloaded = null;
        var module = this.JSInterop.SetupModule(_ => true);
        module.SetupVoid("downloadFile", invocation =>
        {
            Assert.Equal("talksops-import-template.json", invocation.Arguments[0]);
            var reference = Assert.IsType<DotNetStreamReference>(invocation.Arguments[1]);
            using var json = JsonDocument.Parse(reference.Stream);
            var exportedEvent = Assert.Single(json.RootElement.GetProperty("events").EnumerateArray());
            Assert.False(exportedEvent.TryGetProperty("id", out _));
            var session = Assert.Single(exportedEvent.GetProperty("sessions").EnumerateArray());
            Assert.False(session.TryGetProperty("id", out _));
            downloaded = json.Deserialize<EventsExportDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return true;
        }).SetVoidResult();
        var component = this.RenderImportExport();
        Assert.True(component.FindAll("button[type=submit]")[1].HasAttribute("disabled"));

        component.Find("button[type=button]").Click();

        component.WaitForAssertion(() => Assert.NotNull(downloaded));
        var document = Assert.IsType<EventsExportDocument>(downloaded);
        Assert.Equal(EventsExportDocument.CurrentSchemaVersion, document.SchemaVersion);
        var exportedEvent = Assert.Single(document.Events);
        var eventId = Guid.NewGuid();
        Assert.Empty(EventRules.Validate(new Event
        {
            Id = eventId,
            OwnerId = "test-user",
            Name = exportedEvent.Name,
            Location = exportedEvent.Location,
            OfficialWebsiteUrl = exportedEvent.OfficialWebsiteUrl,
            CallForPapersUrl = exportedEvent.CallForPapersUrl,
            CallForPapersDeadline = exportedEvent.CallForPapersDeadline,
            StartDate = exportedEvent.StartDate,
            EndDate = exportedEvent.EndDate,
            Costs = exportedEvent.Costs
        }));
        var session = Assert.Single(exportedEvent.Sessions);
        Assert.True(Enum.IsDefined(session.Status));
        Assert.Empty(SessionProposalRules.Validate(new SessionProposal
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            OwnerId = "test-user",
            Title = session.Title,
            Abstract = session.Abstract,
            Notes = session.Notes,
            SubmissionDate = session.SubmissionDate,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        }));
        Assert.Empty(component.FindAll("[role=alert]"));
        Assert.False(component.Find("button[type=button]").HasAttribute("disabled"));
    }

    [Fact]
    public void DownloadTemplate_BrowserFailureShowsErrorAndAllowsRetry()
    {
        var module = this.JSInterop.SetupModule(_ => true);
        var download = module.SetupVoid("downloadFile", _ => true);
        download.SetException(new JSException("Download failed."));
        var component = this.RenderImportExport();

        component.Find("button[type=button]").Click();

        component.WaitForAssertion(() => Assert.Contains("Unable to download the import template", component.Find("[role=alert]").TextContent));
        Assert.False(component.Find("button[type=button]").HasAttribute("disabled"));
        download.SetVoidResult();

        component.Find("button[type=button]").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=alert]")));
        Assert.Equal(2, module.Invocations["downloadFile"].Count);
    }

    private IRenderedComponent<ImportExport> RenderImportExport()
    {
        this.Services.AddSingleton<ITalksOpsApiClient>(new TalksOpsApiClient(new HttpClient(), new TestUser()));
        return this.Render<ImportExport>();
    }

    private sealed class TestUser : ICurrentUserContext
    {
        public string UserId => "test-user";
    }
}