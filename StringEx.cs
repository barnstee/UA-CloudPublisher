
namespace UA.MQTT.Publisher
{
    using Opc.Ua;
    using System;

    public static class StringEx
    {
        /// <summary>
        /// ResolveAttributeId string extension
        /// </summary>
        /// <param name="attributeId"></param>
        /// <returns></returns>
        public static uint ResolveAttributeId(this string attributeId)
        {
            uint resolvedAttributeId = Attributes.Value;
            if (!string.IsNullOrEmpty(attributeId))
            {
                if (uint.TryParse(attributeId, out resolvedAttributeId))
                {
                    resolvedAttributeId = uint.Parse(attributeId);
                }
                else
                {
                    if ((resolvedAttributeId = Attributes.GetIdentifier(attributeId)) == 0)
                    {
                        string errorMessage = $"The given Attribute '{attributeId}' in a select clause is not a valid attribute identifier.";
                        throw new Exception(errorMessage);
                    }
                }
            }

            return resolvedAttributeId;
        }

        /// <summary>
        /// ResolveIndexRange string extension
        /// </summary>
        /// <param name="indexRange"></param>
        /// <returns></returns>
        public static NumericRange ResolveIndexRange(this string indexRange)
        {
            NumericRange resolvedIndexRange = new NumericRange(0, 0);
            if (!string.IsNullOrEmpty(indexRange))
            {
                try
                {
                    resolvedIndexRange = NumericRange.Parse(indexRange);
                }
                catch (Exception ex)
                {
                    string errorMessage = $"The given IndexRange '{indexRange}' in a select clause has not a valid syntax.";
                    throw new Exception(errorMessage, ex);
                }
            }

            return resolvedIndexRange;
        }

        /// <summary>
        /// ResolveFilterOperator string extension
        /// </summary>
        /// <param name="filterOperator"></param>
        /// <returns></returns>
        public static FilterOperator ResolveFilterOperator(this string filterOperator)
        {

            if (Enum.TryParse(filterOperator, out FilterOperator resolvedFilterOperator) == false)
            {
                string errorMessage = $"The given filter operator '{filterOperator}' in a where clause has not a valid syntax.";
                throw new Exception(errorMessage);
            }

            return resolvedFilterOperator;
        }

        /// <summary>
        /// ToNodeId string extension
        /// </summary>
        /// <param name="id"></param>
        /// <param name="namespaceTable"></param>
        /// <returns></returns>
        public static NodeId ToNodeId(this string id, NamespaceTable namespaceTable)
        {
            NodeId nodeId = null;
            if (id.StartsWith("nsu="))
            {
                ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(id);
                int namespaceIndex = namespaceTable.GetIndex(expandedNodeId.NamespaceUri);
                if (namespaceIndex >= 0)
                {
                    nodeId = new NodeId(expandedNodeId.Identifier, (ushort)namespaceIndex);
                }
                else
                {
                    string errorMessage = $"The given id cannot be converted to a NodeId '{id}', because the namespace URI '{expandedNodeId.NamespaceUri}' is unknown.";
                    throw new Exception(errorMessage);
                }
            }
            else
            {
                nodeId = NodeId.Parse(id);
            }

            return nodeId;
        }
    }
}
