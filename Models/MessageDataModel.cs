
namespace UA.MQTT.Publisher.Models
{
    using Opc.Ua;

    /// <summary>
    /// Class used to pass data from the MonitoredItem notification to the hub message processing.
    /// </summary>
    public class MessageDataModel
    {
        /// <summary>
        /// The endpoint URL the monitored item belongs to.
        /// </summary>
        public string EndpointUrl { get; set; }

        /// <summary>
        /// The OPC UA Node Id with the namespace expanded.
        /// </summary>
        public string ExpandedNodeId { get; set; }

        /// <summary>
        /// The Application URI of the OPC UA server the node belongs to.
        /// </summary>
        public string ApplicationUri { get; set; }

        /// <summary>
        /// The display name of the node.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The DataSetWriterId must be unique within the scope of the UA-MQTT-Publisher
        /// We use the applicationURI, which is supposed to be globally unique, plus the subscription ID
        /// </summary>
        public string DataSetWriterId { get; set; }

        /// <summary>
        /// The value of the node.
        /// </summary>
        public DataValue Value { get; set; }

        /// <summary>
        /// Message Context needed for encoding the message
        /// </summary>
        public IServiceMessageContext MessageContext { get; set; }

        /// <summary>
        /// Ctor of the object.
        /// </summary>
        public MessageDataModel()
        {
            EndpointUrl = null;
            ExpandedNodeId = null;
            ApplicationUri = null;
            DataSetWriterId = null;
            DisplayName = null;
            Value = null;
            MessageContext = null;
        }
    }
}
