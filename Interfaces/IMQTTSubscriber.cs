
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IMQTTSubscriber
    {
        void Connect();

        void Publish(byte[] payload);

        void PublishMetadata(byte[] metadata);
    }
}