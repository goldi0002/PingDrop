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
        PDNotification CreateNotification<T>(T notification, string eventType, string? targetKey = null);
    }
}
