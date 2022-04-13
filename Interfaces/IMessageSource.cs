
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using System.Collections.Generic;

    public interface IMessageSource
    {
        Dictionary<string, bool> SkipFirst { get; set; }

        void EventNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);

        void DataChangedNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);
    }
}