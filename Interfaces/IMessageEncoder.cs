
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Cloud.Publisher.Models;
    using System.Collections.Generic;

    public interface IMessageEncoder
    {
        string EncodeHeader(ulong messageID, bool isMetadata = false);

        string EncodePayload(MessageProcessorModel messageData, out ushort hash);

        string EncodeMetadata(MessageProcessorModel messageData);

        string EncodeCloudEventMetadata(MessageProcessorModel messageData);

        IReadOnlyDictionary<string, string> BuildCloudEventMetadataAttributes(ulong messageId, ushort dataSetWriterId);

        string EncodeStatus(ulong messageID);

        string EncodeConnection(ulong messageID, IReadOnlyDictionary<ushort, string> dataSetWriters);
    }
}