using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Abstractions
{
    /// <summary>
    /// Defines a mechanism for publishing events asynchronously.
    /// </summary>
    /// <remarks>This interface is designed to facilitate the publishing of events or notifications to an
    /// external system or message bus. Implementations may vary in how events are delivered, such as using a message
    /// queue, event stream, or other transport mechanisms.</remarks>
    internal interface IEventPublisher
    {
        /// <summary>
        /// Gets a value indicating whether anything is listening, that is the dashboard is enabled and at least one
        /// dashboard feed connection is open. Check this before building optional (for example live-log) payloads.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Publishes a notification asynchronously to the specified event type.
        /// </summary>
        /// <remarks>The method sends the provided notification to the specified event type. If the
        /// operation is canceled via the <paramref name="cancellationToken"/>,  the returned task will be in a canceled
        /// state.</remarks>
        /// <typeparam name="T">The type of the notification to publish.</typeparam>
        /// <param name="notification">The notification object to be published. Cannot be <see langword="null"/>.</param>
        /// <param name="eventType">The type of the event to which the notification is published. Defaults to "message" if not specified.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
        ValueTask PublishAsync<T>(T notification, string eventType = "message", CancellationToken cancellationToken = default);
    }
}
