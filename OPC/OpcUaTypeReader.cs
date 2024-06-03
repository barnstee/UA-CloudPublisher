using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Cloud.Publisher.OPC
{
    public class OpcUaTypeReader
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        private ILogger logger; 
        private List<string> errorMemmory = new List<string>();
        private TypeDictionaries typeDictionaries;
        private OpcUaClient client;
        public OpcUaTypeReader(OpcUaClient opcUaClient) {
            this.client = opcUaClient;
            logger = this.loggerFactory.CreateLogger<OpcUaTypeReader>();
        }
        public void ReadTypeDictionary()
        {
            this.ReadOpcBinary();
            this.ReadDataTypes();
            this.ReadEventTypes();
            this.ReadInterfaceTypes();
            this.ReadObjectTypes();
            this.ReadReferenceTypes();
            this.ReadVariableTypes();
            Console.WriteLine("TypeDictionary Read Finished");
        }
        private void ReadOpcBinary()
        {
            List<NodeId> binaryTypeDictionaries = new List<NodeId>();
            binaryTypeDictionaries = this.client.BrowseLocalNodeIdsWithTypeDefinition(ObjectIds.OPCBinarySchema_TypeSystem, BrowseDirection.Forward, (uint)NodeClass.Variable, ReferenceTypeIds.HasComponent, false, VariableTypeIds.DataTypeDictionaryType);
            foreach (NodeId binaryTypeDictionary in binaryTypeDictionaries)
            {
                DataValue dv = this.client.ReadValue(binaryTypeDictionary);
                string xmlString = Encoding.UTF8.GetString((byte[])dv.Value);
                //Console.WriteLine(xmlString);
                this.generateDataClasses(xmlString);
            };
            List<NodeId> opcBinaryNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(ObjectIds.OPCBinarySchema_TypeSystem, NodeClass.Variable, opcBinaryNodeIds, ReferenceTypeIds.HasComponent);
            this.ReadAndAppendTypeNodeIds(ObjectIds.OPCBinarySchema_TypeSystem, NodeClass.Variable, opcBinaryNodeIds, ReferenceTypeIds.HasProperty);
            Dictionary<NodeId, Node> opcBinaryTypes = new Dictionary<NodeId, Node>();
            //opcBinaryNodeIds = opcBinaryNodeIds.Distinct().ToList();
            foreach (NodeId opcBinaryNodeId in opcBinaryNodeIds)
            {
                Node? node = this.client.ReadNode(opcBinaryNodeId);
                if (node != null)
                {
                    opcBinaryTypes.Add(opcBinaryNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", opcBinaryNodeId);
                }
            }
            this.typeDictionaries.SetOpcBinaryTypes(opcBinaryTypes);
        }
        private void generateDataClasses(string xmlString)
        {
            Console.Out.WriteLine(xmlString);
            XmlTextReader reader = new XmlTextReader(new System.IO.StringReader(xmlString));
            GeneratedStructure generatedStructure = new GeneratedStructure();
            GeneratedEnumeratedType generatedEnumeratedType = new GeneratedEnumeratedType();
            GeneratedOpaqueType generatedOpaqueType = new GeneratedOpaqueType();
            //Structure or enumerated Type
            GeneratedComplexTypes generatedComplexType = GeneratedComplexTypes.StructuredType;
            string? Name = null;
            string? BaseType = null;
            string documentation = "";
            string? targetNamespace = null;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        string nodeElement = reader.Name;
                        switch (nodeElement)
                        {
                            case ("opc:StructuredType"):
                                generatedComplexType = GeneratedComplexTypes.StructuredType;
                                generatedStructure = new GeneratedStructure();
                                Name = reader.GetAttribute("Name");
                                BaseType = reader.GetAttribute("BaseType");
                                generatedStructure.BaseType = BaseType;
                                if (Name != null)
                                {
                                    generatedStructure.Name = Name;
                                }
                                else
                                {
                                    this.errorMemmory.Add("The Name of the structure is null");
                                }
                                break;
                            case ("opc:Documentation"):
                                documentation = reader.ReadInnerXml();
                                if (generatedComplexType == GeneratedComplexTypes.StructuredType)
                                {
                                    generatedStructure.Documentation = documentation;
                                }
                                else if (generatedComplexType == GeneratedComplexTypes.EnumeratedType)
                                {
                                    generatedEnumeratedType.Documentation = documentation;
                                }
                                else if (generatedComplexType == GeneratedComplexTypes.OpaqueType)
                                {

                                }
                                break;
                            case ("opc:Field"):
                                GeneratedField generatedField = new GeneratedField();
                                string? typeName = reader.GetAttribute("TypeName");
                                if (typeName != null)
                                {
                                    generatedField.TypeName = typeName;
                                }
                                else
                                {
                                    this.errorMemmory.Add("The TypeName of the Field is null");
                                }
                                string? fieldname = reader.GetAttribute("Name");
                                if (fieldname != null)
                                {
                                    generatedField.Name = fieldname;
                                }
                                else
                                {
                                    this.errorMemmory.Add("The Name of the Field is null");
                                }
                                if (generatedComplexType == GeneratedComplexTypes.StructuredType)
                                {
                                    generatedStructure.fields.Add(generatedField);
                                }
                                else
                                {
                                    this.errorMemmory.Add("Trying to add a field to a non Structure.");
                                }
                                break;
                            case ("opc:EnumeratedType"):
                                generatedComplexType = GeneratedComplexTypes.EnumeratedType;
                                generatedEnumeratedType = new GeneratedEnumeratedType();
                                Name = reader.GetAttribute("Name");
                                if (Name != null)
                                {
                                    generatedEnumeratedType.Name = Name;
                                }
                                else
                                {
                                    this.errorMemmory.Add("The Name of the structure is null");
                                }
                                break;
                            case ("opc:EnumeratedValue"):
                                break;
                            case ("opc:OpaqueType"):
                                generatedComplexType = GeneratedComplexTypes.OpaqueType;
                                break;
                            case ("opc:TypeDictionary"):
                                targetNamespace = reader.GetAttribute("TargetNamespace");
                                if (targetNamespace == null)
                                {
                                    this.errorMemmory.Add("The TargetNameSpace for the Typedictionary is null.");
                                }
                                break;
                            case ("opc:Import"):
                                break;
                            default:
                                Console.WriteLine("UnknownType: -> ##################" + "###" + reader.Name + "###");
                                break;
                        }
                        //Console.WriteLine("###" + reader.Name + "###");

                        break;
                    case XmlNodeType.Text:
                        break;
                    case XmlNodeType.EndElement:
                        nodeElement = reader.Name;
                        switch (nodeElement)
                        {
                            case ("opc:StructuredType"):
                                if (targetNamespace != null)
                                {
                                    this.typeDictionaries.generatedDataTypes.Add(new GeneratedDataTypeDefinition(targetNamespace, generatedStructure.Name), generatedStructure);
                                }
                                break;
                        }
                        break;
                }
            }
            foreach (string error in this.errorMemmory)
            {
                logger.LogError(error);
            }
        }
        private void ReadDataTypes()
        {
            List<NodeId> dataTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(DataTypeIds.BaseDataType, NodeClass.DataType, dataTypeNodeIds);
            Dictionary<NodeId, Node> dataTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId dataTypeNodeId in dataTypeNodeIds)
            {
                Node? node = this.client.ReadNode(dataTypeNodeId);
                if (node != null)
                {
                    dataTypes.Add(dataTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", dataTypeNodeId);
                }
            }
            this.typeDictionaries.SetDataTypes(dataTypes);
        }
        private void ReadEventTypes()
        {
            List<NodeId> eventTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(ObjectTypeIds.BaseEventType, NodeClass.ObjectType, eventTypeNodeIds);
            Dictionary<NodeId, Node> eventTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId eventTypeNodeId in eventTypeNodeIds)
            {
                Node? node = this.client.ReadNode(eventTypeNodeId);
                if (node != null)
                {
                    eventTypes.Add(eventTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", eventTypeNodeId);
                }
            }
            this.typeDictionaries.SetEventTypes(eventTypes);
        }
        private void ReadInterfaceTypes()
        {
            List<NodeId> interfaceTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(ObjectTypeIds.BaseInterfaceType, NodeClass.ObjectType, interfaceTypeNodeIds);
            Dictionary<NodeId, Node> interfaceTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId interfaceTypeNodeId in interfaceTypeNodeIds)
            {
                Node? node = this.client.ReadNode(interfaceTypeNodeId);
                if (node != null)
                {
                    interfaceTypes.Add(interfaceTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", interfaceTypeNodeId);
                }
            }
            this.typeDictionaries.SetInterfaceTypes(interfaceTypes);
        }

        private void ReadObjectTypes()
        {
            List<NodeId> objectTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(ObjectTypeIds.BaseObjectType, NodeClass.ObjectType, objectTypeNodeIds);
            Dictionary<NodeId, Node> objectTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId objectTypeNodeId in objectTypeNodeIds)
            {
                Node? node = this.client.ReadNode(objectTypeNodeId);
                if (node != null)
                {
                    objectTypes.Add(objectTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", objectTypeNodeId);
                }
            }
            this.typeDictionaries.SetObjectTypes(objectTypes);

        }
        private void ReadReferenceTypes()
        {
            List<NodeId> referenceTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(ReferenceTypeIds.References, NodeClass.ReferenceType, referenceTypeNodeIds);
            Dictionary<NodeId, Node> referenceTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId referenceTypeNodeId in referenceTypeNodeIds)
            {
                Node? node = this.client.ReadNode(referenceTypeNodeId);
                if (node != null)
                {
                    referenceTypes.Add(referenceTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", referenceTypeNodeId);
                }
            }
            this.typeDictionaries.SetReferenceTypes(referenceTypes);
        }

        private void ReadVariableTypes()
        {
            List<NodeId> variableTypeNodeIds = new List<NodeId>();
            this.ReadAndAppendTypeNodeIds(VariableTypeIds.BaseVariableType, NodeClass.VariableType, variableTypeNodeIds);
            Dictionary<NodeId, Node> variableTypes = new Dictionary<NodeId, Node>();
            foreach (NodeId variableTypeNodeId in variableTypeNodeIds)
            {
                Node? node = this.client.ReadNode(variableTypeNodeId);
                if (node != null)
                {
                    variableTypes.Add(variableTypeNodeId, node);
                }
                else
                {
                    Console.WriteLine("Error Reading Node for NodeId:", variableTypeNodeId);
                }
            }
            this.typeDictionaries.SetVariableTypes(variableTypes);
        }
        private void ReadAndAppendTypeNodeIds(NodeId nodeId, NodeClass nodeClass, List<NodeId> nodeIds)
        {
            nodeIds.Add(nodeId);
            List<NodeId> subTypeNodeIds = this.client.BrowseLocalNodeIds(nodeId, BrowseDirection.Forward, (uint)nodeClass, ReferenceTypeIds.HasSubtype, true);
            foreach (NodeId subTypeNodeId in subTypeNodeIds)
            {
                this.ReadAndAppendTypeNodeIds(subTypeNodeId, nodeClass, nodeIds);
            }
        }
        private void ReadAndAppendTypeNodeIds(NodeId nodeId, NodeClass nodeClass, List<NodeId> nodeIds, NodeId referenceTypeId)
        {
            nodeIds.Add(nodeId);
            List<NodeId> subTypeNodeIds = this.client.BrowseLocalNodeIds(nodeId, BrowseDirection.Forward, (uint)nodeClass, referenceTypeId, true);
            foreach (NodeId subTypeNodeId in subTypeNodeIds)
            {
                this.ReadAndAppendTypeNodeIds(subTypeNodeId, nodeClass, nodeIds, referenceTypeId);
            }
        }
    }
}
