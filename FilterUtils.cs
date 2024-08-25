
namespace Opc.Ua.Cloud.Publisher
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using System.Collections.Generic;

    public class FilterUtils
    {
        public static SimpleAttributeOperandCollection ConstructSelectClauses(Session session)
        {
            // browse the type model in the server address space to find the fields available for the event type.
            SimpleAttributeOperandCollection selectClauses = new SimpleAttributeOperandCollection();

            // must always request the NodeId for the condition instances.
            // this can be done by specifying an operand with an empty browse path.
            SimpleAttributeOperand operand = new SimpleAttributeOperand();

            operand.TypeDefinitionId = ObjectTypeIds.BaseEventType;
            operand.AttributeId = Attributes.NodeId;
            operand.BrowsePath = new QualifiedNameCollection();

            selectClauses.Add(operand);

            // add the fields for the selected EventTypes.
            CollectFields(session, ObjectTypeIds.BaseEventType, selectClauses);
            
            return selectClauses;
        }

        public static ContentFilter ConstructWhereClause(IList<NodeId> eventTypes, EventSeverity severity)
        {
            ContentFilter whereClause = new ContentFilter();

            // the code below constructs a filter that looks like this:
            // (Severity >= X OR LastSeverity >= X) AND (SuppressedOrShelved == False) AND (OfType(A) OR OfType(B))
                        
            // add the severity.
            ContentFilterElement element1 = null;
            if (severity > EventSeverity.Min)
            {
                // select the Severity property of the event.
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                operand1.BrowsePath.Add(BrowseNames.Severity);
                operand1.AttributeId = Attributes.Value;

                // specify the value to compare the Severity property with.
                LiteralOperand operand2 = new LiteralOperand();
                operand2.Value = new Variant((ushort)severity);

                // specify that the Severity property must be GreaterThanOrEqual the value specified.
                element1 = whereClause.Push(FilterOperator.GreaterThanOrEqual, operand1, operand2);
            }

            // add the event types.
            ContentFilterElement element2 = null;
            if (eventTypes != null && eventTypes.Count > 0)
            {
                // save the last element.
                for (int i = 0; i < eventTypes.Count; i++)
                {
                    // we uses the 'OfType' operator to limit events to thoses with specified event type. 
                    LiteralOperand operand1 = new LiteralOperand();
                    operand1.Value = new Variant(eventTypes[i]);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.OfType, operand1);

                    // need to chain multiple types together with an OR clause.
                    if (element2 != null)
                    {
                        element2 = whereClause.Push(FilterOperator.Or, element2, element3);
                    }
                    else
                    {
                        element2 = element3;
                    }
                }

                // need to link the set of event types with the previous filters.
                if (element1 != null)
                {
                    whereClause.Push(FilterOperator.And, element1, element2);
                }
            }

            return whereClause;
        }

        private static void CollectFields(Session session, NodeId eventTypeId, SimpleAttributeOperandCollection eventFields)
        {
            // get the supertypes.
            ReferenceDescriptionCollection supertypes = EventUtils.BrowseSuperTypes(session, eventTypeId, false);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            Dictionary<NodeId,QualifiedNameCollection> foundNodes = new Dictionary<NodeId, QualifiedNameCollection>();
            QualifiedNameCollection parentPath = new QualifiedNameCollection();

            for (int i = supertypes.Count - 1; i >= 0; i--)
            {
                CollectFields(session, (NodeId)supertypes[i].NodeId, parentPath, eventFields, foundNodes);
            }

            // collect the fields for the selected type.
            CollectFields(session, eventTypeId, parentPath, eventFields, foundNodes);
        }

        private static void CollectFields(
            Session session,
            NodeId nodeId,
            QualifiedNameCollection parentPath,
            SimpleAttributeOperandCollection eventFields,
            Dictionary<NodeId, QualifiedNameCollection> foundNodes)
        {
            // find all of the children of the field.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.Aggregates;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            ReferenceDescriptionCollection children = UAClient.Browse(session, nodeToBrowse, false);

            if (children == null)
            {
                return;
            }

            // process the children.
            for (int i = 0; i < children.Count; i++)
            {
                ReferenceDescription child = children[i];

                if (child.NodeId.IsAbsolute)
                {
                    continue;
                }

                // construct browse path.
                QualifiedNameCollection browsePath = new QualifiedNameCollection(parentPath);
                browsePath.Add(child.BrowseName);

                // check if the browse path is already in the list.
                if (!ContainsPath(eventFields, browsePath))
                {
                    SimpleAttributeOperand field = new SimpleAttributeOperand();

                    field.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                    field.BrowsePath = browsePath;
                    field.AttributeId = (child.NodeClass == NodeClass.Variable)?Attributes.Value:Attributes.NodeId;

                    eventFields.Add(field);
                }

                // recusively find all of the children.
                NodeId targetId = (NodeId)child.NodeId;

                // need to guard against loops.
                if (!foundNodes.ContainsKey(targetId))
                {
                    foundNodes.Add(targetId, browsePath);
                    CollectFields(session, (NodeId)child.NodeId, browsePath, eventFields, foundNodes);
                }
            }
        }

        private static bool ContainsPath(SimpleAttributeOperandCollection selectClause, QualifiedNameCollection browsePath)
        {
            for (int i = 0; i < selectClause.Count; i++)
            {
                SimpleAttributeOperand field = selectClause[i];

                if (field.BrowsePath.Count != browsePath.Count)
                {
                    continue;
                }

                bool match = true;

                for (int j = 0; j < field.BrowsePath.Count; j++)
                {
                    if (field.BrowsePath[j] != browsePath[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
