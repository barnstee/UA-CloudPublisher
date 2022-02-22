
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class UnpublishNodesMethodRequestModel
    {
        [JsonRequired]
        public string EndpointUrl { get; set; }

        public List<OpcNodeOnEndpointModel> OpcNodes { get; set; }
    }
}
