
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IMQTTSubscriber
    {
        void Publish(byte[] payload);
    }
}