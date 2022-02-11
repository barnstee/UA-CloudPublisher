
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IMessagePublisher
    {
        void SendMessage(byte[] message);
    }
}
