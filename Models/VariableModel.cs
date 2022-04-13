
namespace Opc.Ua.Cloud.Publisher.Models
{
    using Newtonsoft.Json;

    public class VariableModel
    {
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
