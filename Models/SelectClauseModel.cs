
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;

    public class SelectClauseModel
    {
        [JsonProperty(Required = Required.Always)]
        public string TypeId { get; set; }
    }
}
