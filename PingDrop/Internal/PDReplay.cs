using Microsoft.AspNetCore.Http;
using PingDrop.Models;
using System.Globalization;

namespace PingDrop.Internal
{
    /// <summary>Selection and parsing helpers for Last-Event-ID replay.</summary>
    internal static class PDReplay
    {
        /// <summary>Name of the query parameter used when a client cannot set the Last-Event-ID header.</summary>
        internal const string QueryParam = "lastEventId";

        /// <summary>Reads the last event id from the header (preferred) or the query parameter; null when absent or invalid.</summary>
        internal static long? ParseLastEventId(HttpRequest request)
        {
            return TryParse(request.Headers["Last-Event-ID"].ToString()) ?? TryParse(request.Query[QueryParam].ToString());
        }

        internal static long? TryParse(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long id)
                ? id
                : null;
        }

        /// <summary>The retained events newer than <paramref name="afterId"/> that <paramref name="client"/> would have received live, in id order.</summary>
        internal static IReadOnlyList<PDNotification> Select(IEnumerable<PDNotification> history, PDClientConnection client, long afterId)
        {
            return history
                .Where(n => n.Id > afterId && IsEntitled(n, client))
                .OrderBy(n => n.Id)
                .ToList();
        }

        internal static bool IsEntitled(PDNotification notification, PDClientConnection client)
        {
            if (client.HasSubscriptions && !client.IsSubscribedTo(notification.EventType ?? string.Empty))
            {
                return false;
            }

            IReadOnlyList<string>? keys = notification.TargetKeys;

            if (keys is null)
            {
                // No structured target: broadcast, or a single display key.
                return notification.TargetKey is null || notification.TargetKey == client.SubscriberKey;
            }

            return client.SubscriberKey is not null && keys.Contains(client.SubscriberKey, StringComparer.Ordinal);
        }
    }
}
