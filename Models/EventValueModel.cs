
namespace UA.MQTT.Publisher.Models
{
    using Opc.Ua;

    public class EventValueModel
    {
        public string Name { get; set; }

        public DataValue Value { get; set; }
    }
}
