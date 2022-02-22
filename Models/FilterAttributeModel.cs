
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;

    public class FilterAttributeModel
    {
        [JsonProperty(Required = Required.Always)]
        public string NodeId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Alias { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string BrowsePath { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AttributeId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string IndexRange { get; set; }
    }
}
