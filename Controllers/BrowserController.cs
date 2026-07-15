
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading.Tasks;

    public class BrowserController : Controller
    {
        private readonly IUAApplication _app;
        private readonly IUAClient _client;

        private SessionModel _session;

        public BrowserController(IUAApplication app, IUAClient client)
        {
            _app = app;
            _client = client;
            _session = new();
        }

        [HttpPost]
        public IActionResult DownloadUACert()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            try
            {
                return File(_app.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.Export(X509ContentType.Cert), "APPLICATION/octet-stream", "cert.der");
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                return View("Index", _session);
            }
        }

        [HttpGet]
        public ActionResult Index()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            if (!string.IsNullOrEmpty(_session.EndpointUrl))
            {
                return View("Browse", _session);
            }

            return View("Index", _session);
        }

        [HttpPost]
        public ActionResult UserPassword(string endpointUrl)
        {
            if (string.IsNullOrEmpty(endpointUrl) || !endpointUrl.StartsWith("opc.tcp://"))
            {
                _session.StatusMessage = "Please provide a valid OPC UA endpoint URL in the address format opc.tcp://ipaddress:port";
                return View("Index", _session);
            }

            _session.EndpointUrl = endpointUrl;

            HttpContext.Session.SetString("EndpointUrl", endpointUrl);

            return View("User", _session);
        }

        [HttpPost]
        public ActionResult Connect(string username, string password)
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = username;
            _session.Password = password;

            HttpContext.Session.SetString("UserName", username ?? string.Empty);
            HttpContext.Session.SetString("Password", password ?? string.Empty);

            return View("Browse", _session);
        }

        [HttpPost]
        public async Task<ActionResult> DisconnectAsync()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            if (!string.IsNullOrEmpty(_session.EndpointUrl))
            {
                await _client.DisconnectAsync(_session.EndpointUrl).ConfigureAwait(false);
            }

            HttpContext.Session.SetString("EndpointUrl", string.Empty);
            HttpContext.Session.SetString("UserName", string.Empty);
            HttpContext.Session.SetString("Password", string.Empty);

            return View("Index", _session);
        }

        [HttpPost]
        public async Task<ActionResult> GeneratePNAsync()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = HttpContext.Session.GetString("UserName");
            _session.Password = HttpContext.Session.GetString("Password");

            try
            {
                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(_session.EndpointUrl, _session.UserName, _session.Password, null).ConfigureAwait(false);

                PublishNodesInterfaceModel model = new()
                {
                    EndpointUrl = _session.EndpointUrl,
                    OpcNodes = new List<VariableModel>()
                };

                foreach (UANodeInformation nodeInfo in results)
                {
                    if (nodeInfo.Type == "Variable")
                    {
                        VariableModel variable = new()
                        {
                            OpcSamplingInterval = 1000,
                            OpcPublishingInterval = 1000,
                            HeartbeatInterval = 0,
                            SkipFirst = false,
                            Id = nodeInfo.ExpandedNodeId
                        };

                        model.OpcNodes.Add(variable);
                    }
                }

                string json = JsonConvert.SerializeObject(new List<PublishNodesInterfaceModel>() { model }, Formatting.Indented);

                return File(Encoding.UTF8.GetBytes(json), "APPLICATION/octet-stream", "publishednodes.json");
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                return View("Browse", _session);
            }
        }

        [HttpPost]
        public async Task<ActionResult> GenerateCSVAsync()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = HttpContext.Session.GetString("UserName");
            _session.Password = HttpContext.Session.GetString("Password");

            try
            {
                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(_session.EndpointUrl, _session.UserName, _session.Password, null).ConfigureAwait(false);

                StringBuilder content = new StringBuilder();
                content.Append("Endpoint,ApplicationUri,ExpandedNodeId,DisplayName,Type,VariableCurrentValue,VariableType,Parent,References\r\n");
                foreach (UANodeInformation nodeInfo in results)
                {
                    string references = string.Empty;
                    if (nodeInfo.References != null)
                    {
                        StringBuilder referencesBuilder = new StringBuilder();
                        foreach (string reference in nodeInfo.References)
                        {
                            referencesBuilder.Append(reference);
                            referencesBuilder.Append(" | ");
                        }

                        if (referencesBuilder.Length > 0)
                        {
                            referencesBuilder.Length -= 3;
                        }

                        references = referencesBuilder.ToString();
                    }

                    content.Append(EscapeCsv(nodeInfo.Endpoint)).Append(',')
                           .Append(EscapeCsv(nodeInfo.ApplicationUri)).Append(',')
                           .Append(EscapeCsv(nodeInfo.ExpandedNodeId)).Append(',')
                           .Append(EscapeCsv(nodeInfo.DisplayName)).Append(',')
                           .Append(EscapeCsv(nodeInfo.Type)).Append(',')
                           .Append(EscapeCsv(nodeInfo.VariableCurrentValue)).Append(',')
                           .Append(EscapeCsv(nodeInfo.VariableType)).Append(',')
                           .Append(EscapeCsv(nodeInfo.Parent)).Append(',')
                           .Append(EscapeCsv(references)).Append("\r\n");
                }

                return File(Encoding.UTF8.GetBytes(content.ToString()), "APPLICATION/octet-stream", "opcuaservernodes.csv");
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                return View("Browse", _session);
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        [HttpPost]
        public async Task<ActionResult> GenerateNodeSetAsync()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = HttpContext.Session.GetString("UserName");
            _session.Password = HttpContext.Session.GetString("Password");

            try
            {
                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(_session.EndpointUrl, _session.UserName, _session.Password, null).ConfigureAwait(false);
                ISystemContext context = await _client.GetSystemContextAsync(_session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                NodeStateCollection nodeStateCollection = new();
                HashSet<NodeId> addedNodes = new();
                HashSet<NodeId> customDataTypeIds = new();

                // create a single Folder object named after the UA server and organize it under the standard Objects folder
                string serverName = results.Find(r => !string.IsNullOrEmpty(r.ApplicationUri))?.ApplicationUri;
                if (string.IsNullOrEmpty(serverName))
                {
                    serverName = _session.EndpointUrl;
                }

                FolderState serverFolder = new(null)
                {
                    NodeId = new NodeId(Guid.NewGuid(), 1),
                    BrowseName = new QualifiedName(serverName, 1),
                    DisplayName = new LocalizedText(serverName),
                    TypeDefinitionId = ObjectTypeIds.FolderType
                };
                serverFolder.AddReference(ReferenceTypeIds.Organizes, true, new ExpandedNodeId(ObjectIds.ObjectsFolder));

                foreach (UANodeInformation nodeInfo in results)
                {
                    if ((nodeInfo.Type != "Variable") || (nodeInfo.NodeId == null) || (nodeInfo.VariableNode == null))
                    {
                        continue;
                    }

                    // omit standard namespace-0 nodes: they already exist (with their own type definition) in the base UA model
                    if (nodeInfo.NodeId.NamespaceIndex == 0)
                    {
                        continue;
                    }

                    // omit nodes from the standard OPC UA Diagnostics namespace
                    if (string.Equals(context.NamespaceUris.GetString(nodeInfo.NodeId.NamespaceIndex), "http://opcfoundation.org/UA/Diagnostics/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!addedNodes.Add(nodeInfo.NodeId))
                    {
                        continue;
                    }

                    VariableNode variableNode = nodeInfo.VariableNode;

                    NodeId dataType;
                    if (!NodeId.IsNull(variableNode.DataType) && (variableNode.DataType.NamespaceIndex != 0))
                    {
                        // custom DataType: reference it directly and export its definition so the nodeset is self-contained
                        dataType = variableNode.DataType;
                        customDataTypeIds.Add(variableNode.DataType);
                    }
                    else
                    {
                        // standard DataType: use the built-in base type (resolved via the type tree) so newer/unknown
                        // standard types stay resolvable; never emit a potentially-missing standard DataType
                        dataType = nodeInfo.VariableDataTypeId;
                        if (NodeId.IsNull(dataType))
                        {
                            dataType = DataTypeIds.BaseDataType;
                        }
                    }

                    BaseDataVariableState variableState = new(null)
                    {
                        NodeId = nodeInfo.NodeId,
                        BrowseName = variableNode.BrowseName,
                        DisplayName = string.IsNullOrEmpty(nodeInfo.DisplayName) ? variableNode.DisplayName : new LocalizedText(nodeInfo.DisplayName),
                        Description = variableNode.Description,
                        DataType = dataType,
                        ValueRank = variableNode.ValueRank,
                        AccessLevel = variableNode.AccessLevel,
                        UserAccessLevel = variableNode.UserAccessLevel,
                        MinimumSamplingInterval = variableNode.MinimumSamplingInterval,
                        Historizing = variableNode.Historizing,
                        WriteMask = (AttributeWriteMask)variableNode.WriteMask,
                        UserWriteMask = (AttributeWriteMask)variableNode.UserWriteMask,
                        TypeDefinitionId = VariableTypeIds.BaseDataVariableType
                    };

                    if ((variableNode.ArrayDimensions != null) && (variableNode.ArrayDimensions.Count > 0))
                    {
                        variableState.ArrayDimensions = new ReadOnlyList<uint>(variableNode.ArrayDimensions);
                    }

                    if (nodeInfo.VariableValue != null)
                    {
                        variableState.Value = nodeInfo.VariableValue;
                    }

                    // organize the variable under the server folder so it shows up in nodeset editors
                    serverFolder.AddReference(ReferenceTypeIds.Organizes, false, new ExpandedNodeId(nodeInfo.NodeId));
                    variableState.AddReference(ReferenceTypeIds.Organizes, true, new ExpandedNodeId(serverFolder.NodeId));

                    nodeStateCollection.Add(variableState);
                }

                nodeStateCollection.Add(serverFolder);

                // export the definitions of all referenced custom DataTypes (including their supertypes and nested field types)
                if (customDataTypeIds.Count > 0)
                {
                    List<NodeState> dataTypeStates = await _client.CollectDataTypeDefinitionsAsync(_session.EndpointUrl, _session.UserName, _session.Password, customDataTypeIds).ConfigureAwait(false);
                    foreach (NodeState dataTypeState in dataTypeStates)
                    {
                        nodeStateCollection.Add(dataTypeState);
                    }
                }

                using MemoryStream stream = new();
                nodeStateCollection.SaveAsNodeSet2(context, stream);

                return File(stream.ToArray(), "APPLICATION/octet-stream", "opcuaserver.nodeset2.xml");
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                return View("Browse", _session);
            }
        }

        [HttpPost]
        public async Task<ActionResult> PushCertAsync()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = HttpContext.Session.GetString("UserName");
            _session.Password = HttpContext.Session.GetString("Password");

            try
            {
                await _client.GDSServerPushAsync(_session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                _session.StatusMessage = "New certificate and trust list pushed successfully to server!";
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
            }

            return View("Browse", _session);
        }
    }
}
