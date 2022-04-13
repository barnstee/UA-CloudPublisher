
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Cloud.Publisher.Models;

    public interface IMessageEncoder
    {
        string EncodeHeader(ulong messageID, bool isMetadata = false);

        string EncodePayload(MessageProcessorModel messageData, out ushort hash);

        string EncodeMetadata(MessageProcessorModel messageData);
    }
}