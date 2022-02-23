
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;

    public interface IMessageEncoder
    {
        string EncodeHeader(ulong messageID);

        string EncodePayload(MessageProcessorModel messageData);
    }
}