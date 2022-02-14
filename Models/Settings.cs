
namespace UA.MQTT.Publisher.Models
{
    using Microsoft.Extensions.Logging;
    using System;

    public class Settings
    {
        private readonly ILogger _logger;

        public Settings()
        {
            // default constructor needed for serialization
        }

        public Settings(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("Settings");
        }

        public string MQTTClientName { get; set; }

        public string MQTTBrokerName { get; set; }

        public string MQTTUsername { get; set; }

        public string MQTTPassword { get; set; }

        public string MQTTTopic { get; set; }

        public string MQTTResponseTopic { get; set; }

        public uint MQTTMessageSize { get; set; } = HubMessageSizeMax;

        public bool CreateMQTTSASToken { get; set; } = true;

        public string PublisherName { get; set; } = "UA-MQTT-Publisher";

        public uint InternalQueueCapacity { get; set; } = 1000; // records

        public uint DefaultSendIntervalSeconds { get; set; } = 1;

        public uint DiagnosticsLoggingInterval { get; set; } = 30; // seconds

        public uint DefaultOpcSamplingInterval { get; set; } = 500;

        public uint DefaultOpcPublishingInterval { get; set; } = 1000;

        public int UAStackTraceMask { get; set; } = 645; // Error, Trace, StartStop, Security

        public bool ReversiblePubSubEncoding { get; set; } = false;

        public const int MaxResponsePayloadLength = (128 * 1024) - 256;
        public const uint HubMessageSizeMax = 256 * 1024;

        public void LoadRequiredSettingsFromEnvironment()
        {
            try
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_CLIENTNAME")))
                {
                    throw new ArgumentException("MQTT_CLIENTNAME");
                }
                else
                {
                    MQTTClientName = Environment.GetEnvironmentVariable("MQTT_CLIENTNAME");
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_BROKERNAME")))
                {
                    throw new ArgumentException("MQTT_BROKERNAME");
                }
                else
                {
                    MQTTBrokerName = Environment.GetEnvironmentVariable("MQTT_BROKERNAME");
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_USERNAME")))
                {
                    throw new ArgumentException("MQTT_USERNAME");
                }
                else
                {
                    MQTTUsername = Environment.GetEnvironmentVariable("MQTT_USERNAME");
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_PASSWORD")))
                {
                    throw new ArgumentException("MQTT_PASSWORD");
                }
                else
                {
                    MQTTPassword = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_TOPIC")))
                {
                    throw new ArgumentException("MQTT_TOPIC");
                }
                else
                {
                    MQTTTopic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
                }

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC")))
                {
                    throw new ArgumentException("MQTT_RESPONSE_TOPIC");
                }
                else
                {
                    MQTTResponseTopic = Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC");
                }
            }
            catch (Exception)
            {
                _logger.LogCritical("Please specify environment variables: MQTT_CLIENTNAME, MQTT_BROKERNAME, MQTT_USERNAME, MQTT_PASSWORD, MQTT_TOPIC and MQTT_RESPONSE_TOPIC");
            }
        }

        public void LoadOptionalSettingsFromEnvironment()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MQTT_MESSAGE_NAME")))
            {
                MQTTMessageSize = uint.Parse(Environment.GetEnvironmentVariable("MQTT_MESSAGE_NAME"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD")))
            {
                CreateMQTTSASToken = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PUBLISHER_NAME")))
            {
                PublisherName = Environment.GetEnvironmentVariable("PUBLISHER_NAME");
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INTERNAL_QUEUE_CAPACITY")))
            {
                InternalQueueCapacity = uint.Parse(Environment.GetEnvironmentVariable("INTERNAL_QUEUE_CAPACITY"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEFAULT_SEND_INTERVAL_SECS")))
            {
                DefaultSendIntervalSeconds = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_SEND_INTERVAL_SECS"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIAGNOSTICS_LOGGING_INTERVAL")))
            {
                DiagnosticsLoggingInterval = uint.Parse(Environment.GetEnvironmentVariable("DIAGNOSTICS_LOGGING_INTERVAL"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEFAULT_UA_SAMPLING_INTERVAL")))
            {
                DefaultOpcSamplingInterval = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_UA_SAMPLING_INTERVAL"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEFAULT_UA_PUBLISHING_INTERVAL")))
            {
                DefaultOpcPublishingInterval = uint.Parse(Environment.GetEnvironmentVariable("DEFAULT_UA_PUBLISHING_INTERVAL"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UA_STACK_TRACE_MASK")))
            {
                UAStackTraceMask = int.Parse(Environment.GetEnvironmentVariable("UA_STACK_TRACE_MASK"));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_UA_PUBSUB_REVERSIBLE_ENCODING")))
            {
                ReversiblePubSubEncoding = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_UA_PUBSUB_REVERSIBLE_ENCODING"));
            }
        }
    }
}
