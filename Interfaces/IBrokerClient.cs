using System.Threading.Tasks;

namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IBrokerClient
    {
        Task ConnectAsync(bool altBroker = false);

        Task PublishAsync(byte[] payload);

        Task PublishMetadataAsync(byte[] metadata);
    }
}