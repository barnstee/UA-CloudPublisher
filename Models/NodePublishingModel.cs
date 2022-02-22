
namespace UA.MQTT.Publisher.Models
{
    using Opc.Ua;
    using System.Collections.Generic;
    using System.Net;

    public class NodePublishingModel
    {
        public string EndpointUrl { get; set; }

        public bool UseSecurity { get; set; }

        public ExpandedNodeId ExpandedNodeId { get; set; }

        public string DisplayName { get; set; }

        public int OpcSamplingInterval { get; set; }

        public int OpcPublishingInterval { get; set; }

        public int HeartbeatInterval { get; set; }

        public bool SkipFirst { get; set; }

        public OpcSessionUserAuthenticationMode OpcAuthenticationMode { get; set; }

        public NetworkCredential AuthCredential { get; set; }

        public string DataSetFieldId { get; set; }

        public string DataSetWriterId { get; set; }

        public List<SelectClauseModel> SelectClauses { get; set; }

        public List<WhereClauseElementModel> WhereClauses { get; set; }
    }
}
