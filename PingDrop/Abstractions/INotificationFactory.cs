using PingDrop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Abstractions
{
    internal interface INotificationFactory
    {
        PDNotification CreateNotification<T>(T notification, string eventType, string? targetKey = null, IReadOnlyList<string>? targetKeys = null);

        /// <summary>Like <see cref="CreateNotification{T}"/> for a payload that is already serialized JSON.</summary>
        PDNotification CreateNotificationFromJson(string payloadJson, string eventType, string? targetKey = null, IReadOnlyList<string>? targetKeys = null);

        /// <summary>The newest event id issued by this factory (0 when none).</summary>
        long LastIssuedId { get; }
    }
}
