using TalksOps.Core.Contracts;

namespace TalksOps.Functions;

/// <summary>Holds the caller identifier for one Functions invocation.</summary>
public sealed class RequestCurrentUserContext : ICurrentUserContext
{
    private string? userId;

    /// <inheritdoc />
    public string UserId => this.userId
        ?? throw new InvalidOperationException("The request user has not been initialized.");

    /// <summary>Sets the user identifier supplied by the authenticated server-side client.</summary>
    public bool TryInitialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        this.userId = value.Trim();
        return true;
    }
}