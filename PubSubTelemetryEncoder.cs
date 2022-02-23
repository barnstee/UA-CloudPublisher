
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

        public string EncodeHeader(ulong messageID)
        {
            // add PubSub JSON network message header (the mandatory fields of the OPC UA PubSub JSON NetworkMessage definition)
            // see https://reference.opcfoundation.org/v104/Core/docs/Part14/7.2.3/#7.2.3.2
            JsonEncoder encoder = new JsonEncoder(new ServiceMessageContext(), Settings.Singleton.ReversiblePubSubEncoding);

            encoder.WriteString("MessageId", messageID.ToString());
            encoder.WriteString("MessageType", "ua-data");
            encoder.WriteString("PublisherId", Settings.Singleton.PublisherName);
            encoder.PushArray("Messages");

            // remove the closing bracket as we will add this later
            return encoder.CloseAndReturnText().TrimEnd('}');
        }

        public string EncodePayload(MessageProcessorModel messageData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(messageData.MessageContext, Settings.Singleton.ReversiblePubSubEncoding);

                encoder.WriteString("DataSetWriterId", messageData.DataSetWriterId);

                encoder.PushStructure("Payload");

                // process EventValues object properties
                if (messageData.EventValues != null && messageData.EventValues.Count > 0)
                {
                    foreach (EventValueModel eventValue in messageData.EventValues)
                    {
                        encoder.WriteDataValue(eventValue.Name, eventValue.Value);
                    }
                }
                else
                {
                    encoder.WriteDataValue(messageData.DisplayName, messageData.Value);
                }

                encoder.PopStructure();

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub message failed.");
            }

            return string.Empty;
        }
    }
}
