
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Net;

    /// <summary>
    /// Single OPC UA node publishing result.
    /// </summary>
    public class NodePublishingResultModel
    {
        /// <summary>
        /// Result of an OPC UA node publishing.
        /// </summary>
        [JsonRequired]
        public HttpStatusCode PublishingResult { get; set; }

        /// <summary>
        /// Optional payload. Will contain error when publishing fails.
        /// </summary>
        public string Payload { get; set; }
    }
}
