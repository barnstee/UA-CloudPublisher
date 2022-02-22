
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class DiagnosticInfoMethodResponseModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public IEnumerable<DiagnosticInfo> DiagnosticInfos;
    }
}
