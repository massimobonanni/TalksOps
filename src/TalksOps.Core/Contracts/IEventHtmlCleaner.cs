namespace TalksOps.Core.Contracts;

/// <summary>Cleans event-page HTML into concise text suitable for model input.</summary>
public interface IEventHtmlCleaner
{
    /// <summary>Extracts relevant page content and resolves links against the source page URI.</summary>
    string Clean(string html, Uri sourcePageUri);
}