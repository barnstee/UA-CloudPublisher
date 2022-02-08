
namespace UA.MQTT.Publisher.Interfaces
{
    public interface IMessageSink
    {
        void SendMessage(byte[] message);
    }
}
