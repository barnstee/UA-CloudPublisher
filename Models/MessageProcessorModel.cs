
namespace Opc.Ua.Cloud.Publisher.Models
{
    using Opc.Ua;
    using System.Collections.Generic;

    public class MessageProcessorModel
    {
        public string ExpandedNodeId { get; set; }

        public string ApplicationUri { get; set; }

        public string Name { get; set; }

        public DataValue Value { get; set; }

        public IServiceMessageContext MessageContext { get; set; }

        public List<EventValueModel> EventValues { get; set; } = new List<EventValueModel>();
    }
}
