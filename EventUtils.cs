
namespace UA.MQTT.Publisher
{
    using System;
    using System.Collections.Generic;
    using Opc.Ua;
    using Opc.Ua.Client;

    public static class EventUtils
    {
        public static NodeId[] KnownEventTypes = new NodeId[] 
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

        public static NodeId FindEventType(MonitoredItem monitoredItem, EventFieldList notification)
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

        public static BaseEventState ConstructEvent(
            Session session,
            MonitoredItem monitoredItem,
            EventFieldList notification,
            Dictionary<NodeId,NodeId> eventTypeMappings)
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
                    ReferenceDescriptionCollection supertypes = EventUtils.BrowseSuperTypes(session, eventTypeId, false);

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

                default:
                {
                    e = new BaseEventState(null);
                    break;
                }
            }

            // get the filter which defines the contents of the notification.
            EventFilter filter = monitoredItem.Status.Filter as EventFilter;

            // initialize the event with the values in the notification.
            e.Update(session.SystemContext, filter.SelectClauses, notification);

            // save the orginal notification.
            e.Handle = notification;

            return e;
        }
        
        public static ReferenceDescriptionCollection Browse(Session session, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            try
            {
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                nodesToBrowse.Add(nodeToBrowse);

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Browse(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                do
                {
                    // check for error.
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        break;
                    }

                    // process results.
                    for (int i = 0; i < results[0].References.Count; i++)
                    {
                        references.Add(results[0].References[i]);
                    }

                    // check if all references have been fetched.
                    if (results[0].References.Count == 0 || results[0].ContinuationPoint == null)
                    {
                        break;
                    }

                    // continue browse operation.
                    ByteStringCollection continuationPoints = new ByteStringCollection();
                    continuationPoints.Add(results[0].ContinuationPoint);

                    session.BrowseNext(
                        null,
                        false,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                }
                while (true);

                //return complete list.
                return references;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        public static ReferenceDescriptionCollection BrowseSuperTypes(Session session, NodeId typeId, bool throwOnError)
        {
            ReferenceDescriptionCollection supertypes = new ReferenceDescriptionCollection();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types. 
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;
                                
                ReferenceDescriptionCollection references = Browse(session, nodeToBrowse, throwOnError);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]); 

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = Browse(session, nodeToBrowse, throwOnError);
                }

                // return complete list.
                return supertypes;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }
    }
}
