
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IMessagePublisher
    {
        bool SendMessage(byte[] message);

        bool SendMetadata(byte[] metadata);

        void ApplyNewClient(IBrokerClient client);
    }
}
