using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Models
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    internal sealed class PDDashboardModel
    {
        public class ConnectionInfo
        {
            public string? ConnectionId { get; set; }
            public string? TargetKey { get; set; }
            public string? ConnectedAt { get; set; }
            public string? LastActivityAt { get; set; }
            public long EventsDelivered { get; set; }
            public string? Status { get; set; }
            public string? IdleDuration { get; set; }
            public string? SubscribedEvents { get; set; }
        }

        public sealed record ConnectionsQuery(string status, string? searchTxt = null);
        public sealed record EventStreamMonitor
        {
            public string? Timestamp { get; set; }
            public string? EventName { get; set; }
            public string? ConnectionId { get; set; }
            public int deliverdCount { get; set; }
            public string? Status { get; set; }
            public string? Size { get; set; }
        }
        public sealed record PDLiveLogs(string level, string msg, string? createdAt = null);

        public sealed record PDNotificationTestingRequest(string targetType, string? subsbriberKey = null, string? eventType = null, string? payloadJson = null);

        public sealed record PDServerHealth(string type, string condition, string icon);
        public sealed record PDServerHealthUsage(string type, string usage, string icon);
        public sealed record PDAbout(string pdVersion, string dotnetVersion, string machineName, string startedAt, string os, string processId);
        public sealed record NotificationRequest(string? TargetType, string? TargetId, string? EventName, object? Payload, string? Timestamp);
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
