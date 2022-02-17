
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IMessagePublisher
    {
        bool SendMessage(byte[] message);
    }
}
