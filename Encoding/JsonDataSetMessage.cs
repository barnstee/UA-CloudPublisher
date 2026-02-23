/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua.PubSub.PublishedData;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// The JsonDataSetMessage class handler.
    /// It handles the JsonDataSetMessage encoding
    /// </summary>
    public class JsonDataSetMessage : UaDataSetMessage
    {
        #region Fields
        private const string kFieldPayload = "Payload";
        private FieldTypeEncodingMask m_fieldTypeEncoding;
        #endregion

        #region Constructors
        /// <summary>
        /// Create new instance of <see cref="JsonDataSetMessage"/> with DataSet parameter
        /// </summary>
        /// <param name="dataSet"></param>
        public JsonDataSetMessage(DataSet dataSet = null)
        {
            DataSet = dataSet;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get JsonDataSetMessageContentMask
        /// The DataSetWriterMessageContentMask defines the flags for the content of the DataSetMessage header.
        /// The Json message mapping specific flags are defined by the <see cref="JsonDataSetMessageContentMask"/> enum.
        /// </summary>
        public JsonDataSetMessageContentMask DataSetMessageContentMask { get; set; }

        /// <summary>
        /// Flag that indicates if the dataset message header is encoded
        /// </summary>
        public bool HasDataSetMessageHeader { get; set; }

        #endregion Properties

        #region Public Methods
        /// <summary>
        /// Set DataSetFieldContentMask
        /// </summary>
        /// <param name="fieldContentMask">The new <see cref="DataSetFieldContentMask"/> for this dataset</param>
        public override void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
        {
            FieldContentMask = fieldContentMask;

            if (FieldContentMask == DataSetFieldContentMask.None)
            {
                // 00 Variant Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.Variant;
            }
            else if ((FieldContentMask & DataSetFieldContentMask.RawData) != 0)
            {
                // If the RawData flag is set, all other bits are ignored.
                // 01 RawData Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.RawData;
            }
            else if ((FieldContentMask & (DataSetFieldContentMask.StatusCode
                                          | DataSetFieldContentMask.SourceTimestamp
                                          | DataSetFieldContentMask.ServerTimestamp
                                          | DataSetFieldContentMask.SourcePicoSeconds
                                          | DataSetFieldContentMask.ServerPicoSeconds)) != 0)
            {
                // 10 DataValue Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.DataValue;
            }
        }

        /// <summary>
        /// Encodes the dataset message
        /// </summary>
        /// <param name="jsonEncoder">The <see cref="JsonEncoder"/> used to encode this object.</param>
        /// <param name="fieldName">The field name to be used to encode this object, by default it is null.</param>
        public void Encode(JsonEncoder jsonEncoder, string fieldName = null)
        {
            jsonEncoder.PushStructure(fieldName);
            if (HasDataSetMessageHeader)
            {
                EncodeDataSetMessageHeader(jsonEncoder);
            }

            if (DataSet != null)
            {
                EncodePayload(jsonEncoder, HasDataSetMessageHeader);
            }

            jsonEncoder.PopStructure();
        }

        /// <summary>
        /// Decode dataset from the provided json decoder using the provided <see cref="DataSetReaderDataType"/>.
        /// </summary>
        /// <param name="jsonDecoder">The json decoder that contains the json stream.</param>
        /// <param name="messagesCount">Number of Messages found in current jsonDecoder. If 0 then there is SingleDataSetMessage</param>
        /// <param name="messagesListName">The name of the Messages list</param>
        /// <param name="dataSetReader">The <see cref="DataSetReaderDataType"/> used to decode the data set.</param>
        public void DecodePossibleDataSetReader(JsonDecoder jsonDecoder, int messagesCount, string messagesListName, DataSetReaderDataType dataSetReader)
        {
            if (messagesCount == 0)
            {
                // check if there shall be a dataset header and decode it
                if (HasDataSetMessageHeader)
                {
                    DecodeDataSetMessageHeader(jsonDecoder);

                    // push into PayloadStructure if there was a dataset header
                    jsonDecoder.PushStructure(kFieldPayload);
                }

                DecodeErrorReason = ValidateMetadataVersion(dataSetReader?.DataSetMetaData?.ConfigurationVersion);
                if (IsMetadataMajorVersionChange)
                {
                    return;
                }

                // handle single dataset with no network message header & no dataset message header (the content of the payload)
                DataSet = DecodePayloadContent(jsonDecoder, dataSetReader);
            }
            else
            {
                for (int index = 0; index < messagesCount; index++)
                {
                    bool wasPush = jsonDecoder.PushArray(messagesListName, index);
                    if (wasPush)
                    {
                        // Attempt decoding the DataSet fields
                        DecodePossibleDataSetReader(jsonDecoder, dataSetReader);

                        // redo jsonDecoder stack
                        jsonDecoder.Pop();

                        if ((dataSetReader.Name != "default_json:0") && (DataSet != null))
                        {
                            // the dataset was decoded
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Attempt to decode dataset from the KeyValue pairs
        /// </summary>
        private void DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            // check if there shall be a dataset header and decode it
            if (HasDataSetMessageHeader)
            {
                DecodeDataSetMessageHeader(jsonDecoder);
            }

            if (dataSetReader.DataSetWriterId != 0 && DataSetWriterId != dataSetReader.DataSetWriterId)
            {
                return;
            }

            object token = null;
            string payloadStructureName = kFieldPayload;
            // try to read "Payload" structure
            if (!jsonDecoder.ReadField(kFieldPayload, out token))
            {
                // Decode the Messages element in case there is no "Payload" structure
                jsonDecoder.ReadField(null, out token);
                payloadStructureName = null;
            }

            Dictionary<string, object> payload = token as Dictionary<string, object>;

            if ((payload != null) && !string.IsNullOrEmpty(dataSetReader.Name) && (dataSetReader.Name != "default_json:0") && (dataSetReader.DataSetMetaData != null) && (dataSetReader.DataSetMetaData.Fields.Count > 0))
            {
                DecodeErrorReason = ValidateMetadataVersion(dataSetReader.DataSetMetaData.ConfigurationVersion);

                if ((payload.Count > dataSetReader.DataSetMetaData.Fields.Count) ||
                     IsMetadataMajorVersionChange)
                {
                    // filter out payload that has more fields than the searched datasetMetadata or
                    // doesn't pass metadata version
                    return;
                }
                // check also the field names from reader, if any extra field names then the payload is not matching
                foreach (string key in payload.Keys)
                {
                    var field = dataSetReader.DataSetMetaData.Fields.FirstOrDefault(f => f.Name == key);
                    if (field == null)
                    {
                        // the field from payload was not found in dataSetReader therefore the payload is not suitable to be decoded
                        return;
                    }
                }
            }
            try
            {
                // try decoding Payload Structure
                bool wasPush = jsonDecoder.PushStructure(payloadStructureName);
                if (wasPush)
                {
                    if (DataSet == null)
                    {
                        DataSet = DecodePayloadContent(jsonDecoder, dataSetReader);
                    }
                    else
                    {
                        // combine with existing fields
                        List<Field> fields = new List<Field>();
                        fields.AddRange(DataSet.Fields);
                        fields.AddRange(DecodePayloadContent(jsonDecoder, dataSetReader).Fields);

                        DataSet.Fields = fields.ToArray();
                    }
                }
            }
            finally
            {
                // redo decode stack
                jsonDecoder.Pop();
            }
        }

        /// <summary>
        /// Flatten a Complex Field
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        private Variant[] FlattenComplexField(Dictionary<string, object> complexType)
        {
            List<Variant> result = new List<Variant>();

            foreach (KeyValuePair<string, object> complexSubType in complexType)
            {
                // check for nested complex type
                if (complexSubType.Value is Dictionary<string, object>)
                {
                    result.AddRange(FlattenComplexField((Dictionary<string, object>)complexSubType.Value));
                }
                else if (complexSubType.Value is List<object>)
                {
                    // array
                    foreach (object element in (List<object>)complexSubType.Value)
                    {
                        // check for complex type
                        if (element is Dictionary<string, object>)
                        {
                            result.AddRange(FlattenComplexField((Dictionary<string, object>)element));
                        }
                        else
                        {
                            result.Add(new Variant(element));
                        }
                    }
                }
                else
                {
                    string[] keyValue = new string[2];
                    keyValue[0] = complexSubType.Key;
                    keyValue[1] = complexSubType.Value.ToString();
                    result.Add(new Variant(keyValue, TypeInfo.Construct(keyValue)));
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Decode a Field
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        private List<DataValue> DecodeField(Dictionary<string, object> field)
        {
            List<DataValue> result = new List<DataValue>();

            if (field != null)
            {
                // check for non-reversible encoding
                DataValue newValue = null;
                if (field.ContainsKey("Value"))
                {
                    // check for complex type
                    if (field["Value"] is Dictionary<string, object>)
                    {
                        newValue = new DataValue(new Variant(FlattenComplexField((Dictionary<string, object>)field["Value"])));
                    }
                    else if (field["Value"] is List<object>)
                    {
                        // array
                        List<Variant> list = new List<Variant>();
                        foreach (object element in (List<object>)field["Value"])
                        {
                            // check for complex type
                            if (element is Dictionary<string, object>)
                            {
                                list.AddRange(FlattenComplexField((Dictionary<string, object>)element));
                            }
                            else
                            {
                                list.Add(new Variant(element));
                            }
                        }
                        newValue = new DataValue(new Variant(list.ToArray()));
                    }
                    else
                    {
                        newValue = new DataValue(new Variant(field["Value"]));
                    }
                }

                // check for reversible encoding
                if (field.ContainsKey("Type") && field.ContainsKey("Body"))
                {
                    // check for localized text
                    if ((Int64)field["Type"] == 21)
                    {
                        Dictionary<string, object> localizedText = (Dictionary<string, object>)field["Body"];
                        if (localizedText.ContainsKey("Text"))
                        {
                            newValue = new DataValue(new Variant(localizedText["Text"], TypeInfo.Construct(localizedText["Text"])));
                        }
                    }

                    // check for complex type
                    if (field["Body"] is Dictionary<string, object>)
                    {
                        newValue = new DataValue(new Variant(FlattenComplexField((Dictionary<string, object>)field["Body"])));
                    }
                    else if (field["Body"] is List<object>)
                    {
                        // array
                        List<Variant> list = new List<Variant>();
                        foreach (object element in (List<object>)field["Body"])
                        {
                            // check for complex type
                            if (element is Dictionary<string, object>)
                            {
                                list.AddRange(FlattenComplexField((Dictionary<string, object>)element));
                            }
                            else
                            {
                                list.Add(new Variant(element));
                            }
                        }
                        newValue = new DataValue(new Variant(list.ToArray()));
                    }
                    else
                    {
                        newValue = new DataValue(new Variant(field["Body"], TypeInfo.Construct(field["Body"])));
                    }
                }

                // check for source timestamp
                if ((newValue != null) && field.ContainsKey("SourceTimestamp"))
                {
                    newValue.SourceTimestamp = (DateTime)field["SourceTimestamp"];
                }

                // check for server timestamp
                if ((newValue != null) && field.ContainsKey("ServerTimestamp"))
                {
                    newValue.ServerTimestamp = (DateTime)field["ServerTimestamp"];
                }

                // add the new value to our collection
                if (newValue != null)
                {
                    result.Add(newValue);
                }
            }

            return result;
        }

        /// <summary>
        /// Decode the Content of the Payload and create a DataSet object from it
        /// </summary>
        private DataSet DecodePayloadContent(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            DataSetMetaDataType dataSetMetaData = dataSetReader.DataSetMetaData;
            List<Field> dataFields = new List<Field>();

            List<DataValue> dataValues = new List<DataValue>();
            if (!string.IsNullOrEmpty(dataSetMetaData.Name) && dataSetMetaData?.Fields.Count > 0)
            {
                for (int index = 0; index < dataSetMetaData?.Fields.Count; index++)
                {
                    FieldMetaData fieldMetaData = dataSetMetaData?.Fields[index];

                    object token;
                    if (jsonDecoder.ReadField(fieldMetaData.Name, out token))
                    {
                        // check for array
                        if (token is List<object>)
                        {
                            foreach (object subfield in (List<object>)token)
                            {
                                if (subfield is Dictionary<string, object>)
                                {
                                    dataValues.AddRange(DecodeField((Dictionary<string, object>)subfield));
                                }
                                else
                                {
                                    dataValues.Add(new DataValue(new Variant(subfield)));
                                }
                            }
                        }
                        else if (token is Dictionary<string, object>)
                        {
                            dataValues.AddRange(DecodeField((Dictionary<string, object>)token));
                        }
                        else
                        {
                            dataValues.Add(new DataValue(new Variant(token)));
                        }
                    }
                }
            }
            else
            {
                // no metadata
                object token;
                if (jsonDecoder.ReadField(null, out token))
                {
                    Dictionary<string, object> fields = (Dictionary<string, object>)token;
                    foreach (KeyValuePair<string, object> field in fields)
                    {
                        FieldMetaData metaData = new FieldMetaData();
                        metaData.Name = field.Key;

                        // check for array
                        if (field.Value is List<object>)
                        {
                            foreach (object subfield in (List<object>)field.Value)
                            {
                                if (subfield is Dictionary<string, object>)
                                {
                                    dataSetMetaData?.Fields.Add(metaData);
                                    dataValues.AddRange(DecodeField((Dictionary<string, object>)subfield));
                                }
                                else
                                {
                                    dataSetMetaData?.Fields.Add(metaData);
                                    dataValues.Add(new DataValue(new Variant(subfield)));
                                }

                            }
                        }
                        else if (field.Value is Dictionary<string, object>)
                        {
                            dataSetMetaData?.Fields.Add(metaData);
                            dataValues.AddRange(DecodeField((Dictionary<string, object>)field.Value));
                        }
                        else
                        {
                            dataSetMetaData?.Fields.Add(metaData);
                            dataValues.Add(new DataValue(new Variant(field.Value)));
                        }
                    }
                }
            }

            // build the DataSet Fields collection based on the decoded values and the target
            for (int i = 0; i < dataValues.Count; i++)
            {
                Field dataField = new Field();
                dataField.FieldMetaData = dataSetMetaData?.Fields[(int)dataSetMetaData?.Fields.Count - dataValues.Count + i];
                dataField.Value = dataValues[i];

                dataFields.Add(dataField);
            }

            // build the dataset object
            DataSet dataSet = new DataSet(dataSetMetaData?.Name);
            dataSet.DataSetMetaData = dataSetMetaData;
            dataSet.Fields = dataFields.ToArray();
            dataSet.DataSetWriterId = DataSetWriterId;
            dataSet.SequenceNumber = SequenceNumber;

            return dataSet;
        }
        #endregion

        #region Private Encode Methods
        /// <summary>
        /// Encode DataSet message header
        /// </summary>
        private void EncodeDataSetMessageHeader(IEncoder encoder)
        {
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {
                encoder.WriteUInt16(nameof(DataSetWriterId), DataSetWriterId);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt32(nameof(SequenceNumber), SequenceNumber);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                encoder.WriteEncodeable(nameof(MetaDataVersion), MetaDataVersion, typeof(ConfigurationVersionDataType));
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime(nameof(Timestamp), Timestamp);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                encoder.WriteStatusCode(nameof(Status), Status);
            }
        }

        /// <summary>
        /// Encodes The DataSet message payload
        /// </summary>
        internal void EncodePayload(JsonEncoder jsonEncoder, bool pushStructure = true)
        {
            bool forceNamespaceUri = jsonEncoder.ForceNamespaceUri;

            if (pushStructure)
            {
                jsonEncoder.PushStructure(kFieldPayload);
            }

            foreach (var field in DataSet.Fields)
            {
                if (field != null)
                {
                    EncodeField(jsonEncoder, field);
                }
            }

            if (pushStructure)
            {
                jsonEncoder.PopStructure();
            }

            jsonEncoder.ForceNamespaceUri = forceNamespaceUri;
        }

        /// <summary>
        /// Encodes a dataSet field
        /// </summary>
        private void EncodeField(JsonEncoder encoder, Field field)
        {
            string fieldName = field.FieldMetaData.Name;

            Variant valueToEncode = field.Value.WrappedValue;

            // The StatusCode.Good value is not encoded correctly then it shall be committed
            if (valueToEncode == StatusCodes.Good && m_fieldTypeEncoding != FieldTypeEncodingMask.Variant)
            {
                valueToEncode = Variant.Null;
            }

            if (m_fieldTypeEncoding != FieldTypeEncodingMask.DataValue && StatusCode.IsBad(field.Value.StatusCode))
            {
                valueToEncode = field.Value.StatusCode;
            }

            switch (m_fieldTypeEncoding)
            {
                case FieldTypeEncodingMask.Variant:
                    // If the DataSetFieldContentMask results in a Variant representation,
                    // the field value is encoded as a Variant encoded using the reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6.
                    encoder.ForceNamespaceUri = false;
                    encoder.WriteVariant(fieldName, valueToEncode);
                    break;

                case FieldTypeEncodingMask.RawData:
                    // If the DataSetFieldContentMask results in a RawData representation,
                    // the field value is a Variant encoded using the non-reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6
                    encoder.ForceNamespaceUri = true;

                    encoder.WriteVariant(fieldName, valueToEncode);
                    break;

                case FieldTypeEncodingMask.DataValue:
                    DataValue dataValue = new DataValue();

                    dataValue.WrappedValue = valueToEncode;

                    if ((FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
                    {
                        dataValue.StatusCode = field.Value.StatusCode;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
                    {
                        dataValue.SourceTimestamp = field.Value.SourceTimestamp;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
                    {
                        dataValue.SourcePicoseconds = field.Value.SourcePicoseconds;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
                    {
                        dataValue.ServerTimestamp = field.Value.ServerTimestamp;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
                    {
                        dataValue.ServerPicoseconds = field.Value.ServerPicoseconds;
                    }

                    // If the DataSetFieldContentMask results in a DataValue representation,
                    // the field value is a DataValue encoded using the non-reversible OPC UA JSON Data Encoding
                    encoder.ForceNamespaceUri = true;
                    encoder.WriteDataValue(fieldName, dataValue);
                    break;
            }
        }
        #endregion

        #region Private Decode Methods

        /// <summary>
        /// Decode RawData type
        /// </summary>
        /// <returns></returns>
        private object DecodeRawData(JsonDecoder jsonDecoder, FieldMetaData fieldMetaData, string fieldName)
        {
            if (fieldMetaData.BuiltInType != 0)
            {
                try
                {
                    if (fieldMetaData.ValueRank == ValueRanks.Scalar)
                    {
                        return DecodeRawScalar(jsonDecoder, fieldMetaData.BuiltInType, fieldName);
                    }
                    if (fieldMetaData.ValueRank >= ValueRanks.OneDimension)
                    {

                        return jsonDecoder.ReadArray(fieldName, fieldMetaData.ValueRank, (BuiltInType)fieldMetaData.BuiltInType);
                    }
                    else
                    {
                        Log.Logger.Error("JsonDataSetMessage - Decoding ValueRank = {0} not supported yet !!!", fieldMetaData.ValueRank);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "JsonDataSetMessage - Error reading element for RawData.");
                    return (StatusCodes.BadDecodingError);
                }
            }
            return null;
        }

        /// <summary>
        /// Decodes the DataSetMessageHeader
        /// </summary>
        private void DecodeDataSetMessageHeader(JsonDecoder jsonDecoder)
        {
            object token = null;
            if (jsonDecoder.ReadField(nameof(DataSetWriterId), out token))
            {
                DataSetWriterId = jsonDecoder.ReadUInt16(nameof(DataSetWriterId));
            }

            if (jsonDecoder.ReadField(nameof(SequenceNumber), out token))
            {
                SequenceNumber = jsonDecoder.ReadUInt32(nameof(SequenceNumber));
            }

            if (jsonDecoder.ReadField(nameof(MetaDataVersion), out token))
            {
                MetaDataVersion = jsonDecoder.ReadEncodeable(nameof(MetaDataVersion), typeof(ConfigurationVersionDataType)) as ConfigurationVersionDataType;
            }

            if (jsonDecoder.ReadField(nameof(Timestamp), out token))
            {
                Timestamp = jsonDecoder.ReadDateTime(nameof(Timestamp));
            }

            if (jsonDecoder.ReadField(nameof(Status), out token))
            {
                Status = jsonDecoder.ReadStatusCode(nameof(Status));
            }
        }

        /// <summary>
        /// Decode a scalar type
        /// </summary>
        private object DecodeRawScalar(JsonDecoder jsonDecoder, byte builtInType, string fieldName)
        {
            try
            {
                switch ((BuiltInType)builtInType)
                {
                    case BuiltInType.Boolean:
                        return jsonDecoder.ReadBoolean(fieldName);
                    case BuiltInType.SByte:
                        return jsonDecoder.ReadSByte(fieldName);
                    case BuiltInType.Byte:
                        return jsonDecoder.ReadByte(fieldName);
                    case BuiltInType.Int16:
                        return jsonDecoder.ReadInt16(fieldName);
                    case BuiltInType.UInt16:
                        return jsonDecoder.ReadUInt16(fieldName);
                    case BuiltInType.Int32:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.UInt32:
                        return jsonDecoder.ReadUInt32(fieldName);
                    case BuiltInType.Int64:
                        return jsonDecoder.ReadInt64(fieldName);
                    case BuiltInType.UInt64:
                        return jsonDecoder.ReadUInt64(fieldName);
                    case BuiltInType.Float:
                        return jsonDecoder.ReadFloat(fieldName);
                    case BuiltInType.Double:
                        return jsonDecoder.ReadDouble(fieldName);
                    case BuiltInType.String:
                        return jsonDecoder.ReadString(fieldName);
                    case BuiltInType.DateTime:
                        return jsonDecoder.ReadDateTime(fieldName);
                    case BuiltInType.Guid:
                        return jsonDecoder.ReadGuid(fieldName);
                    case BuiltInType.ByteString:
                        return jsonDecoder.ReadByteString(fieldName);
                    case BuiltInType.XmlElement:
                        return jsonDecoder.ReadXmlElement(fieldName);
                    case BuiltInType.NodeId:
                        return jsonDecoder.ReadNodeId(fieldName);
                    case BuiltInType.ExpandedNodeId:
                        return jsonDecoder.ReadExpandedNodeId(fieldName);
                    case BuiltInType.QualifiedName:
                        return jsonDecoder.ReadQualifiedName(fieldName);
                    case BuiltInType.LocalizedText:
                        return jsonDecoder.ReadLocalizedText(fieldName);
                    case BuiltInType.DataValue:
                        return jsonDecoder.ReadDataValue(fieldName);
                    case BuiltInType.Enumeration:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.Variant:
                        return jsonDecoder.ReadVariant(fieldName);
                    case BuiltInType.ExtensionObject:
                        return jsonDecoder.ReadExtensionObject(fieldName);
                    case BuiltInType.DiagnosticInfo:
                        return jsonDecoder.ReadDiagnosticInfo(fieldName);
                    case BuiltInType.StatusCode:
                        return jsonDecoder.ReadStatusCode(fieldName);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "JsonDataSetMessage - Error decoding field {0}", fieldName);
            }

            return null;
        }
        #endregion
    }
}
