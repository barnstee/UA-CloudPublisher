
namespace Opc.Ua.Cloud.Publisher.Models
{
    using Newtonsoft.Json;
    using System;

    public class DiagnosticsModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTime PublisherStartTime { get; set; } = DateTime.UtcNow;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public bool ConnectedToBroker { get; set; } = false;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcSessionsConnected { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcSubscriptionsConnected { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public int NumberOfOpcMonitoredItemsMonitored { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long MonitoredItemsQueueCount { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long EnqueueCount { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long EnqueueFailureCount { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long NumberOfEvents { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long MissedSendIntervalCount { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long TooLargeCount { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long SentBytes { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long SentMessages { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTime SentLastTime { get; set; } = DateTime.UtcNow;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long FailedMessages { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long AverageMessageLatency { get; set; } = 0;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long AverageNotificationsInBrokerMessage { get; set; } = 0;
    }
}
