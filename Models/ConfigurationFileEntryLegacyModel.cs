
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using Opc.Ua;
    using UA.MQTT.Publisher;
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    // Class describing the nodes which should be published. It supports two formats:
    // - NodeId syntax using the namespace index (ns) syntax. This is only used in legacy environments and is only supported for backward compatibility.
    // - List of ExpandedNodeId syntax, to allow putting nodes with similar publishing and/or sampling intervals in one object
    public partial class ConfigurationFileEntryLegacyModel
    {
        public Uri EndpointUrl { get; set; }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        public bool UseSecurity { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public NodeId NodeId { get; set; }

        [DefaultValue(OpcSessionUserAuthenticationMode.Anonymous)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public OpcSessionUserAuthenticationMode OpcAuthenticationMode { get; set; }

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

        [JsonIgnore]
        public EncryptedCredentials EncryptedAuthCredential { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<OpcNodeOnEndpointModel> OpcNodes { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<OpcEventOnEndpointModel> OpcEvents { get; set; }
    }
}
