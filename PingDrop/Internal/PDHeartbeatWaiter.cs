using Microsoft.AspNetCore.Http;
using PingDrop.Models;
using System.Threading.Channels;

namespace PingDrop.Internal
{
    /// <summary>
    /// Waits for the next notification on a connection, writing a <c>: ping</c> comment every <c>interval</c> while idle.
    /// One instance per connection: it keeps a single linked <see cref="CancellationTokenSource"/> and re-arms it for each wait
    /// instead of creating a new source and timer per wake-up.
    /// </summary>
    internal sealed class PDHeartbeatWaiter(ChannelReader<PDNotification> reader, HttpResponse response, TimeSpan interval,
        PDClientConnection client, CancellationToken cancellationToken) : IDisposable
    {
        private CancellationTokenSource? _timeout;

        /// <summary>
        /// Returns false when the channel is completed. Heartbeats refresh connection activity but are not counted as deliveries.
        /// </summary>
        public async ValueTask<bool> WaitToReadAsync()
        {
            if (interval <= TimeSpan.Zero)
            {
                return await reader.WaitToReadAsync(cancellationToken);
            }

            while (true)
            {
                CancellationTokenSource timeout = _timeout ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(interval);
                try
                {
                    bool result = await reader.WaitToReadAsync(timeout.Token);
                    DisarmOrDiscard();
                    return result;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The interval elapsed; a canceled source cannot be reused.
                    DisarmOrDiscard();
                    await PDSseWriter.WritePingAsync(response, cancellationToken);
                    client.MarkActivity();
                }
            }
        }

        private void DisarmOrDiscard()
        {
            if (_timeout is { } timeout && !timeout.TryReset())
            {
                timeout.Dispose();
                _timeout = null;
            }
        }

        public void Dispose() => _timeout?.Dispose();
    }
}
