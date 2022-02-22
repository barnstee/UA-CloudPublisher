
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class WhereClauseElementModel
    {
        [JsonProperty(Required = Required.Always)]
        public string Operator { get; set; }

        [JsonProperty(Required = Required.Always)]
        public List<WhereClauseOperandModel> Operands { get; set; }
    }
}
