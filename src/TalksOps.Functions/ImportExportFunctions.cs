using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using TalksOps.ApiClient.Contracts;
using TalksOps.Core.Contracts;
using TalksOps.Core.Export;

namespace TalksOps.Functions;

/// <summary>HTTP endpoints to export and import the current user's events with their sessions.</summary>
public sealed class ImportExportFunctions(
    IEventImportExportService importExport,
    RequestCurrentUserContext currentUser)
{
    private readonly IEventImportExportService importExport = importExport ?? throw new ArgumentNullException(nameof(importExport));
    private readonly RequestCurrentUserContext currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <summary>Exports the user's events overlapping the optional <c>from</c>/<c>to</c> range (yyyy-MM-dd).</summary>
    [Function(nameof(ExportEvents))]
    public async Task<IActionResult> ExportEvents(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "export")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.currentUser.TryInitialize(request.Query["userId"].ToString()))
        {
            return new BadRequestObjectResult("A user identifier is required.");
        }

        if (!TryReadOptionalDate(request, "from", out var from) || !TryReadOptionalDate(request, "to", out var to))
        {
            return new BadRequestObjectResult("The from and to dates must use the yyyy-MM-dd format.");
        }

        try
        {
            return new OkObjectResult(await this.importExport.ExportAsync(from, to, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return new BadRequestObjectResult(exception.Message);
        }
    }

    /// <summary>Adds the events and sessions contained in an export document to the user's data.</summary>
    [Function(nameof(ImportEvents))]
    public async Task<IActionResult> ImportEvents(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "import")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        ApiRequest<EventsExportDocument>? envelope;
        try
        {
            envelope = await request.ReadFromJsonAsync<ApiRequest<EventsExportDocument>>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("The request body is not a valid TalksOps events export.");
        }

        if (envelope?.Payload is null)
        {
            return new BadRequestObjectResult("A valid request envelope and payload are required.");
        }

        if (!this.currentUser.TryInitialize(envelope.UserId))
        {
            return new BadRequestObjectResult("A user identifier is required.");
        }

        try
        {
            return new OkObjectResult(await this.importExport.ImportAsync(envelope.Payload, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            return new BadRequestObjectResult(exception.Message);
        }
    }

    private static bool TryReadOptionalDate(HttpRequest request, string name, out DateOnly? value)
    {
        var rawValue = request.Query[name].ToString();
        if (string.IsNullOrEmpty(rawValue))
        {
            value = null;
            return true;
        }

        var valid = DateOnly.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
        value = valid ? parsed : null;
        return valid;
    }
}
