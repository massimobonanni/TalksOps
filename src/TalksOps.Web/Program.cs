using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using TalksOps.ApiClient;
using TalksOps.Core.Contracts;
using TalksOps.Web.Components;
using TalksOps.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build());

var useFakeAccount = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Authentication:UseFakeAccount");

if (useFakeAccount)
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
        });
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddScoped<WebCurrentUserContext>();
builder.Services.AddScoped<ICurrentUserContext>(services => services.GetRequiredService<WebCurrentUserContext>());
builder.Services.AddHttpClient<ITalksOpsApiClient, TalksOpsApiClient>(client =>
{
    var apiBaseAddress = builder.Configuration["TalksOpsApi:BaseAddress"] ?? "http://localhost:7071/";
    client.BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute);

    var functionKey = builder.Configuration["TalksOpsApi:FunctionKey"];
    if (!string.IsNullOrWhiteSpace(functionKey))
    {
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapGet("/auth/login", (HttpContext context, IConfiguration configuration) =>
{
    var returnUrl = GetLocalReturnUrl(context.Request.Query["returnUrl"]);
    if (useFakeAccount)
    {
        return Results.Redirect($"/auth/dev-login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl },
        [OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapGet("/auth/logout", () => useFakeAccount
    ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/login" }, [CookieAuthenticationDefaults.AuthenticationScheme])
    : Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/login" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
    .AllowAnonymous();

if (useFakeAccount)
{
    app.MapGet("/auth/dev-login", async (HttpContext context, IConfiguration configuration) =>
    {
        var userId = configuration["Authentication:FakeAccountId"] ?? "local-talksops-user";
        var displayName = configuration["Authentication:FakeAccountName"] ?? "Local speaker";
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Email, $"{userId}@localhost")
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Results.LocalRedirect(GetLocalReturnUrl(context.Request.Query["returnUrl"]));
    }).AllowAnonymous();
}

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string GetLocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
        && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/events";
