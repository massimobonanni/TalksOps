using System.Security.Claims;
using TalksOps.Web.Security;

namespace TalksOps.Web.Tests;

public sealed class WebCurrentUserContextTests
{
    [Fact]
    public void UserId_UsesPreferredUsernameBeforeOtherClaims()
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [
            new Claim("preferred_username", "speaker@example.com"),
            new Claim("upn", "other@example.com"),
            new Claim(ClaimTypes.Upn, "mapped@example.com"),
            new Claim("sub", "stable-subject"),
            new Claim(ClaimTypes.NameIdentifier, "mapped-name-identifier"),
            new Claim(ClaimTypes.Name, "Speaker Name")
        ], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("speaker@example.com", context.UserId);
    }

    [Theory]
    [InlineData("upn")]
    [InlineData(ClaimTypes.Upn)]
    public void UserId_UsesUpnWhenPreferredUsernameIsUnavailable(string claimType)
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [
            new Claim("preferred_username", " "),
            new Claim(claimType, "speaker@example.com")
        ], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("speaker@example.com", context.UserId);
    }

    [Theory]
    [InlineData("preferred_username")]
    [InlineData("upn")]
    [InlineData(ClaimTypes.Upn)]
    public void UserId_NormalizesUsername(string claimType)
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [new Claim(claimType, " Speaker@Example.COM ")], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Equal("speaker@example.com", context.UserId);
    }

    [Fact]
    public void UserId_RemainsTheSameWhenSubjectChanges()
    {
        var context = new WebCurrentUserContext();
        context.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("preferred_username", "speaker@example.com"),
            new Claim("sub", "original-subject")
        ], "test")));
        var originalOwner = context.UserId;

        context.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("preferred_username", "speaker@example.com"),
            new Claim("sub", "new-subject")
        ], "test")));

        Assert.Equal(originalOwner, context.UserId);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData(ClaimTypes.Name)]
    [InlineData(ClaimTypes.Email)]
    public void UserId_RejectsIdentityWithoutLoginUsername(string claimType)
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [new Claim(claimType, "speaker@example.com")], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Throws<InvalidOperationException>(() => context.UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UserId_RejectsBlankUsername(string username)
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [new Claim("preferred_username", username)], "test");

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Throws<InvalidOperationException>(() => context.UserId);
    }

    [Fact]
    public void UserId_RejectsUnauthenticatedIdentity()
    {
        var context = new WebCurrentUserContext();
        var identity = new ClaimsIdentity(
        [new Claim("preferred_username", "speaker@example.com")]);

        context.SetPrincipal(new ClaimsPrincipal(identity));

        Assert.Throws<InvalidOperationException>(() => context.UserId);
    }

    [Fact]
    public void UserId_RejectsUninitializedContext()
    {
        var context = new WebCurrentUserContext();

        Assert.Throws<InvalidOperationException>(() => context.UserId);
    }
}