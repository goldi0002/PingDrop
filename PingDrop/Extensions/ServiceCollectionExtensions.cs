using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PingDrop.Abstractions;
using PingDrop.Internal;

namespace PingDrop.Extensions
{
    /// <summary>
    /// Adds the PingDrop services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>This method registers the following services: <list type="bullet"> <item><description><see
    /// cref="PingDropOptions"/> as a singleton.</description></item> <item><description><see
    /// cref="IPDConnectionStore"/> as a singleton.</description></item> <item><description><see cref="PingDropHub"/> as
    /// a singleton.</description></item> <item><description><see cref="IPingDropService"/> as a singleton, resolved
    /// from <see cref="PingDropHub"/>.</description></item> </list> Use this method to configure and enable PingDrop
    /// functionality in your application.</remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the PingDrop services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <remarks>This method registers the necessary services for PingDrop, including the connection
        /// store, hub, and service interface. The <see cref="PingDropOptions"/> can be customized by providing a
        /// configuration delegate.</remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to which the PingDrop services will be added.</param>
        /// <param name="configure">An optional delegate to configure the <see cref="PingDropOptions"/>. If not provided, default options will
        /// be used.</param>
        /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
        public static IServiceCollection AddPingDrop(this IServiceCollection services, Action<PingDropOptions>? configure = null)
        {
            PingDropOptions? options = new();

            configure?.Invoke(options);

            services.AddSingleton(options);
            if (options.AllowedOrigins is { Length: > 0 } origins)
            {
                if (!string.IsNullOrWhiteSpace(options.CorsPolicy))
                    throw new InvalidOperationException("Set either PingDropOptions.CorsPolicy or PingDropOptions.AllowedOrigins, not both.");
                if (origins.Any(o => string.IsNullOrWhiteSpace(o) || o.Contains('*')))
                    throw new InvalidOperationException("PingDropOptions.AllowedOrigins takes exact origins (for example https://app.example.com); wildcards are not supported. Register your own CORS policy and set CorsPolicy instead.");
                services.AddCors(c => c.AddPolicy(PingDropOptions.BuiltInCorsPolicyName, p => p
                    .WithOrigins(origins)
                    .WithMethods("GET")
                    .WithHeaders("Last-Event-ID")
                    .AllowCredentials()));
            }
            services.AddSingleton<INotificationFactory, NotificationFactory>();
            services.AddSingleton<IPDConnectionStore>(sp => new PDConnectionStore(options.ClientQueueCapacity, options.ClientQueueFullMode));
            services.AddSingleton<IEventHistoryStore, PDEventHistoryStore>(sp => new PDEventHistoryStore(options.EventHistorySize));
            services.AddSingleton<PingDropHub>();
            services.AddSingleton<IPingDropService>(sp => sp.GetRequiredService<PingDropHub>());
            services.AddSingleton<PingDropMetrics>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PingDropMetrics>());
            services.AddSingleton<PDShutdownService>();
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PDShutdownService>());
            services.AddSingleton<IEventPublisher, PDEventPublisher>();
            services.AddSingleton<IDashboardService, DashboardService>();

            return services;
        }
    }
}
