using Microsoft.AspNetCore.Http;
using PingDrop.Internal;

namespace PingDrop.Abstractions
{
    /// <summary>
    /// Represents configuration options for the PingDrop service, including endpoint settings, history size, and event
    /// handlers.
    /// </summary>
    /// <remarks>This class provides options to customize the behavior of the PingDrop service, such as the
    /// endpoint path,  the size of the connection history, and event handlers for client connection and disconnection
    /// events.  It also allows specifying a custom resolver for identifying subscribers.</remarks>
    public sealed class PingDropOptions
    {
        /// <summary>
        /// The SSE endpoint path.
        /// </summary>
        public string EndpointPath { get; set; } = "/pingdrop/stream";
        /// <summary>
        /// Gets or sets the name of an authorization policy required to connect to the SSE endpoint.
        /// When <see langword="null"/> the endpoint is anonymous.
        /// </summary>
        /// <remarks>The application must call <c>AddAuthorization</c> (defining the policy) and
        /// <c>UseAuthentication</c>/<c>UseAuthorization</c> before <c>MapPingDrop</c>. Prefer deriving the subscriber key
        /// from authenticated claims in <see cref="TargetKeyResolver"/> rather than from the query string, otherwise any
        /// client can subscribe as any user.</remarks>
        public string? AuthorizationPolicy { get; set; }

        /// <summary>
        /// Gets or sets how often a <c>: ping</c> comment is written to idle SSE connections so that proxies and load
        /// balancers do not close them and dead peers are detected. Defaults to 15 seconds.
        /// </summary>
        /// <remarks>Set to <see cref="TimeSpan.Zero"/> or a negative value to disable heartbeats. Heartbeats are SSE
        /// comments, so clients ignore them, and they are not counted as delivered notifications.</remarks>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets or sets the reconnection delay suggested to clients through the SSE <c>retry:</c> field, sent once when
        /// a stream opens. Defaults to 3 seconds.
        /// </summary>
        /// <remarks>Set to <see cref="TimeSpan.Zero"/> or a negative value to omit the hint, in which case the client
        /// uses its own default.</remarks>
        public TimeSpan ClientRetryInterval { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the maximum number of events to retain in the event history.
        /// </summary>
        public int EventHistorySize { get; set; } = 50;

        internal const int DefaultClientQueueCapacity = 20;

        private int _clientQueueCapacity = DefaultClientQueueCapacity;

        /// <summary>
        /// Gets or sets the maximum number of notifications queued per connected client while they wait to be written
        /// to that client's stream. Defaults to 20.
        /// </summary>
        /// <remarks>What happens when a client's queue is full is controlled by <see cref="ClientQueueFullMode"/>.
        /// Every connection gets its own queue, so memory use grows with capacity times the number of clients.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int ClientQueueCapacity
        {
            get => _clientQueueCapacity;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
                _clientQueueCapacity = value;
            }
        }

        /// <summary>
        /// Gets or sets what happens when a notification is sent to a client whose queue (see
        /// <see cref="ClientQueueCapacity"/>) is full. Defaults to <see cref="PingDropQueueFullMode.DropOldest"/>.
        /// </summary>
        /// <remarks>Every notification lost this way is counted in the dropped-deliveries metric.</remarks>
        public PingDropQueueFullMode ClientQueueFullMode { get; set; } = PingDropQueueFullMode.DropOldest;

        /// <summary>
        /// Gets or sets a value indicating whether the dashboard is enabled.
        /// </summary>
        public bool EnableDashboard { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the bundled JavaScript client is served at
        /// <c>{EndpointPath}/client.js</c>. Defaults to <see langword="true"/>.
        /// </summary>
        public bool ServeJavaScriptClient { get; set; } = true;

        /// <summary>
        /// Gets the configuration options for the Ping Drop Dashboard.
        /// </summary>
        public PingDropDashboardOptions Dashboard { get; set; } = new PingDropDashboardOptions();
        /// <summary>
        /// Gets or sets the callback that is invoked when a client connects.
        /// </summary>
        /// <remarks>The callback receives the client's identifier as a <see cref="string"/> and returns a
        /// <see cref="Task"/>  representing the asynchronous operation. If set to <see langword="null"/>, no action is
        /// performed when a client connects.</remarks>
        public Func<long, Task>? ClientConnected { get; set; }

        /// <summary>
        /// Gets or sets the callback that is invoked when a client disconnects.
        /// </summary>
        /// <remarks>The callback receives the identifier of the disconnected client as a string parameter
        /// and returns a <see cref="Task"/> representing the asynchronous operation.  If no callback is set, no action
        /// is performed when a client disconnects.</remarks>
        public Func<long, Task>? ClientDisconnected { get; set; }

        /// <summary>
        /// Gets or sets the function used to resolve a subscriber key from the current <see cref="HttpContext"/>.
        /// </summary>
        /// <remarks>The function should take an <see cref="HttpContext"/> as input and return a string
        /// representing the subscriber key, or <see langword="null"/> if no key can be resolved. This property is
        /// typically used to identify a unique subscriber in scenarios such as caching or request tracking.</remarks>
        public Func<HttpContext, string?>? TargetKeyResolver { get; set; }
    }
    /// <summary>
    /// Describes what happens when a notification arrives for a client whose queue is full.
    /// </summary>
    public enum PingDropQueueFullMode
    {
        /// <summary>The oldest queued notification is discarded to make room for the new one.</summary>
        DropOldest,

        /// <summary>The new notification is discarded and the queued ones are kept.</summary>
        DropNewest,

        /// <summary>
        /// The client's stream is ended (the new notification is discarded). The client is removed and, being a
        /// well-behaved <c>EventSource</c>, reconnects and starts with an empty queue.
        /// </summary>
        DisconnectClient
    }

    /// <summary>
    /// Dashboard configuration options.
    /// </summary>
    public sealed class PingDropDashboardOptions
    {
        /// <summary>
        /// Dashboard title.
        /// </summary>
        public string Title { get; set; } = "PingDrop Dashboard";
        /// <summary>
        /// Gets or sets the name of an authorization policy required for every dashboard route (page, assets, API and
        /// live feed), including the ability to send notifications.
        /// </summary>
        /// <remarks>When <see langword="null"/>, the dashboard is only mapped in the Development environment unless
        /// <see cref="AllowAnonymous"/> is set; in other environments it is skipped and a warning is logged.</remarks>
        public string? AuthorizationPolicy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the dashboard may be mapped without an
        /// <see cref="AuthorizationPolicy"/> outside the Development environment. Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>The dashboard can send notifications to any client, so enable this only on a trusted network.</remarks>
        public bool AllowAnonymous { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a live-log line is published to the dashboard for every event
        /// dispatched to a client. Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>Connect and disconnect lines are always published while a dashboard feed is open. Per-dispatch
        /// lines are the noisiest, so they are off by default. Nothing is built or serialized when the dashboard is
        /// disabled or no dashboard feed is connected.</remarks>
        public bool LogEachDispatch { get; set; }

        internal string EndpointPath { get; set; } = InternalConstants.DefaultEndpointPath;
        /// <summary>
        /// Gets the full path to the dashboard endpoint.
        /// </summary>
        public string Path => $"{EndpointPath}/dashboard";
    }
}
