
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using UA.MQTT.Publisher;
    using System.Collections.Generic;

    /// <summary>
    /// Class to describe an operand of an WhereClauseElement.
    /// </summary>
    public class WhereClauseOperandModel
    {
        /// <summary>
        /// Holds an element value.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public uint? Element;

        /// <summary>
        /// Holds an Literal value.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Literal;

        /// <summary>
        /// Holds an AttributeOperand value.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public FilterAttributeModel Attribute;

        /// <summary>
        /// Holds an SimpleAttributeOperand value.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public FilterSimpleAttributeModel SimpleAttribute;

        /// <summary>
        /// Fetches the operand value.
        /// </summary>
        public FilterOperand GetOperand(TypeInfo typeInfo)
        {
            if (Element != null)
            {
                return new ElementOperand((uint)Element);
            }
            if (Literal != null)
            {
                object targetLiteral = TypeInfo.Cast(Literal, typeInfo.BuiltInType);
                return new LiteralOperand(targetLiteral);
            }
            if (Attribute != null)
            {
                AttributeOperand attributeOperand = new AttributeOperand(new NodeId(Attribute.NodeId), Attribute.BrowsePath);
                attributeOperand.Alias = Attribute.Alias;
                attributeOperand.AttributeId = Attribute.AttributeId.ResolveAttributeId();
                attributeOperand.IndexRange = Attribute.IndexRange;
                return attributeOperand;
            }
            if (SimpleAttribute != null)
            {
                List<QualifiedName> browsePaths = new List<QualifiedName>();
                if (SimpleAttribute.BrowsePaths != null)
                {
                    foreach (string browsePath in SimpleAttribute.BrowsePaths)
                    {
                        browsePaths.Add(new QualifiedName(browsePath));
                    }
                }
                SimpleAttributeOperand simpleAttributeOperand = new SimpleAttributeOperand(new NodeId(SimpleAttribute.TypeId), browsePaths.ToArray());
                simpleAttributeOperand.AttributeId = SimpleAttribute.AttributeId.ResolveAttributeId();
                simpleAttributeOperand.IndexRange = SimpleAttribute.IndexRange;
                return simpleAttributeOperand;
            }
            return null;
        }
    }
}
