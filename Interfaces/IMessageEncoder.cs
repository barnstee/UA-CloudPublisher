
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;

    public interface IMessageEncoder
    {
        string Encode(MessageProcessorModel messageData);
    }
}