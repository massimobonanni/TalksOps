using System.Security.Claims;
using TalksOps.Core.Contracts;

namespace TalksOps.Web.Security;

/// <summary>Provides the authenticated login username to API calls made by the current Blazor circuit.</summary>
public sealed class WebCurrentUserContext : ICurrentUserContext
{
    private ClaimsPrincipal? principal;

    /// <inheritdoc />
    public string UserId
    {
        get
        {
            if (this.principal?.Identity?.IsAuthenticated != true)
            {
                throw new InvalidOperationException("The current user is not authenticated.");
            }

            string[] claimTypes = ["preferred_username", "upn", ClaimTypes.Upn];
            foreach (var claimType in claimTypes)
            {
                var username = this.principal.FindFirstValue(claimType);
                if (!string.IsNullOrWhiteSpace(username))
                {
                    return username.Trim().ToLowerInvariant();
                }
            }

            throw new InvalidOperationException("The current user does not have a login username claim.");
        }
    }

    /// <summary>Sets the principal associated with this circuit.</summary>
    public void SetPrincipal(ClaimsPrincipal authenticatedPrincipal)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);
        this.principal = authenticatedPrincipal;
    }
}