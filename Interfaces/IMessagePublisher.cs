using System.Collections.Generic;
using System.Threading.Tasks;

namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IMessagePublisher
    {
        Task<bool> SendMessageAsync(byte[] message);

        Task<bool> SendMetadataAsync(byte[] metadata, IReadOnlyDictionary<string, string> cloudEventAttributes = null);

        void ApplyNewClient(IBrokerClient client);

        void ApplyAltClient(IBrokerClient altClient);
    }
}
