
namespace UA.MQTT.Publisher.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Class used to pass data from the Event MonitoredItem event notification to the hub message processing.
    /// </summary>
    public class EventMessageDataModel : MessageDataModel
    {
        /// <summary>
        /// The value of the node.
        /// </summary>
        public List<EventValueModel> EventValues { get; set; }

        /// <summary>
        /// Ctor of the object.
        /// </summary>
        public EventMessageDataModel()
        {
            EventValues = new List<EventValueModel>();
        }
    }
}
