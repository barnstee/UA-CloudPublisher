
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IBrokerClient
    {
        void Connect();

        void Publish(byte[] payload);

        void PublishMetadata(byte[] metadata);
    }
}