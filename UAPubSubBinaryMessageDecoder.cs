
namespace Opc.Ua.Cloud.Dashboard
{
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

    public class UAPubSubBinaryMessageDecoder
    {
        private readonly IUAApplication _app;

        private Dictionary<string, DataSetReaderDataType> _dataSetReaders;

        public UAPubSubBinaryMessageDecoder(IUAApplication app)
        {
            _app = app;

            _dataSetReaders = new Dictionary<string, DataSetReaderDataType>();

            // add default dataset readers
            AddUadpDataSetReader("default_uadp", 0, new DataSetMetaDataType());
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
        public void DecodeMessage(byte[] payload, DateTime receivedTime)
        {
            UadpNetworkMessage encodedMessage = new();
            encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, null);
            if (encodedMessage.IsMetaDataMessage)
            {
                UadpNetworkMessage uadpMessage = encodedMessage;
                AddUadpDataSetReader(uadpMessage.PublisherId.ToString(), uadpMessage.DataSetWriterId, encodedMessage.DataSetMetaData);
            }
            else
            {
                encodedMessage.Decode(ServiceMessageContext.GlobalContext, payload, _dataSetReaders.Values.ToArray());

                // reset metadata fields on default dataset readers
                _dataSetReaders["default_uadp:0"].DataSetMetaData.Fields.Clear();

                string publisherID = encodedMessage.PublisherId?.ToString();

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
                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = "nsu=http://opcfoundation.org/UA/CloudPublisher/;i=" + Math.Abs(item.Key.GetHashCode()),
                    ApplicationUri = _app.UAApplicationInstance.ApplicationConfiguration.ApplicationUri,
                    MessageContext = new ServiceMessageContext(),
                    Name = item.Key,
                    Value = item.Value
                };

                MessageProcessor.Enqueue(messageData);
            }
        }
    }
}
