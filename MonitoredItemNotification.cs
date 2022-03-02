
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class MonitoredItemNotification : IMessageSource
    {
        private readonly ILogger _logger;

        public Dictionary<string, bool> SkipFirst { get; set; } = new Dictionary<string, bool>();

        public MonitoredItemNotification(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("MonitoredItemNotification");
        }

        public void EventNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                if (e == null || e.NotificationValue == null || monitoredItem == null || monitoredItem.Subscription == null || monitoredItem.Subscription.Session == null)
                {
                    return;
                }

                if (!(e.NotificationValue is EventFieldList notificationValue))
                {
                    return;
                }

                if (!(notificationValue.Message is NotificationMessage message))
                {
                    return;
                }

                if (!(message.NotificationData is ExtensionObjectCollection notificationData) || notificationData.Count == 0)
                {
                    return;
                }

                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                    DataSetWriterId = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri + ":" + monitoredItem.Subscription.CurrentPublishingInterval.ToString(),
                    MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext
                };

                foreach (ExtensionObject eventList in notificationData)
                {
                    EventNotificationList eventNotificationList = eventList.Body as EventNotificationList;
                    foreach (EventFieldList eventFieldList in eventNotificationList.Events)
                    {
                        int i = 0;
                        foreach (Variant eventField in eventFieldList.EventFields)
                        {
                            // prepare event field values
                            EventValueModel eventValue = new EventValueModel();
                            eventValue.Name = monitoredItem.GetFieldName(i++);

                            // use the Value as reported in the notification event argument
                            eventValue.Value = new DataValue(eventField);

                            messageData.EventValues.Add(eventValue);
                        }
                    }
                }

                MessageProcessor.Enqueue(messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing monitored item notification");
            }
        }

        public void DataChangedNotificationHandler(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                if (e == null || e.NotificationValue == null || monitoredItem == null || monitoredItem.Subscription == null || monitoredItem.Subscription.Session == null)
                {
                    return;
                }

                if (!(e.NotificationValue is Opc.Ua.MonitoredItemNotification notification))
                {
                    return;
                }

                if (!(notification.Value is DataValue value))
                {
                    return;
                }

                // filter out messages with bad status
                if (StatusCode.IsBad(notification.Value.StatusCode.Code))
                {
                    _logger.LogWarning($"Filtered notification with bad status code '{notification.Value.StatusCode.Code}'");
                    return;
                }

                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                    DataSetWriterId = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri + ":" + monitoredItem.Subscription.CurrentPublishingInterval.ToString(),
                    MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext,
                    Value = value
                };

                // skip event if needed
                if (SkipFirst.ContainsKey(messageData.ExpandedNodeId) && SkipFirst[messageData.ExpandedNodeId])
                {
                    _logger.LogInformation($"Skipping first telemetry event for node '{messageData.ExpandedNodeId}'.");
                    SkipFirst[messageData.ExpandedNodeId] = false;
                }
                else
                {
                    MessageProcessor.Enqueue(messageData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing monitored item notification");
            }
        }
    }
}
