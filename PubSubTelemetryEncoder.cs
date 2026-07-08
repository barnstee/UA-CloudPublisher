
namespace Opc.Ua.Cloud.Publisher
{
    using Extensions;
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;

    public class PubSubTelemetryEncoder : IMessageEncoder
    {
        // OPC UA PubSub JSON-over-MQTT transport profile URI (see OPC 10000-7). The connection message
        // uses the OPC UA JSON message mapping, so we advertise the JSON MQTT profile rather than the UADP one.
        private const string PubSubJsonTransportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json";

        private readonly IUAApplication _app;
        private readonly ILogger _logger;

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
            JsonEncoder encoder = new(new ServiceMessageContext(_app.Telemetry), Settings.Instance.ReversiblePubSubEncoding);

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
                JsonEncoder encoder = new(messageData.MessageContext, Settings.Instance.ReversiblePubSubEncoding);

                ushort hash = (ushort)(messageData.ApplicationUri.GetDeterministicHashCode() ^ messageData.ExpandedNodeId.GetDeterministicHashCode());
                encoder.WriteUInt16("DataSetWriterId", hash);

                DataSetMetaDataType dataSetMetaData = BuildDataSetMetaData(messageData);

                encoder.WriteDateTime("Timestamp", DateTime.UtcNow);

                encoder.WriteEncodeable("MetaData", dataSetMetaData, typeof(DataSetMetaDataType));

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
            DataSetMetaDataType dataSetMetaData = new()
            {
                Name = messageData.ApplicationUri + ";" + messageData.ExpandedNodeId,
                Fields = new FieldMetaDataCollection()
            };

            if (messageData.EventValues != null && messageData.EventValues.Count > 0)
            {
                // process events
                foreach (EventValueModel eventValue in messageData.EventValues)
                {
                    TypeInfo eventTypeInfo = eventValue.Value?.WrappedValue.TypeInfo;
                    FieldMetaData fieldData = new()
                    {
                        Name = eventValue.Name,
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)(eventTypeInfo?.BuiltInType ?? BuiltInType.Null),
                        DataType = eventValue.Value != null ? TypeInfo.GetDataTypeId(eventValue.Value.WrappedValue) : null,
                        ValueRank = eventTypeInfo?.ValueRank ?? ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    };

                    dataSetMetaData.Fields.Add(fieldData);
                }
            }
            else
            {
                TypeInfo typeInfo = messageData.Value?.WrappedValue.TypeInfo;
                FieldMetaData fieldData = new()
                {
                    Name = messageData.Name,
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)(typeInfo?.BuiltInType ?? BuiltInType.Null),
                    DataType = messageData.Value != null ? TypeInfo.GetDataTypeId(messageData.Value.WrappedValue) : null,
                    ValueRank = typeInfo?.ValueRank ?? ValueRanks.Scalar,
                    Description = new LocalizedText(messageData.DataType)
                };

                dataSetMetaData.Fields.Add(fieldData);
            }

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
                foreach (FieldMetaData field in dataSetMetaData.Fields)
                {
                    if (field.DataType == null)
                    {
                        continue;
                    }

                    string namespaceUri = namespaceUris.GetString(field.DataType.NamespaceIndex);
                    if (!string.IsNullOrEmpty(namespaceUri) && !dataSetMetaData.Namespaces.Contains(namespaceUri))
                    {
                        dataSetMetaData.Namespaces.Add(namespaceUri);
                    }
                }
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
                JsonEncoder encoder = new(messageData.MessageContext, false);

                DataSetMetaDataType dataSetMetaData = BuildDataSetMetaData(messageData);

                encoder.WriteEncodeable("MetaData", dataSetMetaData, typeof(DataSetMetaDataType));

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
                JsonEncoder encoder = new(messageData.MessageContext, Settings.Instance.ReversiblePubSubEncoding);

                hash = (ushort)(messageData.ApplicationUri.GetDeterministicHashCode() ^ messageData.ExpandedNodeId.GetDeterministicHashCode());
                encoder.WriteUInt16("DataSetWriterId", hash);

                if ((messageData.EventValues == null) || (messageData.EventValues.Count == 0))
                {
                    encoder.WriteDateTime("Timestamp", messageData.Value.ServerTimestamp);
                }

                if (messageData.Value.StatusCode != StatusCodes.Good)
                {
                    encoder.WriteUInt32("Status", messageData.Value.StatusCode.Code);
                }

                encoder.PushStructure("Payload");

                if ((messageData.EventValues != null) && (messageData.EventValues.Count > 0))
                {
                    // process events
                    foreach (EventValueModel eventValue in messageData.EventValues)
                    {
                        // filter source timestamp before encoding
                        eventValue.Value.SourceTimestamp = DateTime.MinValue;

                        if (Settings.Instance.ReversiblePubSubEncoding)
                        {
                            encoder.WriteVariant(eventValue.Name, eventValue.Value.WrappedValue);
                        }
                        else
                        {
                            encoder.WriteVariant(eventValue.Name, eventValue.Value);
                        }
                    }
                }
                else
                {
                    // filter timestamps before encoding as we already encoded the server timestamp above
                    messageData.Value.SourceTimestamp = DateTime.MinValue;
                    messageData.Value.ServerTimestamp = DateTime.MinValue;

                    if (Settings.Instance.ReversiblePubSubEncoding)
                    {
                        encoder.WriteVariant(messageData.Name, messageData.Value.WrappedValue);
                    }
                    else
                    {
                        encoder.WriteVariant(messageData.Name, messageData.Value);
                    }
                }

                encoder.PopStructure();

                return encoder.CloseAndReturnText();
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
                JsonEncoder encoder = new(new ServiceMessageContext(_app.Telemetry), Settings.Instance.ReversiblePubSubEncoding);

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
                JsonEncoder encoder = new(new ServiceMessageContext(_app.Telemetry), Settings.Instance.ReversiblePubSubEncoding);

                encoder.WriteString("MessageId", messageID.ToString());
                encoder.WriteString("MessageType", "ua-connection");
                encoder.WriteString("PublisherId", Settings.Instance.PublisherName);
                encoder.WriteDateTime("Timestamp", DateTime.UtcNow);

                encoder.WriteEncodeable("Connection", BuildPubSubConnection(dataSetWriters), typeof(PubSubConnectionDataType));

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
            WriterGroupDataType writerGroup = new()
            {
                Name = Settings.Instance.PublisherName,
                Enabled = true,
                WriterGroupId = 1,
                PublishingInterval = Settings.Instance.DefaultSendIntervalSeconds * 1000.0,
                DataSetWriters = new DataSetWriterDataTypeCollection(),
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
                    writerGroup.DataSetWriters.Add(new DataSetWriterDataType()
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
                WriterGroups = new WriterGroupDataTypeCollection() { writerGroup }
            };
        }
    }
}
