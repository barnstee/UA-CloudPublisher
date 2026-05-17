
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using System.Collections.Concurrent;

    public interface IMessageSource
    {
        ConcurrentDictionary<string, bool> SkipFirst { get; set; }

        void EventNotificationHandlerAsync(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);

        void DataChangedNotificationHandlerAsync(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);
    }
}