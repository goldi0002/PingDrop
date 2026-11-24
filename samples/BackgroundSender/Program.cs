using PingDrop.Extensions;
using PingDropSamples.BackgroundSender;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPingDrop(options =>
{
    options.EnableDashboard = builder.Environment.IsDevelopment();

    // DEMO ONLY: key from the query string. Use authenticated claims in production (docs/authentication.md).
    options.TargetKeyResolver = ctx => ctx.Request.Query["userId"].ToString() is { Length: > 0 } id ? id : null;
});

// IPingDropService is a singleton, so it can be injected into a hosted service.
builder.Services.AddHostedService<TickerService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPingDrop();

app.Run();
