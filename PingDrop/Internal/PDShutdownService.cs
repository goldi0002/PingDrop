using Microsoft.Extensions.Hosting;
using PingDrop.Abstractions;

namespace PingDrop.Internal
{
    /// <summary>
    /// Completes every SSE connection (dashboard feed included) when the application starts stopping, so the
    /// handlers' read loops end, their <c>finally</c> blocks run, and the host is not held until the shutdown timeout.
    /// </summary>
    internal sealed class PDShutdownService(IPDConnectionStore connectionStore, IHostApplicationLifetime lifetime) : IHostedService
    {
        private CancellationTokenRegistration _registration;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _registration = lifetime.ApplicationStopping.Register(CompleteAll);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _registration.Dispose();
            CompleteAll();
            return Task.CompletedTask;
        }

        private void CompleteAll()
        {
            foreach (PDClientConnection connection in connectionStore.GetAllIncludingDashboard())
            {
                // DisposeAsync only completes the channel and returns a completed ValueTask.
                _ = connection.DisposeAsync();
            }
        }
    }
}
