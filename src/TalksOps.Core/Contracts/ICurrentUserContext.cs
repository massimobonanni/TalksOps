namespace TalksOps.Core.Contracts;

/// <summary>Provides the identity of the authenticated user for the current request.</summary>
public interface ICurrentUserContext
{
    /// <summary>Gets the authenticated user's owner key, supplied as a normalized login username by the Web application.</summary>
    string UserId { get; }
}