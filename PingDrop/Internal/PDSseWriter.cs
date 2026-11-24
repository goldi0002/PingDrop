using Microsoft.AspNetCore.Http;
using PingDrop.Models;
using System.IO.Pipelines;
using System.Text;

namespace PingDrop.Internal
{
    internal static class PDSseWriter
    {
        public static async ValueTask WriteAsync(HttpResponse response, PDNotification notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);

            var writer = response.BodyWriter;

            WriteUtf8(writer, $"id: {notification.Id}\n");
            WriteUtf8(writer, $"event: {notification.EventType}\n");
            WriteUtf8(writer, $"data: {notification.Payload}\n\n");

            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>Writes the stream preamble (optional <c>retry:</c> hint plus a comment) and flushes so headers go out.</summary>
        public static async ValueTask WriteConnectedAsync(HttpResponse response, TimeSpan retryInterval, CancellationToken cancellationToken = default)
        {
            var writer = response.BodyWriter;
            if (retryInterval > TimeSpan.Zero)
            {
                WriteUtf8(writer, $"retry: {(long)retryInterval.TotalMilliseconds}\n");
            }
            WriteUtf8(writer, ": connected\n\n");
            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>Writes an SSE comment line (ignored by clients) and flushes.</summary>
        public static async ValueTask WriteCommentAsync(HttpResponse response, string comment, CancellationToken cancellationToken = default)
        {
            var writer = response.BodyWriter;
            WriteUtf8(writer, $": {comment}\n\n");
            await writer.FlushAsync(cancellationToken);
        }

        private static void WriteUtf8(PipeWriter writer, string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);

            var span = writer.GetSpan(byteCount);

            Encoding.UTF8.GetBytes(value.AsSpan(), span);

            writer.Advance(byteCount);
        }
    }
}
