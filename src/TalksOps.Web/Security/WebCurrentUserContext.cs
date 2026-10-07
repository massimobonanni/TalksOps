using System.Security.Claims;
using TalksOps.Core.Contracts;

namespace TalksOps.Web.Security;

/// <summary>Provides the authenticated subject to API calls made by the current Blazor circuit.</summary>
public sealed class WebCurrentUserContext : ICurrentUserContext
{
    private ClaimsPrincipal? principal;

    /// <inheritdoc />
    public string UserId => this.principal?.FindFirstValue("sub")
        ?? this.principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The current user is not authenticated.");

    /// <summary>Sets the principal associated with this circuit.</summary>
    public void SetPrincipal(ClaimsPrincipal authenticatedPrincipal)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);
        this.principal = authenticatedPrincipal;
    }
}