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
            string eventType = NotificationFactory.NormalizeEventType(notification.EventType ?? InternalConstants.DefaultEventType);

            WriteUtf8(writer, $"id: {notification.Id}\n");
            WriteUtf8(writer, $"event: {eventType}\n");
            WriteDataField(writer, notification.Payload);
            WriteUtf8(writer, "\n");

            await writer.FlushAsync(cancellationToken);
        }

        private static void WriteDataField(PipeWriter writer, string? payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                WriteUtf8(writer, "data:\n");
                return;
            }

            ReadOnlySpan<char> remaining = payload.AsSpan();

            while (true)
            {
                int lineBreakIndex = remaining.IndexOfAny('\r', '\n');

                if (lineBreakIndex < 0)
                {
                    WriteUtf8(writer, "data: ");
                    WriteUtf8(writer, remaining);
                    WriteUtf8(writer, "\n");
                    return;
                }

                WriteUtf8(writer, "data: ");
                WriteUtf8(writer, remaining[..lineBreakIndex]);
                WriteUtf8(writer, "\n");

                if (remaining[lineBreakIndex] == '\r' && lineBreakIndex + 1 < remaining.Length && remaining[lineBreakIndex + 1] == '\n')
                {
                    remaining = remaining[(lineBreakIndex + 2)..];
                }
                else
                {
                    remaining = remaining[(lineBreakIndex + 1)..];
                }
            }
        }

        private static void WriteUtf8(PipeWriter writer, string value)
        {
            WriteUtf8(writer, value.AsSpan());
        }

        private static void WriteUtf8(PipeWriter writer, ReadOnlySpan<char> value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);

            var span = writer.GetSpan(byteCount);

            Encoding.UTF8.GetBytes(value, span);

            writer.Advance(byteCount);
        }
    }
}
