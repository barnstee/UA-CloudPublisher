using Microsoft.AspNetCore.Mvc.Rendering;

namespace UA.MQTT.Publisher.Models
{
    public class OpcSessionModel
    {
        public string SessionId { get; set; }

        public string EndpointUrl { get; set; }

        public string StatusMessage { get; set; }
    }
}