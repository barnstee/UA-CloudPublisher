
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using UA.MQTT.Publisher;
    using System.Collections.Generic;

    /// <summary>
    /// Class describing select clauses for an event filter.
    /// </summary>
    public class SelectClauseModel
    {
        /// <summary>
        /// Ctor of the object.
        /// </summary>
        public SelectClauseModel(string typeId, List<string> browsePaths, string attributeId, string indexRange)
        {
            TypeId = typeId;
            BrowsePaths = browsePaths;

            AttributeId = attributeId;
            attributeId.ResolveAttributeId();


            IndexRange = indexRange;
            indexRange.ResolveIndexRange();
        }

        /// <summary>
        /// The NodeId of the SimpleAttributeOperand.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string TypeId;

        /// <summary>
        /// A list of QualifiedName's describing the field to be published.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public List<string> BrowsePaths;

        /// <summary>
        /// The Attribute of the identified node to be published. This is Value by default.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AttributeId;

        /// <summary>
        /// The index range of the node values to be published.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string IndexRange;
    }
}
