
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class EventModel
    {
        [JsonProperty(Required = Required.Always)]
        public string ExpandedNodeId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<SelectClauseModel> SelectClauses { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<WhereClauseModel> WhereClauses { get; set; }
    }
}
