namespace Opc.Ua.Cloud.Publisher.Models
{
    public class UANodeInformation
    {
        public string Endpoint { get; set; }

        public string ApplicationUri { get; set; }

        public string ExpandedNodeId { get; set; }

        public string DisplayName { get; internal set; }

        public string Type { get; set; }

        public string VariableCurrentValue { get; set; } = string.Empty;

        public string VariableType { get; set; } = string.Empty;

        public string Parent { get; set; } = string.Empty;

        public string[] References { get; set; }

        // Typed OPC UA attributes captured during browsing so a NodeSet2 file can be built directly, without re-reading each node.
        public NodeId NodeId { get; set; }

        public VariableNode VariableNode { get; set; }

        public object VariableValue { get; set; }

        // Built-in base DataType of the variable's actual value (a standard ns=0 DataType), used so the exported
        // NodeSet2 references only resolvable DataTypes even when the server's DataType is custom or unknown.
        public NodeId VariableDataTypeId { get; set; }
    }
}