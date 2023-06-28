namespace Opc.Ua.Cloud.Publisher.Models
{
    public class UANodeInformation
    {
        public string ApplicationUri { get; set; }

        public string ExpandedNodeId { get; set; }

        public string DisplayName { get; internal set; }

        public string Type { get; set; }
    }
}