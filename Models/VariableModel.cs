
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;

    public class VariableModel
    {
        public VariableModel(
            string expandedNodeId = null,
            int opcSamplingInterval = 1000,
            int opcPublishingInterval = 0,
            int heartbeatInterval = 0,
            bool skipFirst = false)
        {
            Id = expandedNodeId;
            OpcSamplingInterval = opcSamplingInterval;
            OpcPublishingInterval = opcPublishingInterval;
            HeartbeatInterval = heartbeatInterval;
            SkipFirst = skipFirst;
        }

        [JsonProperty(Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int OpcSamplingInterval { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int OpcPublishingInterval { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int HeartbeatInterval { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool SkipFirst { get; set; }
    }
}
