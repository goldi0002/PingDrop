using PingDrop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Abstractions
{
    public interface IEventHistoryStore
    {
        int Count { get; }

        void AddEvent(PDNotification notification);

        IReadOnlyCollection<PDNotification> GetAll();

        IReadOnlyCollection<PDNotification> GetRecentEvents(int count);
    }
}
