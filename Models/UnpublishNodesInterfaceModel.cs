
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class UnpublishNodesInterfaceModel
    {
        [JsonRequired]
        public string EndpointUrl { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<VariableModel> OpcNodes { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<EventModel> OpcEvents { get; set; }
    }
}
