
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;

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
                EventFieldList notification = e.NotificationValue as EventFieldList;
                if (notification == null)
                {
                    return;
                }

                NodeId eventTypeId = EventUtils.FindEventType(monitoredItem, notification);
                if (NodeId.IsNull(eventTypeId))
                {
                    return;
                }

                if (eventTypeId == ObjectTypeIds.RefreshStartEventType)
                {
                    return;
                }

                if (eventTypeId == ObjectTypeIds.RefreshEndEventType)
                {
                    return;
                }

                ConditionState condition = EventUtils.ConstructEvent(
                    (Session)monitoredItem.Subscription.Session,
                    monitoredItem,
                    notification,
                    new Dictionary<NodeId, NodeId>()) as ConditionState;
                if (condition == null)
                {
                    return;
                }

                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                    ApplicationUri = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri,
                    MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext
                };

                // Source
                if (condition.SourceName != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Source",
                        Value = new DataValue(condition.SourceName.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Condition
                if (condition.ConditionName != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Condition",
                        Value = new DataValue(condition.ConditionName.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Branch
                if (condition.BranchId != null && !NodeId.IsNull(condition.BranchId.Value))
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Branch",
                        Value = new DataValue(condition.BranchId.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Type
                INode type = monitoredItem.Subscription.Session.NodeCache.Find(condition.TypeDefinitionId);
                if (type != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Type",
                        Value = new DataValue(type.ToString() + " (" + NodeId.ToExpandedNodeId(new NodeId(type.NodeId.ToString()), monitoredItem.Subscription.Session.NamespaceUris).ToString() + ")")
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Severity
                if (condition.Severity != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Severity",
                        Value = new DataValue(((EventSeverity)condition.Severity.Value).ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Time
                if (condition.Time != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Time",
                        Value = new DataValue(condition.Time.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // State
                if (condition.EnabledState != null && condition.EnabledState.EffectiveDisplayName != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "State",
                        Value = new DataValue(condition.EnabledState.EffectiveDisplayName.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Message
                if (condition.Message != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Message",
                        Value = new DataValue(condition.Message.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                // Comment
                if (condition.Comment != null)
                {
                    EventValueModel eventValue = new EventValueModel()
                    {
                        Name = "Comment",
                        Value = new DataValue(condition.Comment.Value.ToString())
                    };

                    messageData.EventValues.Add(eventValue);
                }

                if (messageData.EventValues.Count > 0)
                {
                    MessageProcessor.Enqueue(messageData);
                }
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
                    ApplicationUri = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri,
                    MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext,
                    Name = monitoredItem.DisplayName,
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
