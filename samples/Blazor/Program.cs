using PingDrop.Extensions;
using PingDropSamples.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (interactive server components only, no WebAssembly).
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddPingDrop(options =>
{
    options.EnableDashboard = builder.Environment.IsDevelopment();

    // DEMO ONLY: key from the query string. Use authenticated claims in production (docs/authentication.md).
    options.TargetKeyResolver = ctx => ctx.Request.Query["userId"].ToString() is { Length: > 0 } id ? id : null;
});

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapPingDrop();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
