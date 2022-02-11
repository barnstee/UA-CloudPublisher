
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using UA.MQTT.Publisher;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    /// <summary>
    /// Class describing the nodes which should be published.
    /// </summary>
    public partial class ConfigurationFileEntryModel
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public ConfigurationFileEntryModel()
        {
            // nothing to do
        }

        /// <summary>
        /// Constructor using the specified endpoint
        /// </summary>
        /// <param name="endpointUrl"></param>
        public ConfigurationFileEntryModel(string endpointUrl)
        {
            EndpointUrl = new Uri(endpointUrl);
            OpcNodes = new List<OpcNodeOnEndpointModel>();
        }

        /// <summary>
        /// The endpoint URL used
        /// </summary>
        public Uri EndpointUrl { get; set; }

        /// <summary>
        /// Flag to indicate if security is used
        /// </summary>
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public bool? UseSecurity { get; set; }

        /// <summary>
        /// Gets ot sets the authentication mode to authenticate against the OPC UA Server.
        /// </summary>
        [DefaultValue(OpcSessionUserAuthenticationMode.Anonymous)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public OpcSessionUserAuthenticationMode OpcAuthenticationMode { get; set; }

        /// <summary>
        /// Gets or sets the encrypted username to authenticate against the OPC UA Server (when OpcAuthenticationMode is set to UsernamePassword
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public string EncryptedAuthUsername
        {
            get
            {
                return EncryptedAuthCredential?.UserName;
            }
            set
            {
                if (EncryptedAuthCredential == null)
                {
                    EncryptedAuthCredential = new EncryptedCredentials(null, null);
                }

                EncryptedAuthCredential.UserName = value;
            }
        }

        /// <summary>
        /// Gets or sets the encrypted password to authenticate against the OPC UA Server (when OpcAuthenticationMode is set to UsernamePassword
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public string EncryptedAuthPassword
        {
            get
            {
                return EncryptedAuthCredential?.Password;
            }
            set
            {
                if (EncryptedAuthCredential == null)
                {
                    EncryptedAuthCredential = new EncryptedCredentials(null, null);
                }

                EncryptedAuthCredential.Password = value;
            }
        }

        /// <summary>
        /// Gets or sets the encrpyted credential to authenticate against the OPC UA Server (when OpcAuthenticationMode is set to UsernamePassword.
        /// </summary>
        [JsonIgnore]
        public EncryptedCredentials EncryptedAuthCredential { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<OpcNodeOnEndpointModel> OpcNodes { get; set; }


        /// <summary>
        /// Event and Conditions are defined here.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<OpcEventOnEndpointModel> OpcEvents { get; set; }
    }
}
