
namespace Opc.Ua.Cloud.Publisher
{
    using Extensions;
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class PubSubTelemetryEncoder : IMessageEncoder
    {
        // OPC UA PubSub JSON-over-MQTT transport profile URI (see OPC 10000-7). The connection message
        // uses the OPC UA JSON message mapping, so we advertise the JSON MQTT profile rather than the UADP one.
        private const string PubSubJsonTransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json";

        private readonly IUAApplication _app;
        private readonly ILogger _logger;

        private static JsonEncoderOptions GetJsonEncoderOptions(bool reversible)
        {
            return reversible ? JsonEncoderOptions.Verbose : JsonEncoderOptions.Compact;
        }

        public PubSubTelemetryEncoder(IUAApplication app, ILoggerFactory loggerFactory)
        {
            _app = app;
            _logger = loggerFactory.CreateLogger("PubSubTelemetryEncoder");
        }

        public string EncodeHeader(ulong messageID, bool isMetaData = false)
        {
            // The OPC UA PubSub JSON NetworkMessage header is optional (see OPC UA Part 14 JSON message mapping).
            // When it is omitted for data messages, the NetworkMessage is simply the JSON array of DataSetMessages,
            // so we only emit the opening bracket of that array here (the closing bracket is added when the batch is finished).
            if (!isMetaData && Settings.Instance.OmitNetworkMessageHeader)
            {
                return "[";
            }

            // add PubSub JSON network message header (the mandatory fields of the OPC UA PubSub JSON NetworkMessage definition)
            // see https://reference.opcfoundation.org/v105/Core/docs/Part14/7.2.5/#7.2.5.4
            JsonEncoder encoder = new(new MemoryStream(), ServiceMessageContext.Create(_app.Telemetry), GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

            encoder.WriteString("MessageId", messageID.ToString());

            if (isMetaData)
            {
                encoder.WriteString("MessageType", "ua-metadata");
            }
            else
            {
                encoder.WriteString("MessageType", "ua-data");
            }

            encoder.WriteString("PublisherId", Settings.Instance.PublisherName);

            // remove the closing bracket as we will add this later
            string header = encoder.CloseAndReturnText().TrimEnd('}');

            if (!isMetaData)
            {
                // JsonEncoder no longer provides PushArray, so open the Messages array manually
                header += ",\"Messages\":[";
            }

            return header;
        }

        public string EncodeMetadata(MessageProcessorModel messageData)
        {
            try
            {
                JsonEncoder encoder = new(new MemoryStream(), messageData.MessageContext, GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

                ushort hash = (ushort)(messageData.ApplicationUri.GetDeterministicHashCode() ^ messageData.ExpandedNodeId.GetDeterministicHashCode());
                encoder.WriteUInt16("DataSetWriterId", hash);

                DataSetMetaDataType dataSetMetaData = BuildDataSetMetaData(messageData);

                encoder.WriteDateTime("Timestamp", DateTime.UtcNow);

                encoder.WriteEncodeable("MetaData", dataSetMetaData, dataSetMetaData.TypeId);

                // remove the opening bracket as we need to stitch this together with the header
                return encoder.CloseAndReturnText().TrimStart('{');
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub metadata message failed.");
                return string.Empty;
            }
        }

        // Builds the DataSetMetaData (field type information) for the given message. Shared by the
        // OPC UA PubSub metadata message and the CloudEvents metadata message.
        private DataSetMetaDataType BuildDataSetMetaData(MessageProcessorModel messageData)
        {
            List<FieldMetaData> fields = new();
            DataSetMetaDataType dataSetMetaData = new()
            {
                Name = messageData.ApplicationUri + ";" + messageData.ExpandedNodeId
            };

            if (messageData.EventValues != null && messageData.EventValues.Count > 0)
            {
                // process events
                foreach (EventValueModel eventValue in messageData.EventValues)
                {
                    TypeInfo eventTypeInfo = eventValue.Value.WrappedValue.TypeInfo;
                    FieldMetaData fieldData = new()
                    {
                        Name = eventValue.Name,
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)eventTypeInfo.BuiltInType,
                        DataType = TypeInfo.GetDataTypeId(eventValue.Value.WrappedValue),
                        ValueRank = eventTypeInfo.ValueRank,
                        Description = LocalizedText.Null
                    };

                    fields.Add(fieldData);
                }
            }
            else
            {
                TypeInfo typeInfo = messageData.Value.WrappedValue.TypeInfo;
                FieldMetaData fieldData = new()
                {
                    Name = messageData.Name,
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)typeInfo.BuiltInType,
                    DataType = TypeInfo.GetDataTypeId(messageData.Value.WrappedValue),
                    ValueRank = typeInfo.ValueRank,
                    Description = new LocalizedText(messageData.DataType)
                };

                fields.Add(fieldData);
            }

            dataSetMetaData.Fields = fields.ToArrayOf();
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };

            dataSetMetaData.Description = LocalizedText.Null;

            // Populate the schema header namespaces with the distinct namespace URIs referenced by the field
            // data types, resolved from the local (session) namespace table - no server round-trips. The
            // structure/enum/simple type descriptions are intentionally left empty: built-in types don't need
            // them, and describing custom types would require reading their definitions from the server.
            NamespaceTable namespaceUris = messageData.MessageContext?.NamespaceUris;
            if (namespaceUris != null)
            {
                List<string> namespaces = new();
                foreach (FieldMetaData field in dataSetMetaData.Fields)
                {
                    if (field.DataType.IsNull)
                    {
                        continue;
                    }

                    string namespaceUri = namespaceUris.GetString(field.DataType.NamespaceIndex);
                    if (!string.IsNullOrEmpty(namespaceUri) && !namespaces.Contains(namespaceUri))
                    {
                        namespaces.Add(namespaceUri);
                    }
                }

                dataSetMetaData.Namespaces = namespaces.ToArrayOf();
            }

            return dataSetMetaData;
        }

        public string EncodeCloudEventMetadata(MessageProcessorModel messageData)
        {
            try
            {
                // CloudEvents binary mode: the OPC UA NetworkMessage and DataSet headers are mapped to CloudEvents
                // attributes (carried as transport headers), so the payload contains only the DataSetMetaData object,
                // encoded non-reversibly. See https://github.com/cloudevents/spec/blob/main/cloudevents/extensions/opcua.md
                JsonEncoder encoder = new(new MemoryStream(), messageData.MessageContext, GetJsonEncoderOptions(false));

                DataSetMetaDataType dataSetMetaData = BuildDataSetMetaData(messageData);

                encoder.WriteEncodeable("MetaData", dataSetMetaData, dataSetMetaData.TypeId);

                // the encoder wraps the value as {"MetaData":{...}}; return just the inner DataSetMetaData object
                string wrapped = encoder.CloseAndReturnText();
                const string metaDataPrefix = "{\"MetaData\":";
                if (wrapped.StartsWith(metaDataPrefix, StringComparison.Ordinal) && wrapped.EndsWith("}", StringComparison.Ordinal))
                {
                    return wrapped.Substring(metaDataPrefix.Length, wrapped.Length - metaDataPrefix.Length - 1);
                }

                return wrapped;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of CloudEvents PubSub metadata message failed.");
                return string.Empty;
            }
        }

        // Builds the CloudEvents context attributes for an OPC UA PubSub metadata message (binary content mode).
        // See https://github.com/cloudevents/spec/blob/main/cloudevents/extensions/opcua.md
        public IReadOnlyDictionary<string, string> BuildCloudEventMetadataAttributes(ulong messageId, ushort dataSetWriterId)
        {
            return new Dictionary<string, string>
            {
                ["specversion"] = "1.0",
                ["type"] = "ua-metadata",
                ["id"] = messageId.ToString(),
                ["source"] = Settings.Instance.PublisherName,
                ["subject"] = dataSetWriterId.ToString(),
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["datacontenttype"] = "application/json"
            };
        }

        public string EncodePayload(MessageProcessorModel messageData, out ushort hash)
        {
            try
            {
                JsonEncoder encoder = new(new MemoryStream(), messageData.MessageContext, GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

                hash = (ushort)(messageData.ApplicationUri.GetDeterministicHashCode() ^ messageData.ExpandedNodeId.GetDeterministicHashCode());
                encoder.WriteUInt16("DataSetWriterId", hash);

                if ((messageData.EventValues == null) || (messageData.EventValues.Count == 0))
                {
                    // use the server timestamp for the DataSetMessage timestamp; fall back to the source
                    // timestamp and finally to the current time, since many servers don't populate the
                    // server timestamp (which would otherwise serialize as 0001-01-01T00:00:00Z)
                    DateTime dataSetTimestamp = (DateTime)messageData.Value.ServerTimestamp;
                    if (dataSetTimestamp == DateTime.MinValue)
                    {
                        dataSetTimestamp = (DateTime)messageData.Value.SourceTimestamp;
                    }
                    if (dataSetTimestamp == DateTime.MinValue)
                    {
                        dataSetTimestamp = DateTime.UtcNow;
                    }

                    encoder.WriteDateTime("Timestamp", dataSetTimestamp);
                }

                if (messageData.Value.StatusCode != StatusCodes.Good)
                {
                    encoder.WriteUInt32("Status", messageData.Value.StatusCode.Code);
                }

                JsonEncoder payloadEncoder = new(new MemoryStream(), messageData.MessageContext, GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

                if ((messageData.EventValues != null) && (messageData.EventValues.Count > 0))
                {
                    // process events
                    foreach (EventValueModel eventValue in messageData.EventValues)
                    {
                        DataValue eventDataValue = new(
                            eventValue.Value.WrappedValue,
                            eventValue.Value.StatusCode,
                            DateTimeUtc.MinValue,
                            eventValue.Value.ServerTimestamp);

                        if (Settings.Instance.ReversiblePubSubEncoding)
                        {
                            payloadEncoder.WriteVariant(eventValue.Name, eventDataValue.WrappedValue);
                        }
                        else
                        {
                            payloadEncoder.WriteVariant(eventValue.Name, eventDataValue);
                        }
                    }
                }
                else
                {
                    // filter timestamps before encoding as we already encoded the server timestamp above
                    DataValue dataValue = new(
                        messageData.Value.WrappedValue,
                        messageData.Value.StatusCode,
                        DateTimeUtc.MinValue,
                        DateTimeUtc.MinValue);

                    if (Settings.Instance.ReversiblePubSubEncoding)
                    {
                        payloadEncoder.WriteVariant(messageData.Name, dataValue.WrappedValue);
                    }
                    else
                    {
                        payloadEncoder.WriteVariant(messageData.Name, dataValue);
                    }
                }

                string message = encoder.CloseAndReturnText();
                string payload = payloadEncoder.CloseAndReturnText();
                return message.TrimEnd('}') + ",\"Payload\":" + payload + "}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub data message failed.");
                hash = 0;
                return string.Empty;
            }
        }

        public string EncodeStatus(ulong messageID)
        {
            try
            {
                // encode a PubSub JSON status message
                JsonEncoder encoder = new(new MemoryStream(), ServiceMessageContext.Create(_app.Telemetry), GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

                encoder.WriteString("MessageId", messageID.ToString());
                encoder.WriteString("MessageType", "ua-status");
                encoder.WriteString("PublisherId", Settings.Instance.PublisherName);
                encoder.WriteDateTime("Timestamp", DateTime.UtcNow);
                encoder.WriteBoolean("IsCyclic", true);
                encoder.WriteEnumerated("Status", PubSubState.Operational);
                encoder.WriteDateTime("NextReportTime", DateTime.UtcNow.AddMilliseconds(Settings.Instance.DiagnosticsLoggingInterval * 1000));

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub status message failed.");
                return string.Empty;
            }
        }

        public string EncodeConnection(ulong messageID, IReadOnlyDictionary<ushort, string> dataSetWriters)
        {
            try
            {
                // encode a PubSub JSON connection (discovery) message, describing this Publisher's PubSubConnection.
                // See the JSON PubSubConnection definition (Table 192) in OPC UA Part 14 JSON message mapping:
                // https://reference.opcfoundation.org/v105/Core/docs/Part14/7.2.5.5.6
                JsonEncoder encoder = new(new MemoryStream(), ServiceMessageContext.Create(_app.Telemetry), GetJsonEncoderOptions(Settings.Instance.ReversiblePubSubEncoding));

                encoder.WriteString("MessageId", messageID.ToString());
                encoder.WriteString("MessageType", "ua-connection");
                encoder.WriteString("PublisherId", Settings.Instance.PublisherName);
                encoder.WriteDateTime("Timestamp", DateTime.UtcNow);

                PubSubConnectionDataType connection = BuildPubSubConnection(dataSetWriters);
                encoder.WriteEncodeable("Connection", connection, connection.TypeId);

                return encoder.CloseAndReturnText();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Generation of JSON PubSub connection message failed.");
                return string.Empty;
            }
        }

        // Builds the PubSubConnectionDataType advertised in the OPC UA PubSub connection (discovery) message.
        // Per OPC UA Part 14 (Table 192) the Address and ReaderGroup lists shall be empty and no configuration
        // properties are included in the PubSubConnection, WriterGroup or DataSetWriter.
        private static PubSubConnectionDataType BuildPubSubConnection(IReadOnlyDictionary<ushort, string> dataSetWriters)
        {
            List<DataSetWriterDataType> dataSetWritersList = new();
            WriterGroupDataType writerGroup = new()
            {
                Name = Settings.Instance.PublisherName,
                Enabled = true,
                WriterGroupId = 1,
                PublishingInterval = Settings.Instance.DefaultSendIntervalSeconds * 1000.0,
                // advertise the JSON network message layout produced by this WriterGroup
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType()
                {
                    NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId
                        | JsonNetworkMessageContentMask.DataSetClassId)
                }),
                // advertise the MQTT topic this WriterGroup publishes to
                TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType()
                {
                    QueueName = Settings.Instance.BrokerMessageTopic
                })
            };

            if (dataSetWriters != null)
            {
                foreach (KeyValuePair<ushort, string> dataSetWriter in dataSetWriters)
                {
                    dataSetWritersList.Add(new DataSetWriterDataType()
                    {
                        Name = dataSetWriter.Value,
                        Enabled = true,
                        DataSetWriterId = dataSetWriter.Key,
                        DataSetName = dataSetWriter.Value,
                        // advertise the JSON DataSetMessage layout for this DataSetWriter
                        MessageSettings = new ExtensionObject(new JsonDataSetWriterMessageDataType()
                        {
                            DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                                | JsonDataSetMessageContentMask.MetaDataVersion
                                | JsonDataSetMessageContentMask.SequenceNumber
                                | JsonDataSetMessageContentMask.Timestamp
                                | JsonDataSetMessageContentMask.Status
                                | JsonDataSetMessageContentMask.MessageType
                                | JsonDataSetMessageContentMask.DataSetWriterName)
                        }),
                        // advertise the MQTT data and metadata topics for this DataSetWriter
                        TransportSettings = new ExtensionObject(new BrokerDataSetWriterTransportDataType()
                        {
                            QueueName = Settings.Instance.BrokerMessageTopic,
                            MetaDataQueueName = Settings.Instance.BrokerMetadataTopic
                        })
                    });
                }
            }

            writerGroup.DataSetWriters = dataSetWritersList.ToArrayOf();

            return new PubSubConnectionDataType()
            {
                Name = Settings.Instance.PublisherName,
                Enabled = true,
                PublisherId = new Variant(Settings.Instance.PublisherName),
                TransportProfileUri = PubSubJsonTransportProfileUri,
                // advertise the MQTT broker address this Publisher connects to
                Address = new ExtensionObject(new NetworkAddressUrlDataType()
                {
                    NetworkInterface = string.Empty,
                    Url = Settings.Instance.BrokerUrl
                }),
                TransportSettings = new ExtensionObject(new BrokerConnectionTransportDataType()),
                WriterGroups = [writerGroup]
            };
        }
    }
}
