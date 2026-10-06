namespace TalksOps.Core.Contracts;

/// <summary>Provides the identity of the authenticated user for the current request.</summary>
public interface ICurrentUserContext
{
    /// <summary>Gets the authenticated user's stable subject identifier.</summary>
    string UserId { get; }
}