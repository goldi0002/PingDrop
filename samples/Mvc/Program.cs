using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Cookie authentication. EventSource sends same-origin cookies automatically.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        // Answer 401 instead of redirecting, so an unauthenticated EventSource gets a clean failure.
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("PingDropClients", p => p.RequireAuthenticatedUser()));

builder.Services.AddPingDrop(options =>
{
    options.EnableDashboard = builder.Environment.IsDevelopment();

    // Only signed-in users may open the stream...
    options.AuthorizationPolicy = "PingDropClients";

    // ...and the subscriber key is derived from the authenticated principal, not from anything
    // the client sends. This is the approach recommended in docs/authentication.md.
    // Controllers send to the same value (User.Identity.Name below).
    options.TargetKeyResolver = ctx => ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapPingDrop();
app.MapDefaultControllerRoute();

// FAKE sign-in for the sample: GET /login?user=alice signs in as "alice" with a cookie.
// A real app would verify credentials (or use an external identity provider).
app.MapGet("/login", async (HttpContext http, string user) =>
{
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
});

app.MapGet("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.Run();
