
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;

    public class FilterModel
    {
        [JsonProperty(Required = Required.Always)]
        public string OfType { get; set; }
    }
}
