namespace PingDrop.Abstractions
{
    /// <summary>
    /// Defines a service for sending notifications to connected clients via broadcast or targeted delivery.
    /// </summary>
    /// <remarks>This interface provides methods to send notifications to all connected clients or to a
    /// specific client identified by a target key. Notifications are sent asynchronously and support cancellation
    /// through a <see cref="CancellationToken"/>.</remarks>
    public interface IPingDropService
    {
        /// <summary>
        /// Sends a notification to all connected clients asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of the notification to broadcast.</typeparam>
        /// <param name="notification">The notification object to send to all clients.</param>
        /// <param name="eventType">The type of event associated with the notification. Defaults to <see langword="message"/>.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
        ValueTask BroadcastAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a notification asynchronously to the specified target.
        /// </summary>
        /// <typeparam name="T">The type of the notification to send.</typeparam>
        /// <param name="targetKey">The key identifying the target recipient of the notification. Cannot be <see langword="null"/> or empty.</param>
        /// <param name="notification">The notification object to send. Cannot be <see langword="null"/>.</param>
        /// <param name="eventType">The type of event associated with the notification. Defaults to "message" if not specified.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
        ValueTask SendToAsync<T>(string targetKey, T notification, string eventType = "message", CancellationToken cancellationToken = default);

    }
}
