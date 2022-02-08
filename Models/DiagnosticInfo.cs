
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System;

    /// <summary>
    /// Model for a diagnostic info response.
    /// </summary>
    public class DiagnosticInfo
    {
        /// <summary>
        /// The asset ID of the asset for which the diagnostic info is for
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string AssetID;

        /// <summary>
        /// Stores startup time.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTime PublisherStartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of connected OPC UA session.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcSessionsConnected { get; set; } = 0;

        /// <summary>
        /// Number of connected OPC UA subscriptions.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcSubscriptionsConnected { get; set; } = 0;

        /// <summary>
        /// Number of monitored OPC UA nodes.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcMonitoredItemsMonitored { get; set; } = 0;

        /// <summary>
        /// Number of events in the monitored items queue.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long MonitoredItemsQueueCount { get; set; } = 0;

        /// <summary>
        /// Number of events we enqueued.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long EnqueueCount { get; set; } = 0;

        /// <summary>
        /// Number of times enqueueing of events failed.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long EnqueueFailureCount { get; set; } = 0;

        /// <summary>
        /// Number of events sent to the cloud.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long NumberOfEvents { get; set; } = 0;

        /// <summary>
        /// Number of times we were not able to make the send interval, because too high load.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long MissedSendIntervalCount { get; set; } = 0;

        /// <summary>
        /// Number of times the size for the event payload was too large for a telemetry message.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long TooLargeCount { get; set; } = 0;

        /// <summary>
        /// Number of payload bytes we sent to the cloud.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long SentBytes { get; set; } = 0;

        /// <summary>
        /// Number of messages we sent to the cloud.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long SentMessages { get; set; } = 0;

        /// <summary>
        /// Time when we sent the last telemetry message.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTime SentLastTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of times we were not able to sent the telemetry message to the cloud.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long FailedMessages { get; set; } = 0;

        /// <summary>
        /// Average message latency
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long AverageMessageLatency { get; set; } = 0;

        /// <summary>
        /// The Hub Protocol in use
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string HubProtocol { get; set; } = "Mqtt";

        /// <summary>
        /// The average number of OPC UA notifications batched into a single MQTT message
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long AverageNotificationsInHubMessage { get; set; } = 0;
    }
}
