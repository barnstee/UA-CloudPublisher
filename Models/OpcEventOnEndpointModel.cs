
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class OpcEventOnEndpointModel
    {
        [JsonProperty(Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<SelectClauseModel> SelectClauses { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<WhereClauseElementModel> WhereClauses { get; set; }
    }
}
