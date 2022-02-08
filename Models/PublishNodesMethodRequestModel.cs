
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.Collections.Generic;

    /// <summary>
    /// Model for a publish node request.
    /// </summary>
    public class PublishNodesMethodRequestModel
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="endpointUrl">OPC UA Endpoint URL</param>
        /// <param name="useSecurity">Flag to indicate if OPC UA secure channel communication should be used</param>
        /// <param name="userName">Username for user authentication</param>
        /// <param name="password">Password for user authentication</param>
        public PublishNodesMethodRequestModel(string endpointUrl, bool useSecurity = true, string userName = null, string password = null)
        {
            OpcNodes = new List<OpcNodeOnEndpointModel>();
            EndpointUrl = endpointUrl;
            UseSecurity = useSecurity;
            UserName = userName;
            Password = password;
        }

        /// <summary>
        /// OPC UA Endpoint URL
        /// </summary>
        public string EndpointUrl { get; set; }

        /// <summary>
        /// List of OPC UA nodes to publish
        /// </summary>
        public List<OpcNodeOnEndpointModel> OpcNodes { get; }

        /// <summary>
        /// OPC UA user authentication mode to use
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public OpcSessionUserAuthenticationMode? OpcAuthenticationMode { get; set; }

        /// <summary>
        /// Flag to indicate if OPC UA secure channel communication should be used
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool UseSecurity { get; set; }

        /// <summary>
        /// Username for user authentication
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string UserName { get; set; }

        /// <summary>
        /// Password for user authentication
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Password { get; set; }
    }
}
