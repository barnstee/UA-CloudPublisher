
namespace UA.MQTT.Publisher.Models
{
    /// <summary>
    /// Model for an unpublish all nodes request.
    /// </summary>
    public class UnpublishAllNodesMethodRequestModel
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="endpointUrl">The OPC UA Endpoint Url (optional)</param>
        public UnpublishAllNodesMethodRequestModel(string endpointUrl = null)
        {
            EndpointUrl = endpointUrl;
        }

        /// <summary>
        /// The OPC UA Endpoint Url
        /// </summary>
        public string EndpointUrl { get; set; }
    }
}
