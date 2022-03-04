
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.Collections.Generic;

    public class PublishNodesInterfaceModel
    {
        [JsonProperty(Required = Required.Always)]
        public string EndpointUrl { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<VariableModel> OpcNodes { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<EventModel> OpcEvents { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public UserAuthModeEnum OpcAuthenticationMode { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string UserName { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Password { get; set; }
    }
}
