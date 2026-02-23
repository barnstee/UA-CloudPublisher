
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;

    public class MonitoredItemNotification : IMessageSource
    {
        private readonly ILogger _logger;

        public Dictionary<string, bool> SkipFirst { get; set; } = new Dictionary<string, bool>();

        private NodeId[] KnownEventTypes = new NodeId[]
{
            ObjectTypeIds.BaseEventType,
            ObjectTypeIds.ConditionType,
            ObjectTypeIds.DialogConditionType,
            ObjectTypeIds.AlarmConditionType,
            ObjectTypeIds.ExclusiveLimitAlarmType,
            ObjectTypeIds.NonExclusiveLimitAlarmType,
            ObjectTypeIds.AuditEventType,
            ObjectTypeIds.AuditUpdateMethodEventType
        };

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

                NodeId eventTypeId = FindEventType(monitoredItem, notification);
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

                ConditionState condition = ConstructEvent(
                    (Session)monitoredItem.Subscription.Session,
                    monitoredItem,
                    notification,
                    new Dictionary<NodeId, NodeId>()) as ConditionState;
                if (condition == null)
                {
                    return;
                }

                MessageProcessorModel messageData = new()
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
                INode type = monitoredItem.Subscription.Session.NodeCache.FindAsync(condition.TypeDefinitionId).GetAwaiter().GetResult();
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

        private NodeId FindEventType(MonitoredItem monitoredItem, EventFieldList notification)
        {
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            if (filter != null)
            {
                for (int i = 0; i < filter.SelectClauses.Count; i++)
                {
                    SimpleAttributeOperand clause = filter.SelectClauses[i];

                    if (clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == BrowseNames.EventType)
                    {
                        return notification.EventFields[i].Value as NodeId;
                    }
                }
            }

            return null;
        }

        private BaseEventState ConstructEvent(
            Session session,
            MonitoredItem monitoredItem,
            EventFieldList notification,
            Dictionary<NodeId, NodeId> eventTypeMappings)
        {
            // find the event type.
            NodeId eventTypeId = FindEventType(monitoredItem, notification);

            if (eventTypeId == null)
            {
                return null;
            }

            // look up the known event type.
            NodeId knownTypeId = null;

            if (!eventTypeMappings.TryGetValue(eventTypeId, out knownTypeId))
            {
                // check for a known type
                for (int j = 0; j < KnownEventTypes.Length; j++)
                {
                    if (KnownEventTypes[j] == eventTypeId)
                    {
                        knownTypeId = eventTypeId;
                        eventTypeMappings.Add(eventTypeId, eventTypeId);
                        break;
                    }
                }

                // browse for the supertypes of the event type.
                if (knownTypeId == null)
                {
                    ReferenceDescriptionCollection supertypes = UAClient.BrowseSuperTypes(session, eventTypeId, false).GetAwaiter().GetResult();

                    // can't do anything with unknown types.
                    if (supertypes == null)
                    {
                        return null;
                    }

                    // find the first supertype that matches a known event type.
                    for (int i = 0; i < supertypes.Count; i++)
                    {
                        for (int j = 0; j < KnownEventTypes.Length; j++)
                        {
                            if (KnownEventTypes[j] == supertypes[i].NodeId)
                            {
                                knownTypeId = KnownEventTypes[j];
                                eventTypeMappings.Add(eventTypeId, knownTypeId);
                                break;
                            }
                        }

                        if (knownTypeId != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (knownTypeId == null)
            {
                return null;
            }

            // all of the known event types have a UInt32 as identifier.
            uint? id = knownTypeId.Identifier as uint?;

            if (id == null)
            {
                return null;
            }

            // construct the event based on the known event type.
            BaseEventState e = null;

            switch (id.Value)
            {
                case ObjectTypes.ConditionType: { e = new ConditionState(null); break; }
                case ObjectTypes.DialogConditionType: { e = new DialogConditionState(null); break; }
                case ObjectTypes.AlarmConditionType: { e = new AlarmConditionState(null); break; }
                case ObjectTypes.ExclusiveLimitAlarmType: { e = new ExclusiveLimitAlarmState(null); break; }
                case ObjectTypes.NonExclusiveLimitAlarmType: { e = new NonExclusiveLimitAlarmState(null); break; }
                case ObjectTypes.AuditEventType: { e = new AuditEventState(null); break; }
                case ObjectTypes.AuditUpdateMethodEventType: { e = new AuditUpdateMethodEventState(null); break; }
                default: { e = new BaseEventState(null); break; }
            }

            // get the filter which defines the contents of the notification.
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            // initialize the event with the values in the notification.
            e.Update(session.SystemContext, filter.SelectClauses, notification);

            // save the original notification.
            e.Handle = notification;

            return e;
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

                string dataType = string.Empty;
                VariableNode variable = (VariableNode)monitoredItem.Subscription.Session.NodeCache.FindAsync(monitoredItem.StartNodeId).GetAwaiter().GetResult();
                if (variable != null)
                {
                    // handle complex types
                    ComplexTypeSystem complexTypeSystem = new(monitoredItem.Subscription.Session);
                    ExpandedNodeId nodeTypeId = variable.DataType;
                    complexTypeSystem.LoadTypeAsync(nodeTypeId).ConfigureAwait(false);

                    dataType = NodeId.ToExpandedNodeId(variable.DataType, monitoredItem.Subscription.Session.NamespaceUris).ToString();
                }

                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                    ApplicationUri = monitoredItem.Subscription.Session.Endpoint.Server.ApplicationUri,
                    MessageContext = (ServiceMessageContext)monitoredItem.Subscription.Session.MessageContext,
                    Name = monitoredItem.DisplayName,
                    Value = value,
                    DataType = dataType
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
