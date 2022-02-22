
namespace UA.MQTT.Publisher.Models
{
    using Opc.Ua;
    using System.Collections.Generic;

    public class MessageProcessorModel
    {
        public string EndpointUrl { get; set; }

        public string ExpandedNodeId { get; set; }

        public string ApplicationUri { get; set; }

        public string DisplayName { get; set; }

        public string DataSetWriterId { get; set; }

        public DataValue Value { get; set; }

        public IServiceMessageContext MessageContext { get; set; }

        public List<EventValueModel> EventValues { get; set; } = new List<EventValueModel>();
    }
}
