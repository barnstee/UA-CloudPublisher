
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

        public string EncodeHeader(ulong messageID, bool isMetaData = false)
        {
            // add PubSub JSON network message header (the mandatory fields of the OPC UA PubSub JSON NetworkMessage definition)
            // see https://reference.opcfoundation.org/v104/Core/docs/Part14/7.2.3/#7.2.3.2
            JsonEncoder encoder = new JsonEncoder(ServiceMessageContext.GlobalContext, Settings.Singleton.ReversiblePubSubEncoding);

            encoder.WriteString("MessageId", messageID.ToString());
            
            if (isMetaData)
            {
                encoder.WriteString("MessageType", "ua-metadata");
            }
            else
            {
                encoder.WriteString("MessageType", "ua-data");
            }

            encoder.WriteString("PublisherId", Settings.Singleton.PublisherName);

            if (!isMetaData)
            {
                encoder.PushArray("Messages");
            }

            // remove the closing bracket as we will add this later
            return encoder.CloseAndReturnText().TrimEnd('}');
        }

        public string EncodeMetadata(MessageProcessorModel messageData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(messageData.MessageContext, Settings.Singleton.ReversiblePubSubEncoding);

                encoder.WriteString("DataSetWriterId", messageData.DataSetWriterId);

                DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();

                dataSetMetaData.Name = "telemetry";

                dataSetMetaData.Fields = new FieldMetaDataCollection();
                
                if (messageData.EventValues != null && messageData.EventValues.Count > 0)
                {
                    // process events
                    foreach (EventValueModel eventValue in messageData.EventValues)
                    {
                        FieldMetaData fieldData = new FieldMetaData()
                        {
                            Name = eventValue.Name,
                            DataSetFieldId = new Uuid(Guid.NewGuid()),
                            BuiltInType = (byte)eventValue.Value.WrappedValue.TypeInfo.BuiltInType,
                            DataType = TypeInfo.GetDataTypeId(eventValue.Value.WrappedValue),
                            ValueRank = ValueRanks.Scalar,
                            Description = LocalizedText.Null
                        };

                        dataSetMetaData.Fields.Add(fieldData);
                    }
                }
                else
                {
                    FieldMetaData fieldData = new FieldMetaData()
                    {
                        Name = messageData.ExpandedNodeId,
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)messageData.Value.WrappedValue.TypeInfo.BuiltInType,
                        DataType = TypeInfo.GetDataTypeId(messageData.Value.WrappedValue),
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    };

                    dataSetMetaData.Fields.Add(fieldData);
                }
                             
                dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                };

                dataSetMetaData.Description = LocalizedText.Null;

                encoder.WriteEncodeable("MetaData", dataSetMetaData, typeof(DataSetMetaDataType));

                // remove the opening bracket as we need to stitch this together with the header
                return encoder.CloseAndReturnText().TrimStart('{');
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub metadata message failed.");
            }

            return string.Empty;
        }

        public string EncodePayload(MessageProcessorModel messageData)
        {
            try
            {
                JsonEncoder encoder = new JsonEncoder(messageData.MessageContext, Settings.Singleton.ReversiblePubSubEncoding);

                encoder.WriteString("DataSetWriterId", messageData.DataSetWriterId);

                encoder.WriteDateTime("Timestamp", messageData.Value.SourceTimestamp);

                encoder.PushStructure("Payload");
                                
                if (messageData.EventValues != null && messageData.EventValues.Count > 0)
                {
                    // process events
                    foreach (EventValueModel eventValue in messageData.EventValues)
                    {
                        // filter timestamps before encoding as we already specified it
                        eventValue.Value.SourceTimestamp = DateTime.MinValue;
                        eventValue.Value.ServerTimestamp = DateTime.MinValue;

                        encoder.WriteVariant(eventValue.Name, eventValue.Value);
                    }
                }
                else
                {
                    // filter timestamps before encoding as we already specified it
                    messageData.Value.SourceTimestamp = DateTime.MinValue;
                    messageData.Value.ServerTimestamp = DateTime.MinValue;

                    encoder.WriteVariant(messageData.ExpandedNodeId, messageData.Value);
                }

                encoder.PopStructure();

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub data message failed.");
            }

            return string.Empty;
        }
    }
}
