
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;

    public interface IMessageEncoder
    {
        /// <summary>
        /// Encode a data change
        /// </summary>
        /// <param name="messageData"></param>
        /// <returns></returns>
        string EncodeDataChange(MessageDataModel messageData);

        /// <summary>
        /// Encode an event
        /// </summary>
        /// <param name="eventData"></param>
        /// <returns></returns>
        string EncodeEvent(EventMessageDataModel eventData);
    }
}