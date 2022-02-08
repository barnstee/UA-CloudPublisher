
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Class to describe the SimpleAttributeOperand.
    /// </summary>
    public class FilterSimpleAttributeModel
    {
        /// <summary>
        /// Ctor of the object.
        /// </summary>
        public FilterSimpleAttributeModel(string typeId, List<string> browsePath, string attributeId, string indexRange)
        {
            TypeId = typeId;
            BrowsePaths = browsePath;

            AttributeId = attributeId;
            attributeId.ResolveAttributeId();

            IndexRange = indexRange;
            indexRange.ResolveIndexRange();
        }

        /// <summary>
        /// The TypeId of the SimpleAttributeOperand.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string TypeId;

        /// <summary>
        /// The browse path as a list of QualifiedName's of the SimpleAttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> BrowsePaths;

        /// <summary>
        /// The AttributeId of the SimpleAttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AttributeId;

        /// <summary>
        /// The IndexRange of the SimpleAttributeOperand.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string IndexRange;
    }
}
