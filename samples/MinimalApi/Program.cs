using Microsoft.AspNetCore.Mvc;
using PingDrop.Abstractions;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingDrop(options =>
{
    // The dashboard is only reachable in Development unless you give it an authorization policy.
    options.EnableDashboard = builder.Environment.IsDevelopment();

    // DEMO ONLY: the subscriber key comes from the query string (?userId=alice), so ANY client
    // can pretend to be ANY user. In production derive the key from the authenticated user's
    // claims instead (ctx.User.FindFirst("sub")?.Value) and require authorization on the
    // endpoint. See docs/authentication.md and the Mvc sample.
    options.TargetKeyResolver = ctx => ctx.Request.Query["userId"].ToString() is { Length: > 0 } id ? id : null;
});

var app = builder.Build();

app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();

app.MapPingDrop();       // SSE endpoint at /pingdrop/stream, client at /pingdrop/stream/client.js

// POST /broadcast?text=hello          -> every connected client
app.MapPost("/broadcast", async (IPingDropService pingDrop, string text) =>
{
    await pingDrop.BroadcastAsync(new { Text = text, At = DateTime.UtcNow }, "notification");
    return Results.Ok();
});

// POST /send/alice?text=hello         -> only connections whose key is "alice"
app.MapPost("/send/{userId}", async (IPingDropService pingDrop, string userId, string text) =>
{
    await pingDrop.SendToAsync(userId, new { Text = text, At = DateTime.UtcNow }, "notification");
    return Results.Ok();
});

// POST /send-many?users=alice&users=bob&text=hello  -> several keys in one call
app.MapPost("/send-many", async (IPingDropService pingDrop, [FromQuery] string[] users, string text) =>
{
    await pingDrop.SendToAsync(users, new { Text = text, At = DateTime.UtcNow }, "notification");
    return Results.Ok(new { sentTo = users });
});

app.Run();
