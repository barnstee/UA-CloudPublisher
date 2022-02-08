
namespace UA.MQTT.Publisher.Models
{
    using Opc.Ua;

    /// <summary>
    /// Class used to pass key/value pairs of event field data from the MonitoredItem event notification to the hub message processing.
    /// </summary>
    public class EventValueModel
    {
        /// <summary>
        /// Ctor of the class
        /// </summary>
        public EventValueModel()
        {
            Name = string.Empty;
            Value = null;
        }

        /// <summary>
        /// The name of the field.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The value of the field
        /// </summary>
        public DataValue Value { get; set; }
    }
}
