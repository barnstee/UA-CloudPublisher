
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IMessageSource
    {
        Dictionary<string, bool> SkipFirst { get; set; }

        void EventNotificationHandlerAsync(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);

        void DataChangedNotificationHandlerAsync(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);
    }
}