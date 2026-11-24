using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PingDrop.Abstractions;
using PingDrop.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddLogging();

builder.Services.AddPingDrop(options =>
{
    options.EnableDashboard = true;
    options.Dashboard.Title = "PingDrop Dashboard - Test Application";
    options.ClientConnected = id =>
    {
        Console.WriteLine($"PingDrop client connected: {id}");

        return Task.CompletedTask;
    };
    options.ClientDisconnected = id =>
    {
        Console.WriteLine($"PingDrop client Disconnected: {id}");
        return Task.CompletedTask;
    };
    options.TargetKeyResolver = context =>
    {
        return context.Request.Query["uId"];
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.MapPingDrop();

// Test endpoint
app.MapPost("/send", async (
    IPingDropService notifier) =>
{
    await notifier.SendToAsync("1",
        new
        {
            Message = "Hello from PingDrop",
            Time = DateTime.UtcNow
        },
        "notification");

    return Results.Ok();
});

// Send notification endpoint for testing dashboard
app.MapPost("/send-notification", async (
    IPingDropService notifier,
    [FromBody] NotificationPayload payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Message))
    {
        return Results.BadRequest(new { error = "Title and Message are required" });
    }

    var notification = new
    {
        Title = payload.Title,
        Message = payload.Message,
        Severity = payload.Severity ?? "info",
        Duration = payload.Duration,
        Timestamp = payload.Timestamp ?? DateTime.UtcNow,
        EventType = payload.EventType ?? "notification"
    };

    try
    {
        if (string.IsNullOrWhiteSpace(payload.TargetUserId))
        {
            // Broadcast to all
            await notifier.BroadcastAsync(notification, payload.EventType ?? "notification");
        }
        else
        {
            // Send to specific user
            await notifier.SendToAsync(payload.TargetUserId, notification, payload.EventType ?? "notification");
        }

        return Results.Ok(new { success = true, message = "Notification sent successfully" });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

// Send to multiple users
app.MapPost("/send-to-multiple", async (
    IPingDropService notifier,
    [FromBody] MultiUserPayload payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Message) || payload.UserIds == null || payload.UserIds.Length == 0)
    {
        return Results.BadRequest(new { error = "Title, Message, and UserIds are required" });
    }

    var notification = new
    {
        Title = payload.Title,
        Message = payload.Message,
        Severity = payload.Severity ?? "info",
        Duration = payload.Duration,
        Timestamp = payload.Timestamp ?? DateTime.UtcNow
    };

    try
    {
        await notifier.SendToAsync(payload.UserIds, notification, payload.EventType ?? "notification");
        return Results.Ok(new { success = true, message = $"Sent to {payload.UserIds.Length} user key(s)" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Load test - send multiple notifications in sequence
app.MapPost("/load-test", async (
    IPingDropService notifier,
    [FromBody] LoadTestPayload payload) =>
{
    try
    {
        int count = payload.NotificationCount ?? 10;
        int delayMs = payload.DelayMs ?? 100;
        string targetUserId = payload.TargetUserId ?? "1";

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < count; i++)
            {
                await notifier.SendToAsync(targetUserId, new
                {
                    Title = $"Load Test Message {i + 1}",
                    Message = $"This is test notification {i + 1} of {count}",
                    Index = i + 1,
                    Total = count,
                    Timestamp = DateTime.UtcNow
                }, "notification");

                if (i < count - 1)
                    await Task.Delay(delayMs);
            }
        });

        return Results.Ok(new { success = true, message = $"Load test started: {count} notifications scheduled" });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

// Stress test - simulate multiple concurrent connections
app.MapPost("/stress-test", async (
    IPingDropService notifier) =>
{
    try
    {
        var tasks = new List<Task>();

        // Simulate 5 concurrent users, each receiving 3 notifications
        for (int user = 1; user <= 5; user++)
        {
            for (int msg = 1; msg <= 3; msg++)
            {
                int userId = user;
                int msgNum = msg;
                tasks.Add(Task.Run(async () =>
                {
                    await notifier.SendToAsync(userId.ToString(), new
                    {
                        Title = $"Stress Test - User {userId}",
                        Message = $"Message {msgNum} from stress test",
                        UserId = userId,
                        MessageNum = msgNum,
                        Timestamp = DateTime.UtcNow
                    }, "stress-test");
                }));
            }
        }

        await Task.WhenAll(tasks);
        return Results.Ok(new { success = true, message = "Stress test completed: 15 notifications sent to 5 users" });
    }
    catch (Exception ex)
    {
        return Results.StatusCode(500);
    }
});

app.Run();

// Notification payload model
public class NotificationPayload
{
    public string Title { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }
    public int Duration { get; set; } = 3000;
    public DateTime? Timestamp { get; set; }
    public string EventType { get; set; }
    public string TargetUserId { get; set; }
}

// Multiple users payload
public class MultiUserPayload
{
    public string Title { get; set; }
    public string Message { get; set; }
    public string Severity { get; set; }
    public int Duration { get; set; } = 3000;
    public DateTime? Timestamp { get; set; }
    public string EventType { get; set; }
    public string[] UserIds { get; set; }
}

// Load test payload
public class LoadTestPayload
{
    public string TargetUserId { get; set; }
    public int? NotificationCount { get; set; }
    public int? DelayMs { get; set; }
}
