
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    /// <summary>
    /// Wrapper for the OPC UA monitored item, which monitored a nodes we need to publish.
    /// </summary>
    public class MonitoredItemNotificationTrigger : IMessageTrigger
    {
        /// <summary>
        /// Skip first notification dictionary
        /// </summary>
        public Dictionary<string, bool> SkipFirst { get; set; } = new Dictionary<string, bool>();

        public MonitoredItemNotificationTrigger(
            ILoggerFactory loggerFactory,
            ISettingsConfiguration settingsConfiguration)
        {
            _logger = loggerFactory.CreateLogger("MonitoredItemNotificationTrigger");
            _settingsConfiguration = settingsConfiguration;
        }

        /// <summary>
        /// The notification that a monitored item event has occured on an OPC UA server.
        /// </summary>
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

                EventMessageDataModel eventMessageData = new EventMessageDataModel();
                eventMessageData.EndpointUrl = monitoredItem.Subscription.Session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                eventMessageData.ApplicationUri = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri + _settingsConfiguration.PublisherSite;
                eventMessageData.DisplayName = monitoredItem.DisplayName;
                eventMessageData.ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString();
                eventMessageData.DataSetWriterId = eventMessageData.ApplicationUri + ":" + monitoredItem.Subscription.CurrentPublishingInterval.ToString();
                eventMessageData.MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext;

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

                            eventMessageData.EventValues.Add(eventValue);
                        }
                    }
                }

                _logger.LogDebug($"   ApplicationUri: {eventMessageData.ApplicationUri}");
                _logger.LogDebug($"   EndpointUrl: {eventMessageData.EndpointUrl}");
                _logger.LogDebug($"   DisplayName: {eventMessageData.DisplayName}");
                _logger.LogDebug($"   Value: {eventMessageData.Value}");

                if (monitoredItem.Subscription == null)
                {
                    _logger.LogDebug($"Subscription already removed. No more details available.");
                }
                else
                {
                    _logger.LogDebug($"Enqueue a new message from subscription {(monitoredItem.Subscription == null ? "removed" : monitoredItem.Subscription.Id.ToString(CultureInfo.InvariantCulture))}");
                    _logger.LogDebug($" with publishing interval: {monitoredItem?.Subscription?.PublishingInterval} and sampling interval: {monitoredItem?.SamplingInterval}):");
                }

                // add message to fifo send queue
                if (monitoredItem.Subscription == null)
                {
                    _logger.LogDebug($"Subscription already removed. No more details available.");
                }
                else
                {
                    _logger.LogDebug($"Enqueue a new message from subscription {(monitoredItem.Subscription == null ? "removed" : monitoredItem.Subscription.Id.ToString(CultureInfo.InvariantCulture))}");
                    _logger.LogDebug($" with publishing interval: {monitoredItem?.Subscription?.PublishingInterval} and sampling interval: {monitoredItem?.SamplingInterval}):");
                }

                // enqueue the telemetry event
                MessageProcessingEngine.Enqueue(eventMessageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing monitored item notification");
            }
        }

        /// <summary>
        /// The notification that the data for a monitored item has changed on an OPC UA server.
        /// </summary>
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

                // filter out configured suppression status codes
                if (_settingsConfiguration.SuppressedOpcStatusCodes != null && _settingsConfiguration.SuppressedOpcStatusCodes.Contains(notification.Value.StatusCode.Code))
                {
                    _logger.LogDebug($"Filtered notification with status code '{notification.Value.StatusCode.Code}'");
                    return;
                }

                MessageDataModel messageData = new MessageDataModel();
                messageData.EndpointUrl = monitoredItem.Subscription.Session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                messageData.ApplicationUri = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri + _settingsConfiguration.PublisherSite;
                messageData.DisplayName = monitoredItem.DisplayName;
                messageData.ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString();
                messageData.DataSetWriterId = messageData.ApplicationUri + ":" + monitoredItem.Subscription.CurrentPublishingInterval.ToString();
                messageData.MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext;
                messageData.Value = value;

                _logger.LogDebug($"   ApplicationUri: {messageData.ApplicationUri}");
                _logger.LogDebug($"   EndpointUrl: {messageData.EndpointUrl}");
                _logger.LogDebug($"   DisplayName: {messageData.DisplayName}");
                _logger.LogDebug($"   Value: {messageData.Value}");

                if (monitoredItem.Subscription == null)
                {
                    _logger.LogDebug($"Subscription already removed. No more details available.");
                }
                else
                {
                    _logger.LogDebug($"Enqueue a new message from subscription {(monitoredItem.Subscription == null ? "removed" : monitoredItem.Subscription.Id.ToString(CultureInfo.InvariantCulture))}");
                    _logger.LogDebug($" with publishing interval: {monitoredItem?.Subscription?.PublishingInterval} and sampling interval: {monitoredItem?.SamplingInterval}):");
                }

                // skip event if needed
                if (SkipFirst.ContainsKey(messageData.ExpandedNodeId) && SkipFirst[messageData.ExpandedNodeId])
                {
                    _logger.LogInformation($"Skipping first telemetry event for node '{messageData.DisplayName}'.");
                    SkipFirst[messageData.ExpandedNodeId] = false;
                }
                else
                {
                    // enqueue the telemetry event
                    MessageProcessingEngine.Enqueue(messageData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing monitored item notification");
            }
        }

        private readonly ILogger _logger;
        private readonly ISettingsConfiguration _settingsConfiguration;
    }
}
