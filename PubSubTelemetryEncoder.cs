
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using System;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class PubSubTelemetryEncoder : IMessageEncoder
    {
        private readonly ILogger _logger;
        private readonly Settings _settings;

        public PubSubTelemetryEncoder(ILoggerFactory loggerFactory, Settings settings)
        {
            _logger = loggerFactory.CreateLogger("PubSubTelemetryEncoder");
            _settings = settings;
        }
        /// <summary>
        /// Creates a JSON message to be sent to IoT Hub, based on the telemetry configuration for the endpoint.
        /// </summary>
        public string EncodeDataChange(MessageDataModel messageData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(messageData.MessageContext, _settings.ReversiblePubSubEncoding);

                encoder.WriteString("DataSetWriterId", messageData.DataSetWriterId);

                encoder.PushStructure("Payload");

                encoder.WriteDataValue(messageData.DisplayName, messageData.Value);

                encoder.PopStructure();

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub data change message failed.");
            }

            return string.Empty;
        }

        /// <summary>
        /// Creates a IoT Hub JSON message for an event notification, based on the telemetry configuration for the endpoint.
        /// </summary>
        public string EncodeEvent(EventMessageDataModel eventData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(eventData.MessageContext, _settings.ReversiblePubSubEncoding);

                encoder.WriteString("DataSetWriterId", eventData.DataSetWriterId);

                encoder.PushStructure("Payload");

                // process EventValues object properties
                if (eventData.EventValues != null && eventData.EventValues.Count > 0)
                {
                    foreach (EventValueModel eventValue in eventData.EventValues)
                    {
                        encoder.WriteDataValue(eventData.DisplayName, eventData.Value);
                    }
                }

                encoder.PopStructure();

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub event message failed.");
            }

            return string.Empty;
        }
    }
}
