
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using UA.MQTT.Publisher;

    /// <summary>
    /// Class to describe the AttributeOperand.
    /// </summary>
    public class FilterAttributeModel
    {
        /// <summary>
        /// Ctor of the object.
        /// </summary>
        public FilterAttributeModel(string nodeId, string alias, string browsePath, string attributeId, string indexRange)
        {
            NodeId = nodeId;
            BrowsePath = browsePath;
            Alias = alias;

            AttributeId = attributeId;
            attributeId.ResolveAttributeId();

            IndexRange = indexRange;
            indexRange.ResolveIndexRange();
        }

        /// <summary>
        /// The NodeId of the AttributeOperand.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string NodeId;

        /// <summary>
        /// The Alias of the AttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Alias;

        /// <summary>
        /// A RelativePath describing the browse path from NodeId of the AttributeOperand.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string BrowsePath;

        /// <summary>
        /// The AttibuteId of the AttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AttributeId;


        /// <summary>
        /// The IndexRange of the AttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string IndexRange;
    }
}
