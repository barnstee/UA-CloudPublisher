using System.Collections.Generic;

namespace Opc.Ua.Cloud.Publisher.OPC
{
    public class TypeDictionaries
    {
        public Dictionary<GeneratedDataTypeDefinition, GeneratedDataClass> generatedDataTypes = new Dictionary<GeneratedDataTypeDefinition, GeneratedDataClass>();
        private Dictionary<NodeId, Node> opcBinary = new Dictionary<NodeId, Node>(new NodeIdComparer());
        private Dictionary<NodeId, Node> dataTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> eventTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> interfaceTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> objectTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> referenceTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> variableTypes = new Dictionary<NodeId, Node>();
        private Dictionary<NodeId, Node> xmlSchema = new Dictionary<NodeId, Node>();
        public List<DataTypeDefinition> dataTypeDefinition = new List<DataTypeDefinition>();

        public TypeDictionaries()
        {

        }
        public void SetOpcBinaryTypes(Dictionary<NodeId, Node> opcBinary)
        {
            this.opcBinary.Clear();
            if (opcBinary != null)
            {
                this.opcBinary = opcBinary;
            }
        }
        public Dictionary<NodeId, Node> GetOpcBinary()
        {
            return this.opcBinary;
        }
        public void SetDataTypes(Dictionary<NodeId, Node> dataTypes)
        {
            this.dataTypes.Clear();
            if (dataTypes != null)
            {
                this.dataTypes = dataTypes;
            }
        }
        public Dictionary<NodeId, Node> GetDataTypes()
        {
            return this.dataTypes;
        }
        public void SetEventTypes(Dictionary<NodeId, Node> eventTypes)
        {
            this.eventTypes.Clear();
            if (eventTypes != null)
            {
                this.eventTypes = eventTypes;
            }
        }
        public Dictionary<NodeId, Node> GetEventTypes()
        {
            return this.eventTypes;
        }
        public void SetInterfaceTypes(Dictionary<NodeId, Node> interfaceTypes)
        {
            this.interfaceTypes.Clear();
            if (interfaceTypes != null)
            {
                this.interfaceTypes = interfaceTypes;
            }
        }
        public Dictionary<NodeId, Node> GetInterfaceTypes()
        {
            return this.interfaceTypes;
        }
        public void SetObjectTypes(Dictionary<NodeId, Node> objectTypes)
        {
            this.objectTypes.Clear();
            if (interfaceTypes != null)
            {
                this.objectTypes = objectTypes;
            }
        }
        public Dictionary<NodeId, Node> GetObjectTypes()
        {
            return this.objectTypes;
        }
        public void SetReferenceTypes(Dictionary<NodeId, Node> referenceTypes)
        {
            this.referenceTypes.Clear();
            if (referenceTypes != null)
            {
                this.referenceTypes = referenceTypes;
            }
        }
        public Dictionary<NodeId, Node> GetReferenceTypes()
        {
            return this.referenceTypes;
        }
        public void SetVariableTypes(Dictionary<NodeId, Node> variableTypes)
        {
            this.variableTypes.Clear();
            if (variableTypes != null)
            {
                this.variableTypes = variableTypes;
            }
        }
        public Dictionary<NodeId, Node> GetVariableTypes()
        {
            return this.variableTypes;
        }
        public Node? FindBinaryEncodingType(NodeId nodeId)
        {
            Node? encodingType = null;
            encodingType = this.opcBinary[nodeId];
            return encodingType;
        }
    }
    public class NodeIdComparer : IEqualityComparer<NodeId>
    {
        public bool Equals(NodeId? n1, NodeId? n2)
        {
            if (n1 == n2)
            {
                return true;
            }
            if (n1 == null || n2 == null)
            {
                return false;
            }
            return (n1.Identifier == n2.Identifier && n1.NamespaceIndex == n2.NamespaceIndex);
        }
        public int GetHashCode(NodeId n1)
        {
            return n1.Identifier.GetHashCode() + n1.NamespaceIndex.GetHashCode();
        }
    }
    public class GeneratedDataTypeDefinition
    {
        private string nameSpace;
        private string name;
        public GeneratedDataTypeDefinition(string nameSpace, string name)
        {
            this.nameSpace = nameSpace;
            this.name = name;
        }
    }
    public class GeneratedField
    {
        public string Name = "";
        public string TypeName = "";
        public GeneratedField()
        {

        }
    }
    public class GeneratedDataClass
    {
        public string Name = "";
        public GeneratedDataClass()
        {
        }
    }
    public class GeneratedStructure : GeneratedDataClass
    {
        public string Documentation = "";
        public string? BaseType = null;
        public List<GeneratedField> fields = new List<GeneratedField>();
        public GeneratedStructure()
        {

        }
    }
    public class GeneratedEnumeratedType : GeneratedDataClass
    {
        public string Documentation = "";
        public GeneratedEnumeratedType()
        {

        }
    }
    public class GeneratedOpaqueType : GeneratedDataClass
    {
        public string Documentation = "";
        public GeneratedOpaqueType()
        {

        }
    }
    public enum GeneratedComplexTypes
    {
        StructuredType,
        EnumeratedType,
        OpaqueType
    }

    public class StructuredNode
    {
        public NodeId nodeId;
        public QualifiedName browsename;
        public List<StructuredNode> childNodes = new List<StructuredNode>();
        public string? placeholderTypeDefinition = null;
        public Dictionary<string, List<PlaceHolderNode>> placeholderNodes = new Dictionary<string, List<PlaceHolderNode>>();
        public StructuredNode(QualifiedName browseName, NodeId nodeId)
        {
            this.browsename = browseName;
            this.nodeId = nodeId;
        }
    }
    public class PlaceHolderNode
    {
        public QualifiedName browseName;
        public NodeId nodeId;
        public string typeDefinition;
        public List<StructuredNode> childNodes = new List<StructuredNode>();
        public PlaceHolderNode(QualifiedName browseName, NodeId nodeId, string typeDefinition)
        {
            this.browseName = browseName;
            this.nodeId = nodeId;
            this.typeDefinition = typeDefinition;
        }
    }
}
