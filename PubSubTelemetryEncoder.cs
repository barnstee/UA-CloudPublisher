
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

        public PubSubTelemetryEncoder(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("PubSubTelemetryEncoder");
        }

        public string EncodeDataChange(MessageDataModel messageData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(messageData.MessageContext, Settings.Singleton.ReversiblePubSubEncoding);

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

        public string EncodeEvent(EventMessageDataModel eventData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(eventData.MessageContext, Settings.Singleton.ReversiblePubSubEncoding);

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
