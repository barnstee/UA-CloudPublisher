
namespace UA.MQTT.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using System.Collections.Generic;

    public interface IMessageTrigger
    {
        /// <summary>
        /// Skip the first notification for a published node
        /// </summary>
        Dictionary<string, bool> SkipFirst { get; set; }

        /// <summary>
        /// Handler for event notifications
        /// </summary>
        void EventNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);

        /// <summary>
        /// Handler for data change notifications
        /// </summary>
        void DataChangedNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e);
    }
}