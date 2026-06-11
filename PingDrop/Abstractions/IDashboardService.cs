using PingDrop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Abstractions
{
    internal interface IDashboardService
    {
        IReadOnlyCollection<PDMetrics> GetMetrics();

        IReadOnlyCollection<PDDashboardModel.ConnectionInfo> GetConnections(string status, string? searchText = "");

        PDDashboardModel.PDAbout GetAbout();

        IReadOnlyCollection<PDDashboardModel.EventStreamMonitor> GetEventStream();
        Task<Tuple<PDDashboardModel.PDServerHealth, PDDashboardModel.PDServerHealthUsage>> GetServerHealthAsync(CancellationToken cancellationToken = default);
    }
}
