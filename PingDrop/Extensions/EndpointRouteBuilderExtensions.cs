using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PingDrop.Abstractions;
using PingDrop.Internal;
using PingDrop.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using static PingDrop.Models.PDDashboardModel;

namespace PingDrop.Extensions
{
    /// <summary>
    /// Adds a PingDrop endpoint to the specified <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    /// <remarks>This method configures an endpoint for handling Server-Sent Events (SSE) using the PingDrop
    /// framework. The endpoint path is determined by the <see cref="PingDropOptions.EndpointPath"/> property, or
    /// defaults to a predefined path if not specified. If the <see cref="PingDropOptions.EnableDashboard"/> property is
    /// enabled, a dashboard endpoint may also be configured.</remarks>
    public static class EndpointRouteBuilderExtensions
    {
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

#pragma warning disable CS1574 // XML comment has cref attribute that could not be resolved
        /// <summary>
        /// Configures the specified <see cref="IEndpointRouteBuilder"/> to include endpoints for handling server-sent
        /// events (SSE) and optionally a dashboard for monitoring.
        /// </summary>
        /// <remarks>This method maps the necessary endpoints for handling server-sent events (SSE) using
        /// the configuration provided by <see cref="PingDropOptions"/>. If the dashboard feature is enabled, additional
        /// endpoints for the dashboard and its SSE connections are also mapped. <para> The default endpoint path for
        /// SSE connections is determined by <see cref="PingDropOptions.EndpointPath"/>. If not specified, a default
        /// path is used. </para> <para> When the dashboard is enabled, its endpoint path is determined by <see
        /// cref="PingDropOptions.Dashboard.Path"/>. If not specified, a default path is used. </para></remarks>
        /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to configure.</param>
        /// <returns>The configured <see cref="IEndpointRouteBuilder"/> instance.</returns>
        public static IEndpointRouteBuilder MapPingDrop(this IEndpointRouteBuilder endpoints)
#pragma warning restore CS1574 // XML comment has cref attribute that could not be resolved
        {
            IServiceProvider? sp = endpoints.ServiceProvider;
            PingDropOptions? options = sp.GetRequiredService<PingDropOptions>();
            RouteHandlerBuilder stream = endpoints.MapGet(options.EndpointPath ?? InternalConstants.DefaultEndpointPath, HandleSseConnection);

            if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
            {
                stream.RequireAuthorization(options.AuthorizationPolicy);
            }

            string? corsPolicy = options.EffectiveCorsPolicy;
            if (corsPolicy is not null)
            {
                stream.RequireCors(corsPolicy);
            }

            if (options.ServeJavaScriptClient)
            {
                IEndpointConventionBuilder client = MapJavaScriptClient(endpoints, options.EndpointPath ?? InternalConstants.DefaultEndpointPath);
                if (corsPolicy is not null) client.RequireCors(corsPolicy);
            }

            if (options.EnableDashboard)
            {
                bool secured = !string.IsNullOrWhiteSpace(options.Dashboard.AuthorizationPolicy);
                IHostEnvironment? environment = sp.GetService<IHostEnvironment>();

                if (!secured && !options.Dashboard.AllowAnonymous && environment is not null && !environment.IsDevelopment())
                {
                    sp.GetService<ILoggerFactory>()?.CreateLogger("PingDrop").LogWarning(
                        "The PingDrop dashboard is enabled but was not mapped: it has no authorization policy and the environment is '{Environment}'. " +
                        "Set PingDropOptions.Dashboard.AuthorizationPolicy (recommended), set Dashboard.AllowAnonymous = true, or set EnableDashboard = false.",
                        environment.EnvironmentName);

                    return endpoints;
                }

                options.Dashboard.EndpointPath = options.EndpointPath ?? InternalConstants.DefaultEndpointPath;

                // All dashboard routes go through one group so the policy is applied to every one of them.
                RouteGroupBuilder dashboard = endpoints.MapGroup(string.Empty);
                dashboard.MapGet(options.Dashboard.Path + "/sse", HandleDashboardSseConnection);
                dashboard.MapDashboard(options);
                // The dashboard can send notifications, so it stays same-origin even when the app has a default CORS policy.
                // (CORS metadata requires UseCors, so it is only added for apps that already opted in to CORS for PingDrop.)
                if (options.EffectiveCorsPolicy is not null)
                    dashboard.WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute());

                if (secured)
                {
                    dashboard.RequireAuthorization(options.Dashboard.AuthorizationPolicy!);
                }
            }

            return endpoints;
        }
        private static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints, PingDropOptions options)
        {
            endpoints.AddDashboardFiles(options);
            endpoints.AddDashboardEndpoints(options);
            return endpoints;
        }

        private static void AddSseHeaders(HttpResponse response)
        {
            response.ContentType = "text/event-stream";
            response.Headers.Append("Cache-Control", "no-cache, no-transform");
            response.Headers.Append("X-Accel-Buffering", "no");
            response.Headers.Append("Connection", "keep-alive");
        }
        private static void AddJsonHeaders(HttpResponse response)
        {
            response.ContentType = "application/json";

            response.Headers.Append("Cache-Control", "no-store");
            response.Headers.Append("X-Content-Type-Options", "nosniff");
        }

        private const int DefaultRetryAfterSeconds = 5;

        private static void RejectConnection(HttpResponse response, PDConnectionRejection rejection, PingDropOptions options)
        {
            // Per key: 429 (this caller has too many). Total / dashboard feeds: 503 (the server is full).
            response.StatusCode = rejection == PDConnectionRejection.PerKey ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable;

            double retry = options.ClientRetryInterval.TotalSeconds;
            int seconds = retry > 0 ? (int)Math.Ceiling(retry) : DefaultRetryAfterSeconds;
            response.Headers.RetryAfter = Math.Max(1, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static async Task HandleSseConnection(HttpContext context, PingDropHub hub, PingDropOptions options, IEventPublisher eventPublisher, ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime)
        {
            // Once the host is stopping, new streams are refused so they cannot hang past shutdown (clients retry the next instance).
            if (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            ILogger logger = loggerFactory.CreateLogger(PDLog.Category);

            HashSet<string>? events = ParseEvents(context.Request.Query[InternalConstants.DefaultQueryEventTypeParam]);
            string? subscriberKey = options.TargetKeyResolver?.Invoke(context);
            long? lastEventId = options.EnableReplay ? PDReplay.ParseLastEventId(context.Request) : null;

            // The client is registered (live events start queueing) before the history is snapshotted below. The limit check and
            // the registration are one atomic step; a refused connection gets a bare status and Retry-After, never SSE headers.
            PDConnectionLimits limits = new(options.MaxConnectionsPerKey, options.MaxTotalConnections, null);
            PDConnectionRejection rejection = hub.TryAddClient(events, subscriberKey, limits, out PDClientConnection? added);
            if (rejection != PDConnectionRejection.None)
            {
                RejectConnection(context.Response, rejection, options);
                hub.WarnConnectionRejected(rejection, limits);
                return;
            }

            AddSseHeaders(context.Response);
            await using PDClientConnection client = added!;
            PDLog.ClientConnected(logger, client.ConnectionId, subscriberKey);
            // Shutdown may have swept the store just before this client was added; complete it so the loop below ends.
            if (lifetime.ApplicationStopping.IsCancellationRequested) await client.DisposeAsync();

            if (options.ClientConnected is not null)
            {
                try
                {
                    await options.ClientConnected(client.ConnectionId);
                }
                catch (Exception ex)
                {
                    PDLog.ClientConnectedCallbackFailed(logger, client.ConnectionId, ex);
                }
            }

            if (eventPublisher.IsActive)
                await eventPublisher.PublishAsync(new PDLiveLogs("info", $"Client connected: {client.ConnectionId}", DateTime.UtcNow.ToString("O")), "log", context.RequestAborted);

            try
            {
                // Register first, then flush: once the client sees headers it is guaranteed to receive broadcasts.
                await PDSseWriter.WriteConnectedAsync(context.Response, options.ClientRetryInterval, context.RequestAborted);

                // Replay missed events. They are written straight to the response (not through the bounded queue), so a gap
                // larger than the queue cannot lose any. Live events published meanwhile wait in the queue.
                // Anything in the queue with an id up to dedupeThrough was already sent (or already seen by the client).
                long dedupeThrough = 0;
                if (lastEventId is long seen)
                {
                    dedupeThrough = seen;
                    long newestId = hub.LastIssuedEventId;
                    if (seen > newestId)
                    {
                        // Ids restart with the process; nothing can be said about what this client missed.
                        dedupeThrough = 0;
                        PDLog.ReplaySkippedUnknownId(logger, client.ConnectionId, seen, newestId);
                    }
                    else
                    {
                        // Snapshot, write, and snapshot again until nothing newer is retained. Writing a long replay takes
                        // time during which live events keep queueing; a small queue may evict some of them, but every
                        // event is in the history first, so the next round re-sends whatever is still missing. When a
                        // round finds nothing, every later event is delivered after the client registered and is in the queue.
                        int replayed = 0;
                        while (true)
                        {
                            IReadOnlyList<PDNotification> missed = hub.GetReplayEvents(client, dedupeThrough);
                            if (missed.Count == 0) break;

                            foreach (PDNotification missedEvent in missed)
                            {
                                try
                                {
                                    await PDSseWriter.WriteAsync(context.Response, missedEvent, context.RequestAborted);
                                    hub.RecordReplayedDelivery();
                                    client.MarkDelivered();
                                    dedupeThrough = missedEvent.Id;
                                    replayed++;
                                }
                                catch (OperationCanceledException)
                                {
                                    hub.IncrementDroppedDispatches();
                                    throw;
                                }
                            }
                        }
                        if (replayed > 0)
                            PDLog.EventsReplayed(logger, replayed, client.ConnectionId, seen);
                    }
                }

                var channelReader = client.NotificationChannel.Reader;
                using var heartbeat = new PDHeartbeatWaiter(channelReader, context.Response, options.HeartbeatInterval, client, context.RequestAborted);
                while (await heartbeat.WaitToReadAsync())
                {
                    while (channelReader.TryRead(out var evt))
                    {
                        if (evt.Id <= dedupeThrough)
                        {
                            // Already replayed (or already seen by the client): published while the replay was being built.
                            // Its queued copy counted as an attempted delivery, so close it out as dispatched.
                            hub.IncrementSuccessfulDispatches();
                            continue;
                        }
                        try
                        {
                            await PDSseWriter.WriteAsync(context.Response, evt, context.RequestAborted);
                            if (options.Dashboard.LogEachDispatch && eventPublisher.IsActive)
                                await eventPublisher.PublishAsync(new PDLiveLogs("info", $"Event dispatched to client {client.ConnectionId}: {evt.EventType}", DateTime.UtcNow.ToString("O")), "log", context.RequestAborted);
                            hub.IncrementSuccessfulDispatches();
                            client.MarkDelivered();
                        }
                        catch (OperationCanceledException)
                        {
                            hub.IncrementDroppedDispatches();
                            throw;
                        }

                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ChannelClosedException)
            {

            }
            finally
            {
                hub.RemoveClient(client.ConnectionId);
                PDLog.ClientDisconnected(logger, client.ConnectionId);
                try
                {
                    if (eventPublisher.IsActive)
                        await eventPublisher.PublishAsync(new PDLiveLogs("info", $"Client disconnected: {client.ConnectionId}", DateTime.UtcNow.ToString("O")), "log", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    PDLog.LiveLogPublishFailed(logger, ex);
                }

                if (options.ClientDisconnected is not null)
                {
                    try
                    {
                        await options.ClientDisconnected(client.ConnectionId);
                    }
                    catch (Exception ex)
                    {
                        PDLog.ClientDisconnectedCallbackFailed(logger, client.ConnectionId, ex);
                    }
                }
            }
        }
        private static async Task HandleDashboardSseConnection(HttpContext context, PingDropHub hub, PingDropOptions options, IHostApplicationLifetime lifetime)
        {
            if (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            HashSet<string>? events = ParseEvents(context.Request.Query[InternalConstants.DefaultQueryEventTypeParam]);
            PDConnectionLimits limits = new(null, null, options.Dashboard.MaxFeedConnections);
            PDConnectionRejection rejection = hub.TryAddClient(events, InternalConstants.DashboardSubscriberKey, limits, out PDClientConnection? added);
            if (rejection != PDConnectionRejection.None)
            {
                RejectConnection(context.Response, rejection, options);
                hub.WarnConnectionRejected(rejection, limits);
                return;
            }

            AddSseHeaders(context.Response);
            await using PDClientConnection dashboardClient = added!;
            if (lifetime.ApplicationStopping.IsCancellationRequested) await dashboardClient.DisposeAsync();
            try
            {
                // Register first, then flush: once the client sees headers it is guaranteed to receive broadcasts.
                await PDSseWriter.WriteConnectedAsync(context.Response, options.ClientRetryInterval, context.RequestAborted);
                var channelReader = dashboardClient.NotificationChannel.Reader;
                using var heartbeat = new PDHeartbeatWaiter(channelReader, context.Response, options.HeartbeatInterval, dashboardClient, context.RequestAborted);
                while (await heartbeat.WaitToReadAsync())
                {
                    while (channelReader.TryRead(out var log))
                    {
                        await PDSseWriter.WriteAsync(context.Response, log, context.RequestAborted);

                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ChannelClosedException)
            {

            }
            finally
            {
                hub.RemoveClient(dashboardClient.ConnectionId);
            }
        }
        private static HashSet<string>? ParseEvents(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static IEndpointRouteBuilder AddDashboardFiles(this IEndpointRouteBuilder endpoints, PingDropOptions options)
        {
            endpoints.MapGet(options.Dashboard.Path + "/app.css", async context =>
            {
                string css = await PDEmbeddedFileProvider.ReadAsync("app.css");
                context.Response.ContentType = "text/css; charset=utf-8";
                await context.Response.WriteAsync(css);
            });
            endpoints.MapGet(options.Dashboard.Path + "/app.js", async context =>
            {
                string? js = await PDEmbeddedFileProvider.ReadAsync("app.js");
                context.Response.ContentType = "text/javascript; charset=utf-8";
                await context.Response.WriteAsync(js);
            });
            endpoints.MapGet(options.Dashboard.Path, async context =>
            {
                string? dashboard = await PDEmbeddedFileProvider.ReadAsync("Index.html");
                context.Response.ContentType = "text/html; charset=utf-8";
                // Absolute URLs in the page must include the PathBase (UsePathBase / reverse-proxy prefix) the request came through.
                dashboard = dashboard.Replace("{{DASHBOARD_PATH}}", SafeUrlPath(context.Request.PathBase.ToUriComponent() + options.Dashboard.Path));
                dashboard = dashboard.Replace("{{DASHBOARD_TITLE}}", !string.IsNullOrEmpty(options.Dashboard.Title) ? options.Dashboard.Title : "PingDrop Dashboard");
                await context.Response.WriteAsync(dashboard);
            });
            return endpoints;
        }
        private static string SafeUrlPath(string path) =>
            path.Replace("'", "%27").Replace("\"", "%22").Replace("<", "%3C").Replace(">", "%3E").Replace("\\", "%5C").Replace("&", "%26");

        private static IReadOnlyCollection<PDMetrics> GetCombinedMetrics(PingDropHub hub, IDashboardService dashboardService)
        {
            var hubMetrics = hub.GetMetrics();

            var metrics = dashboardService.GetMetrics().ToList();

            metrics.Insert(0, new PDMetrics("Connected Clients", hubMetrics.ConnectedClients.ToString("N0"),
                "ri-user-line", "Active connections"));

            metrics.Insert(1, new PDMetrics("Events Published", hub.TotalEventsPublished.ToString("N0"),
                "ri-send-plane-2-line", "Since restart"));

            metrics.Insert(2, new PDMetrics("Queued Notifications", hubMetrics.SuccessfulDeliveries.ToString("N0"), "ri-send-plane-2-line", "Since restart"));

            metrics.Insert(3, new PDMetrics("Attempted Deliveries", hubMetrics.AttemptedDeliveries.ToString("N0"), "ri-refresh-line", "Since restart"));

            metrics.Insert(4, new PDMetrics("Successful Deliveries",
                hub.SuccessfulDispatchedDeliveries.ToString("N0"), "ri-send-plane-2-line", "Since restart"));

            metrics.Insert(5, new PDMetrics("Dropped Deliveries", hub.DroppedDeliveries.ToString("N0"),
                "ri-close-circle-line", "Since restart"));

            metrics.Insert(6, new PDMetrics("Delivery Success Rate", $"{(hubMetrics.AttemptedDeliveries > 0
                    ? (hub.SuccessfulDispatchedDeliveries * 100.0 / hubMetrics.AttemptedDeliveries)
                    : 0):F2}%", "ri-pulse-line", "Successful vs attempted"));

            return metrics;
        }
        private sealed record EventsQuery(int? limit);

        private sealed record RecentEventInfo(long Id, string? EventType, string? Target, DateTime TimestampUtc, int SizeBytes, string? Payload);

        private static string? Truncate(string? value, int max) =>
            value is null || value.Length <= max ? value : value[..max] + "…";

        private static IEndpointRouteBuilder AddDashboardEndpoints(this IEndpointRouteBuilder endpoints, PingDropOptions options)
        {
            IDashboardService? _dashboardService = endpoints.ServiceProvider.GetRequiredService<IDashboardService>();
            IPDConnectionStore? _connectionStore = endpoints.ServiceProvider.GetRequiredService<IPDConnectionStore>();
            PingDropHub hub = endpoints.ServiceProvider.GetRequiredService<PingDropHub>();
            IEventPublisher _pub = endpoints.ServiceProvider.GetRequiredService<IEventPublisher>();
            IEventHistoryStore _history = endpoints.ServiceProvider.GetRequiredService<IEventHistoryStore>();
            endpoints.MapGet(options.Dashboard.Path + "/metrics", async context =>
            {
                AddJsonHeaders(context.Response);
                var metrics = GetCombinedMetrics(hub, _dashboardService);

                await context.Response.WriteAsJsonAsync(ApiResponse<IReadOnlyCollection<PDMetrics>>.Success(metrics), _jsonOptions, context.RequestAborted);
            });

            endpoints.MapGet(options.Dashboard.Path + "/about", async context =>
            {
                AddJsonHeaders(context.Response);
                await context.Response.WriteAsJsonAsync(ApiResponse<PDAbout>.Success(_dashboardService.GetAbout()), _jsonOptions, context.RequestAborted);
            });

            endpoints.MapGet(options.Dashboard.Path + "/connections", async context =>
            {
                string? filterQ = context.Request.Query["q"];
                string statusFilter = "All";
                string searchText = string.Empty;

                if (!string.IsNullOrEmpty(filterQ))
                {
                    try
                    {
                        var obj = JsonSerializer.Deserialize<ConnectionsQuery>(filterQ);
                        if (obj is not null)
                        {
                            statusFilter = string.IsNullOrWhiteSpace(obj.status) ? "All" : obj.status;
                            searchText = obj.searchTxt ?? string.Empty;
                        }
                    }
                    catch (JsonException)
                    {
                        statusFilter = "All";
                        searchText = string.Empty;
                    }
                }
                if (_dashboardService is null)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(ApiResponse<IReadOnlyCollection<PDDashboardModel.ConnectionInfo>>.Failure("Dashboard service unavailable"), _jsonOptions, context.RequestAborted);
                    return;
                }

                try
                {
                    var connections = _dashboardService.GetConnections(statusFilter, searchText);
                    await context.Response.WriteAsJsonAsync(ApiResponse<IReadOnlyCollection<PDDashboardModel.ConnectionInfo>>.Success(connections), _jsonOptions, context.RequestAborted);
                }
                catch (Exception ex)
                {
                    PDLog.DashboardConnectionsFailed(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(PDLog.Category), ex);
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(ApiResponse<IReadOnlyCollection<PDDashboardModel.ConnectionInfo>>.Failure("Failed to retrieve connections"), _jsonOptions, context.RequestAborted);
                }
            });

            endpoints.MapGet(options.Dashboard.Path + "/events", async context =>
            {
                int limit = 50;
                string? q = context.Request.Query["q"];
                if (!string.IsNullOrEmpty(q))
                {
                    try { limit = JsonSerializer.Deserialize<EventsQuery>(q)?.limit ?? limit; }
                    catch (JsonException) { }
                }
                limit = Math.Clamp(limit, 1, 500);

                var events = _history.GetRecentEvents(limit)
                    .OrderByDescending(e => e.Id)
                    .Select(e => new RecentEventInfo(
                        e.Id,
                        e.EventType,
                        e.TargetKey,
                        e.TimestampUtc,
                        e.Payload is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(e.Payload),
                        options.Dashboard.ShowEventPayloads ? Truncate(e.Payload, 500) : null))
                    .ToList();

                AddJsonHeaders(context.Response);
                await context.Response.WriteAsJsonAsync(ApiResponse<IReadOnlyCollection<RecentEventInfo>>.Success(events), _jsonOptions, context.RequestAborted);
            });

            PDSendRateLimiter? sendLimiter = options.Dashboard.MaxSendsPerMinute is int maxSends
                ? new PDSendRateLimiter(maxSends, endpoints.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System)
                : null;

            endpoints.MapPost(options.Dashboard.Path + "/send-notification", async context =>
            {
                if (sendLimiter is not null && !sendLimiter.TryAcquire(out int retryAfterSeconds))
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure($"Too many notifications sent from the dashboard (limit {options.Dashboard.MaxSendsPerMinute} per minute). Retry in {retryAfterSeconds} second(s)."), _jsonOptions, context.RequestAborted);
                    return;
                }

                NotificationRequest? request = await context.Request.ReadFromJsonAsync<NotificationRequest>(context.RequestAborted);

                if (request is null)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("Invalid request payload"), _jsonOptions,
                        context.RequestAborted);
                    return;
                }

                bool IsValidRequest(NotificationRequest req)
                {
                    if (string.IsNullOrWhiteSpace(req.TargetType) || string.IsNullOrWhiteSpace(req.EventName) || req.Payload is null)
                    {
                        return false;
                    }

                    return req.TargetType.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                           req.TargetType.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
                           req.TargetType.Equals("broadcast", StringComparison.OrdinalIgnoreCase);
                }

                if (!IsValidRequest(request))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("Invalid notification request"), _jsonOptions,
                        context.RequestAborted);
                    return;
                }

                bool isDashboardClient = (request.TargetType?.Equals("key", StringComparison.OrdinalIgnoreCase) ?? false) &&
                    request.TargetId == InternalConstants.DashboardSubscriberKey;

                if (isDashboardClient)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("Cannot send notifications to dashboard client"), _jsonOptions, context.RequestAborted);
                    return;
                }

                bool notificationSent = false;
                try
                {
                    if (string.IsNullOrEmpty(request.TargetType))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("TargetType is required"), _jsonOptions, context.RequestAborted);
                        return;
                    }
                    if (request.TargetType.Equals("key", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(request.TargetId))
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("TargetId is required for key notifications"), _jsonOptions, context.RequestAborted);
                            return;
                        }

                        await hub.SendToAsync(request.TargetId, request.Payload, request.EventName!, context.RequestAborted);

                        notificationSent = true;
                    }
                    else if (request.TargetType.Equals("connection", StringComparison.OrdinalIgnoreCase))
                    {
                        var connection = _connectionStore.GetAll().FirstOrDefault(c => c.ConnectionId.ToString() == request.TargetId);

                        if (connection is not null && !string.IsNullOrWhiteSpace(connection.SubscriberKey))
                        {
                            await hub.SendToAsync(connection.SubscriberKey, request.Payload, request.EventName!, context.RequestAborted);

                            notificationSent = true;
                        }
                    }
                    else if (request.TargetType.Equals("broadcast", StringComparison.OrdinalIgnoreCase))
                    {
                        await hub.BroadcastAsync(request.Payload, request.EventName!, context.RequestAborted);

                        notificationSent = true;
                    }

                }
                catch (PingDropPayloadTooLargeException ex)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure(ex.Message), _jsonOptions, context.RequestAborted);
                    return;
                }

                if (!notificationSent)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure("Target not found"), _jsonOptions,
                        context.RequestAborted);
                    return;
                }
                if (_pub.IsActive)
                    await _pub.PublishAsync(JsonSerializer.Serialize(ApiResponse<IReadOnlyCollection<PDMetrics>>.Success(GetCombinedMetrics(hub, _dashboardService)), _jsonOptions), "metrics", CancellationToken.None);
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Success(new { }, "Notification sent successfully"), _jsonOptions, context.RequestAborted);
            });

            return endpoints;
        }

        private static IEndpointConventionBuilder MapJavaScriptClient(IEndpointRouteBuilder endpoints, string streamPath)
        {
            byte[]? script = null;
            return endpoints.MapGet(streamPath.TrimEnd('/') + "/client.js", async context =>
            {
                if (script is null)
                {
                    using Stream stream = typeof(EndpointRouteBuilderExtensions).Assembly
                        .GetManifestResourceStream("PingDrop.Client.pingdrop.js")!;
                    using MemoryStream ms = new();
                    await stream.CopyToAsync(ms);
                    script = ms.ToArray();
                }

                context.Response.ContentType = "text/javascript; charset=utf-8";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                await context.Response.Body.WriteAsync(script, context.RequestAborted);
            });
        }
    }
}
