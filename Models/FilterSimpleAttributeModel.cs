
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class FilterSimpleAttributeModel
    {
        [JsonProperty(Required = Required.Always)]
        public string TypeId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> BrowsePaths { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AttributeId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string IndexRange { get; set; }
    }
}
