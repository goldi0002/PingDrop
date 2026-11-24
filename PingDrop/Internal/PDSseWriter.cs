using Microsoft.AspNetCore.Http;
using PingDrop.Models;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace PingDrop.Internal
{
    internal static class PDSseWriter
    {
        public static async ValueTask WriteAsync(HttpResponse response, PDNotification notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);

            var writer = response.BodyWriter;

            writer.Write(GetFrame(notification));

            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// The UTF-8 bytes of the notification's SSE frame. Built once per event on first use and shared by every client and by
        /// replay, so a broadcast encodes its frame once instead of once per client. Notifications are immutable, so the cache
        /// never goes stale; if two threads race, both build identical bytes and either result is fine.
        /// </summary>
        internal static byte[] GetFrame(PDNotification notification)
        {
            byte[]? frame = Volatile.Read(ref notification.SseFrame);
            if (frame is null)
            {
                frame = BuildFrame(notification);
                Volatile.Write(ref notification.SseFrame, frame);
            }
            return frame;
        }

        private static byte[] BuildFrame(PDNotification notification)
        {
            string eventType = notification.EventType ?? string.Empty;
            string payload = notification.Payload ?? string.Empty;

            Span<byte> id = stackalloc byte[20];
            notification.Id.TryFormat(id, out int idLength, default, CultureInfo.InvariantCulture);

            ReadOnlySpan<byte> idLabel = "id: "u8, eventLabel = "\nevent: "u8, dataLabel = "\ndata: "u8, end = "\n\n"u8;
            int eventLength = Encoding.UTF8.GetByteCount(eventType);
            int payloadLength = Encoding.UTF8.GetByteCount(payload);

            byte[] frame = GC.AllocateUninitializedArray<byte>(idLabel.Length + idLength + eventLabel.Length + eventLength + dataLabel.Length + payloadLength + end.Length);
            Span<byte> rest = frame;
            Append(ref rest, idLabel);
            id[..idLength].CopyTo(rest);
            rest = rest[idLength..];
            Append(ref rest, eventLabel);
            rest = rest[Encoding.UTF8.GetBytes(eventType, rest)..];
            Append(ref rest, dataLabel);
            rest = rest[Encoding.UTF8.GetBytes(payload, rest)..];
            Append(ref rest, end);
            return frame;
        }

        private static void Append(ref Span<byte> destination, ReadOnlySpan<byte> value)
        {
            value.CopyTo(destination);
            destination = destination[value.Length..];
        }

        /// <summary>Writes the stream preamble (optional <c>retry:</c> hint plus a comment) and flushes so headers go out.</summary>
        public static async ValueTask WriteConnectedAsync(HttpResponse response, TimeSpan retryInterval, CancellationToken cancellationToken = default)
        {
            var writer = response.BodyWriter;
            if (retryInterval > TimeSpan.Zero)
            {
                writer.Write(Encoding.UTF8.GetBytes($"retry: {(long)retryInterval.TotalMilliseconds}\n"));
            }
            writer.Write(": connected\n\n"u8);
            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>Writes the <c>: ping</c> heartbeat comment (ignored by clients) and flushes.</summary>
        public static async ValueTask WritePingAsync(HttpResponse response, CancellationToken cancellationToken = default)
        {
            var writer = response.BodyWriter;
            writer.Write(": ping\n\n"u8);
            await writer.FlushAsync(cancellationToken);
        }
    }
}
