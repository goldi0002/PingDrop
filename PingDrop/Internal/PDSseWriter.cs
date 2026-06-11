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
        private static void WriteUtf8(PipeWriter writer, string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);

            var span = writer.GetSpan(byteCount);

            Encoding.UTF8.GetBytes(value.AsSpan(), span);

            writer.Advance(byteCount);
        }
    }
}
