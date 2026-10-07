using System.Security.Claims;
using TalksOps.Web.Security;

namespace TalksOps.Web.Tests;

public sealed class WebCurrentUserContextTests
{
    [Fact]
    public void UserId_UsesSubjectClaimBeforeNameIdentifier()
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "stable-subject"),
            new Claim(ClaimTypes.NameIdentifier, "mapped-name-identifier")
        ], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("stable-subject", context.UserId);
    }

    [Fact]
    public void UserId_UsesNameIdentifierWhenSubjectClaimIsNotMapped()
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "mapped-name-identifier")], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("mapped-name-identifier", context.UserId);
    }
}