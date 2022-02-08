
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Model for an unpublish node request.
    /// </summary>
    public class UnpublishNodesMethodRequestModel
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="endpointUrl">The OPC UA Endpoint Url</param>
        public UnpublishNodesMethodRequestModel(string endpointUrl)
        {
            OpcNodes = new List<OpcNodeOnEndpointModel>();
            EndpointUrl = endpointUrl;
        }

        /// <summary>
        /// The OPC UA Endpoint Url
        /// </summary>
        [JsonRequired]
        public string EndpointUrl { get; set; }

        /// <summary>
        /// The list of OPC UA nodes to unpublish
        /// </summary>
        public List<OpcNodeOnEndpointModel> OpcNodes { get; }
    }
}
