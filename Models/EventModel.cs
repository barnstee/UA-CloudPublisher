
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class EventModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ExpandedNodeId { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<SelectClauseModel> SelectClauses { get; set; }
    }
}
