using System.Threading.Tasks;

namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IBrokerClient
    {
        void Connect(bool altBroker = false);

        Task PublishAsync(byte[] payload);

        Task PublishMetadataAsync(byte[] metadata);
    }
}