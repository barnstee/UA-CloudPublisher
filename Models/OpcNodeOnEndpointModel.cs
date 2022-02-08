
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;

    /// <summary>
    /// Class describing a list of nodes
    /// </summary>
    public class OpcNodeOnEndpointModel
    {
        public OpcNodeOnEndpointModel(
            string id,
            string expandedNodeId = null,
            int opcSamplingInterval = 1000,
            int opcPublishingInterval = 0,
            string displayName = null,
            int heartbeatInterval = 0,
            bool skipFirst = false)
        {
            Id = id;
            ExpandedNodeId = expandedNodeId;
            OpcSamplingInterval = opcSamplingInterval;
            OpcPublishingInterval = opcPublishingInterval;
            DisplayName = displayName;
            HeartbeatInterval = heartbeatInterval;
            SkipFirst = skipFirst;
        }

        // Id can be:
        // a NodeId ("ns=")
        // an ExpandedNodeId ("nsu=")
        public string Id { get; set; }

        /// <summary>
        /// OPC UA ExpandedNodeId
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ExpandedNodeId { get; set; }

        /// <summary>
        /// OPC UA SamplingInterval
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int OpcSamplingInterval { get; set; }

        /// <summary>
        /// OPC UA PublishingInterval
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int OpcPublishingInterval { get; set; }

        /// <summary>
        /// OPC UA DisplayName
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        /// <summary>
        /// OPC UA HeartbeatInterval
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int HeartbeatInterval { get; set; }

        /// <summary>
        /// Skip first notification flag
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool SkipFirst { get; set; }
    }
}
