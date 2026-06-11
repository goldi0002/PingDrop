using PingDrop.Abstractions;
using PingDrop.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PingDrop.Internal
{
    internal sealed class DashboardService(IPDConnectionStore connectionStore, IEventHistoryStore eventHistoryStore) : IDashboardService
    {
        private readonly IPDConnectionStore _connectionStore = connectionStore;
        private readonly IEventHistoryStore _eventHistoryStore = eventHistoryStore;
        public PDDashboardModel.PDAbout GetAbout()
        {
            string startedat = Process.GetCurrentProcess().StartTime.ToString("dd MMM yyyy, hh:mm tt");
            string processId = Environment.ProcessId.ToString();
            return new PDDashboardModel.PDAbout(InternalConstants.Version, RuntimeInformation.FrameworkDescription, Environment.MachineName, startedat, RuntimeInformation.OSDescription, processId);
        }

        public IReadOnlyCollection<PDDashboardModel.ConnectionInfo> GetConnections(string status, string? searchText = "")
        {
            var result = new List<PDDashboardModel.ConnectionInfo>();
            var connections = _connectionStore.GetAll().Where(c =>
                status == "All" || (status == "Active" && c.IdleDuration.TotalMinutes < 5) || (status == "Inactive" && c.IdleDuration.TotalMinutes >= 5)
            ).ToList();
            if (!string.IsNullOrEmpty(searchText))
            {
                connections = [.. connections.Where(c => !string.IsNullOrEmpty(c.SubscriberKey) && c.SubscriberKey.Contains(searchText, StringComparison.OrdinalIgnoreCase))];
            }

            if (connections.Count != 0)
            {
                foreach (var connection in connections)
                {
                    result.Add(new PDDashboardModel.ConnectionInfo
                    {
                        ConnectionId = connection.ConnectionId.ToString(),
                        TargetKey = connection.SubscriberKey,
                        ConnectedAt = connection.ConnectedAtUtc.ToString("yyyy-MM-dd hh:mm:ss"),
                        EventsDelivered = connection.NotificationsSent,
                        Status = connection.IdleDuration.TotalMinutes < 5 ? "Active" : "Inactive",
                        IdleDuration = $"{(int)connection.IdleDuration.TotalMinutes}m {connection.IdleDuration.Seconds}s",
                        SubscribedEvents = connection.HasSubscriptions ? string.Join(", ", connection.SubscribedEventTypes) : "None",
                        LastActivityAt = connection.LastActivityAtUtc.ToString("yyyy-MM-dd hh:mm:ss")
                    });
                }
            }
            return result;
        }

        public IReadOnlyCollection<PDDashboardModel.EventStreamMonitor> GetEventStream()
        {
            var result = new List<PDDashboardModel.EventStreamMonitor>();
            var events = _eventHistoryStore.GetAll().OrderByDescending(e => e.Id).Take(100);
            //foreach (var e in events)
            //{
            //    result.Add(new PDDashboardModel.EventStreamMonitor
            //    {
            //        Timestamp = e.TimestampUtc.ToString("yyyy-MM-dd hh:mm:ss"),
            //        EventName = e.EventType,
            //        ConnectionId = e.,
            //        deliverdCount = e.DeliveredCount,
            //        Status = e.DeliveredCount > 0 ? "Delivered" : "Pending",
            //        Size = $"{e.Payload?.Length ?? 0} chars"
            //    });
            //}
            return result;
        }

        public IReadOnlyCollection<PDMetrics> GetMetrics()
        {
            Process process = Process.GetCurrentProcess();

            TimeSpan uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

            return [
                new("History Events",_eventHistoryStore.Count.ToString("N0"),"ri-archive-line","Currently stored"),
                new("Process Memory",$"{process.WorkingSet64 / (1024 * 1024):N0} MB","ri-hard-drive-2-line","Current working set"),
                new("Managed Memory",$"{GC.GetTotalMemory(false) / (1024 * 1024):N0} MB","ri-cpu-line","Managed heap"),
                new("Server Uptime",$"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m","ri-time-line",$"Since {process.StartTime:MMM dd, hh:mm tt}")
            ];
        }

        public Task<Tuple<PDDashboardModel.PDServerHealth, PDDashboardModel.PDServerHealthUsage>> GetServerHealthAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
