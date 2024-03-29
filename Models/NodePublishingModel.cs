﻿
namespace Opc.Ua.Cloud.Publisher.Models
{
    using Opc.Ua;
    using System.Collections.Generic;

    public class NodePublishingModel
    {
        public string EndpointUrl { get; set; }

        public ExpandedNodeId ExpandedNodeId { get; set; }

        public int OpcSamplingInterval { get; set; }

        public int OpcPublishingInterval { get; set; }

        public int HeartbeatInterval { get; set; }

        public bool SkipFirst { get; set; }

        public UserAuthModeEnum OpcAuthenticationMode { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public List<FilterModel> Filter { get; set; }
    }
}
