
namespace UA.MQTT.Publisher.Models
{
    using System;

    public class Settings
    {
        public string MQTTClientName { get; set; } = Environment.GetEnvironmentVariable("MQTT_CLIENTNAME");

        public string MQTTBrokerName { get; set; } = Environment.GetEnvironmentVariable("MQTT_BROKERNAME");

        public string MQTTUsername { get; set; } = Environment.GetEnvironmentVariable("MQTT_USERNAME");

        public string MQTTPassword { get; set; } = Environment.GetEnvironmentVariable("MQTT_PASSWORD");

        public string MQTTTopic { get; set; } = Environment.GetEnvironmentVariable("MQTT_TOPIC");

        public string MQTTResponseTopic { get; set; } = Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC");

        public uint MQTTMessageSize { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("MQTT_MESSAGE_NAME"));

        public bool CreateMQTTSASToken { get; set; } = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD"));

        public string LogFilePath { get; set; } = Environment.GetEnvironmentVariable("LOG_FILE_PATH");

        public string PublisherName { get; set; } = Environment.GetEnvironmentVariable("PUBLISHER_NAME");

        public uint InternalQueueCapacity { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("INTERNAL_QUEUE_CAPACITY"));

        public uint DefaultSendIntervalSeconds { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_SEND_INTERVAL_SECS"));

        public uint DiagnosticsLoggingInterval { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("DIAGNOSTICS_LOGGING_INTERVAL"));

        public uint DefaultOpcSamplingInterval { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_UA_SAMPLING_INTERVAL"));

        public uint DefaultOpcPublishingInterval { get; set; } = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_UA_PUBLISHING_INTERVAL"));

        public int UAStackTraceMask { get; set; } = int.Parse(Environment.GetEnvironmentVariable("UA_STACK_TRACE_MASK"));

        public bool ReversiblePubSubEncoding { get; set; } = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_UA_PUBSUB_REVERSIBLE_ENCODING"));

        public const int MaxResponsePayloadLength = (128 * 1024) - 256;
        public const uint HubMessageSizeMax = 256 * 1024;
    }
}
