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
        /// Gets or sets the maximum number of events to retain in the event history.
        /// </summary>
        public int EventHistorySize { get; set; } = 50;

        /// <summary>
        /// Gets or sets a value indicating whether the dashboard is enabled.
        /// </summary>
        public bool EnableDashboard { get; set; } = true;

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

        internal string EndpointPath { get; set; } = InternalConstants.DefaultEndpointPath;
        /// <summary>
        /// Gets the full path to the dashboard endpoint.
        /// </summary>
        public string Path => $"{EndpointPath}/dashboard";
    }
}
