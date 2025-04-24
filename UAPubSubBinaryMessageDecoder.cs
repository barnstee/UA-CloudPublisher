
namespace Opc.Ua.Cloud.Dashboard
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using Opc.Ua.PubSub;
    using Opc.Ua.PubSub.Encoding;
    using Opc.Ua.PubSub.PublishedData;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class UAPubSubBinaryMessageDecoder
    {
        private readonly IUAApplication _app;
        private readonly ILogger _logger;

        private Dictionary<string, DataSetReaderDataType> _dataSetReaders;

        public UAPubSubBinaryMessageDecoder(IUAApplication app, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("UAPubSubBinaryMessageDecoder");
            _app = app;

            _dataSetReaders = new Dictionary<string, DataSetReaderDataType>();

            // add default dataset readers
            AddUadpDataSetReader("default_uadp", 0, new DataSetMetaDataType());
            AddJsonDataSetReader("default_json", 0, new DataSetMetaDataType());
        }

        public void ProcessMessage(byte[] payload, DateTime receivedTime, string contentType)
        {
            string message = string.Empty;
            try
            {
                message = Encoding.UTF8.GetString(payload);
                if (message != null)
                {
                    if (((contentType != null) && (contentType == "application/json")) || message.TrimStart().StartsWith('{') || message.TrimStart().StartsWith('['))
                    {
                        if (message.TrimStart().StartsWith('['))
                        {
                            // we received an array of messages
                            object[] messageArray = JsonConvert.DeserializeObject<object[]>(message);
                            foreach (object singleMessage in messageArray)
                            {
                                DecodeMessage(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(singleMessage)), receivedTime, new Opc.Ua.PubSub.Encoding.JsonNetworkMessage());
                            }
                        }
                        else
                        {
                            DecodeMessage(payload, receivedTime, new Opc.Ua.PubSub.Encoding.JsonNetworkMessage());
                        }
                    }
                    else
                    {
                        DecodeMessage(payload, receivedTime, new UadpNetworkMessage());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception {ex.Message} processing message {message}");
            }
        }

        private void AddUadpDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata)
        {
            DataSetReaderDataType uadpDataSetReader = new DataSetReaderDataType();
            uadpDataSetReader.Name = publisherId + ":" + dataSetWriterId.ToString();
            uadpDataSetReader.DataSetWriterId = dataSetWriterId;
            uadpDataSetReader.PublisherId = publisherId;
            uadpDataSetReader.Enabled = true;
            uadpDataSetReader.DataSetFieldContentMask = (uint)DataSetFieldContentMask.None;
            uadpDataSetReader.KeyFrameCount = 1;
            uadpDataSetReader.TransportSettings = new ExtensionObject(new BrokerDataSetReaderTransportDataType());
            uadpDataSetReader.DataSetMetaData = metadata;

            UadpDataSetReaderMessageDataType uadpDataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType()
            {
                NetworkMessageContentMask = (uint)(UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.DataSetClassId),
                DataSetMessageContentMask = (uint)UadpDataSetMessageContentMask.None,
            };

            uadpDataSetReader.MessageSettings = new ExtensionObject(uadpDataSetReaderMessageSettings);

            TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            uadpDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

            if (_dataSetReaders.ContainsKey(uadpDataSetReader.Name))
            {
                _dataSetReaders[uadpDataSetReader.Name] = uadpDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(uadpDataSetReader.Name, uadpDataSetReader);
            }
        }

        private void AddJsonDataSetReader(string publisherId, ushort dataSetWriterId, DataSetMetaDataType metadata)
        {
            DataSetReaderDataType jsonDataSetReader = new DataSetReaderDataType();
            jsonDataSetReader.Name = publisherId + ":" + dataSetWriterId.ToString();
            jsonDataSetReader.PublisherId = publisherId;
            jsonDataSetReader.DataSetWriterId = dataSetWriterId;
            jsonDataSetReader.Enabled = true;
            jsonDataSetReader.DataSetFieldContentMask = (uint)DataSetFieldContentMask.None;
            jsonDataSetReader.KeyFrameCount = 1;
            jsonDataSetReader.TransportSettings = new ExtensionObject(new BrokerDataSetReaderTransportDataType());
            jsonDataSetReader.DataSetMetaData = metadata;

            JsonDataSetReaderMessageDataType jsonDataSetReaderMessageSettings = new JsonDataSetReaderMessageDataType()
            {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader | JsonNetworkMessageContentMask.DataSetMessageHeader | JsonNetworkMessageContentMask.DataSetClassId | JsonNetworkMessageContentMask.PublisherId),
                DataSetMessageContentMask = (uint)JsonDataSetMessageContentMask.None,
            };

            jsonDataSetReader.MessageSettings = new ExtensionObject(jsonDataSetReaderMessageSettings);

            TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            jsonDataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

            if (_dataSetReaders.ContainsKey(jsonDataSetReader.Name))
            {
                _dataSetReaders[jsonDataSetReader.Name] = jsonDataSetReader;
            }
            else
            {
                _dataSetReaders.Add(jsonDataSetReader.Name, jsonDataSetReader);
            }
        }

        private void DecodeMessage(byte[] payload, DateTime receivedTime, UaNetworkMessage encodedMessage)
        {
            encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, null);
            if (encodedMessage.IsMetaDataMessage)
            {
                // setup dataset reader
                if (encodedMessage is Opc.Ua.PubSub.Encoding.JsonNetworkMessage)
                {
                    Opc.Ua.PubSub.Encoding.JsonNetworkMessage jsonMessage = (Opc.Ua.PubSub.Encoding.JsonNetworkMessage)encodedMessage;

                    AddJsonDataSetReader(jsonMessage.PublisherId, jsonMessage.DataSetWriterId, encodedMessage.DataSetMetaData);
                }
                else
                {
                    UadpNetworkMessage uadpMessage = (UadpNetworkMessage)encodedMessage;
                    AddUadpDataSetReader(uadpMessage.PublisherId.ToString(), uadpMessage.DataSetWriterId, encodedMessage.DataSetMetaData);
                }
            }
            else
            {
                encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, _dataSetReaders.Values.ToArray());

                // reset metadata fields on default dataset readers
                _dataSetReaders["default_uadp:0"].DataSetMetaData.Fields.Clear();
                _dataSetReaders["default_json:0"].DataSetMetaData.Fields.Clear();

                string publisherID = string.Empty;
                if (encodedMessage is Opc.Ua.PubSub.Encoding.JsonNetworkMessage)
                {
                    publisherID = ((Opc.Ua.PubSub.Encoding.JsonNetworkMessage)encodedMessage).PublisherId?.ToString();
                }
                else
                {
                    publisherID = ((UadpNetworkMessage)encodedMessage).PublisherId?.ToString();
                }

                Dictionary<string, DataValue> flattenedPublishedNodes = new();
                foreach (UaDataSetMessage datasetmessage in encodedMessage.DataSetMessages)
                {
                    string dataSetWriterId = datasetmessage.DataSetWriterId.ToString();
                    string assetName = string.Empty;

                    if (_dataSetReaders.ContainsKey(publisherID + ":" + dataSetWriterId))
                    {
                        string name = _dataSetReaders[publisherID + ":" + dataSetWriterId].DataSetMetaData.Name;
                        if (name.IndexOf(";") != -1)
                        {
                            assetName = name.Substring(0, name.LastIndexOf(';'));
                        }
                        else
                        {
                            assetName = name;
                        }
                    }
                    else
                    {
                        // if we didn't reveice a valid asset name, we use the publisher ID instead, if configured by the user
                        assetName = publisherID;
                    }

                    if (datasetmessage.DataSet != null)
                    {
                        for (int i = 0; i < datasetmessage.DataSet.Fields.Length; i++)
                        {
                            Field field = datasetmessage.DataSet.Fields[i];
                            if (field.Value != null)
                            {
                                // if the timestamp in the field is missing, use the timestamp from the dataset message instead
                                if (field.Value.SourceTimestamp == DateTime.MinValue)
                                {
                                    field.Value.SourceTimestamp = datasetmessage.Timestamp;
                                }

                                // if we didn't receive valid metadata, we use the dataset writer ID and index into the dataset instead
                                string telemetryName = string.Empty;

                                if (field.FieldMetaData == null || string.IsNullOrEmpty(field.FieldMetaData.Name))
                                {
                                    telemetryName = assetName + "_" + datasetmessage.DataSetWriterId.ToString() + "_" + i.ToString();
                                }
                                else
                                {
                                    telemetryName = assetName + "_" + field.FieldMetaData.Name + "_" + field.FieldMetaData.BinaryEncodingId.ToString();
                                }

                                try
                                {
                                    // check for variant array
                                    if (field.Value.Value is Variant[])
                                    {
                                        // convert to string
                                        DataValue value = new DataValue(new Variant(field.Value.ToString()), field.Value.StatusCode, field.Value.SourceTimestamp);

                                        if (!flattenedPublishedNodes.ContainsKey(telemetryName))
                                        {
                                            flattenedPublishedNodes.Add(telemetryName, value);
                                        }
                                    }
                                    else
                                    {
                                        if (!flattenedPublishedNodes.ContainsKey(telemetryName))
                                        {
                                            flattenedPublishedNodes.Add(telemetryName, field.Value);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Cannot parse field {field.Value}: {ex.Message}");
                                }
                            }
                        }
                    }
                }

                SendPublishedNodestoMessageProcessor(flattenedPublishedNodes, receivedTime);
            }
        }

        private void SendPublishedNodestoMessageProcessor(Dictionary<string, DataValue> flattenedPublishedNodes, DateTime receivedTime)
        {
            foreach (KeyValuePair<string, DataValue> item in flattenedPublishedNodes)
            {
                MessageProcessorModel messageData = new()
                {
                    ExpandedNodeId = "nsu=http://opcfoundation.org/UA/CloudPublisher/;i=" + Math.Abs(item.Key.GetHashCode()),
                    ApplicationUri = _app.UAApplicationInstance.ApplicationConfiguration.ApplicationUri,
                    MessageContext = new ServiceMessageContext(),
                    Name = item.Key,
                    Value = item.Value
                };

                messageData.Value.ServerTimestamp = receivedTime;

                MessageProcessor.Enqueue(messageData);
            }
        }
    }
}
