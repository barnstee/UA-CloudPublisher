
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class UnpublishNodesInterfaceModel
    {
        [JsonRequired]
        public string EndpointUrl { get; set; }

        public List<VariableModel> OpcNodes { get; set; }
    }
}
