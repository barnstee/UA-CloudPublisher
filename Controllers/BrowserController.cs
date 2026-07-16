
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
        public async Task<ActionResult> Index()
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            if (!string.IsNullOrEmpty(_session.EndpointUrl))
            {
                _session.UserName = HttpContext.Session.GetString("UserName");
                _session.Password = HttpContext.Session.GetString("Password");
                await PopulateNamespacesAsync().ConfigureAwait(false);
                return View("Browse", _session);
            }

            return View("Index", _session);
        }

        [HttpPost]
        public ActionResult UserPassword(string endpointUrl)
        {
            string address = endpointUrl?.Trim();

            // strip any scheme the user may have typed so we can normalize to a single opc.tcp:// prefix
            if (!string.IsNullOrEmpty(address) && address.StartsWith("opc.tcp://", StringComparison.OrdinalIgnoreCase))
            {
                address = address.Substring("opc.tcp://".Length);
            }

            if (string.IsNullOrEmpty(address))
            {
                _session.StatusMessage = "Please provide the OPC UA server's address in the format ipaddress:port";
                return View("Index", _session);
            }

            // the opc.tcp:// scheme is implied by the label in the UI, so prepend it to what the user entered
            _session.EndpointUrl = "opc.tcp://" + address;

            HttpContext.Session.SetString("EndpointUrl", _session.EndpointUrl);

            return View("User", _session);
        }

        [HttpPost]
        public async Task<ActionResult> Connect(string username, string password)
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = username;
            _session.Password = password;

            HttpContext.Session.SetString("UserName", username ?? string.Empty);
            HttpContext.Session.SetString("Password", password ?? string.Empty);

            await PopulateNamespacesAsync().ConfigureAwait(false);

            return View("Browse", _session);
        }

        private async Task PopulateNamespacesAsync()
        {
            try
            {
                string[] namespaces = await _client.GetNamespaceUrisAsync(_session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);
                _session.Namespaces = new List<string>(namespaces);
            }
            catch (Exception)
            {
                _session.Namespaces = new List<string>();
            }
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
        public async Task<ActionResult> GenerateNodeSetAsync(string namespaceUri)
        {
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");
            _session.UserName = HttpContext.Session.GetString("UserName");
            _session.Password = HttpContext.Session.GetString("Password");

            try
            {
                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(_session.EndpointUrl, _session.UserName, _session.Password, null).ConfigureAwait(false);
                ISystemContext context = await _client.GetSystemContextAsync(_session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                // the selected namespace becomes the exported model; only variables from this namespace are exported
                string modelUri = namespaceUri?.Trim();
                if (string.IsNullOrEmpty(modelUri))
                {
                    _session.StatusMessage = "Please select a namespace to export.";
                    await PopulateNamespacesAsync().ConfigureAwait(false);
                    return View("Browse", _session);
                }

                int modelNamespaceIndex = -1;
                for (int i = 0; i < context.NamespaceUris.Count; i++)
                {
                    if (string.Equals(context.NamespaceUris.GetString((uint)i), modelUri, StringComparison.Ordinal))
                    {
                        modelNamespaceIndex = i;
                        break;
                    }
                }

                if (modelNamespaceIndex <= 0)
                {
                    _session.StatusMessage = $"The selected namespace '{modelUri}' is not available on the server.";
                    await PopulateNamespacesAsync().ConfigureAwait(false);
                    return View("Browse", _session);
                }

                NodeStateCollection nodeStateCollection = new();
                HashSet<NodeId> addedNodes = new();

                // namespaces (other than the exported model's own) that exported nodes depend on, declared as required models
                HashSet<string> requiredNamespaceUris = new(StringComparer.Ordinal) { Namespaces.OpcUa };

                // create a single Folder object named after the UA server and organize it under the standard Objects folder
                string serverName = results.Find(r => !string.IsNullOrEmpty(r.ApplicationUri))?.ApplicationUri;
                if (string.IsNullOrEmpty(serverName))
                {
                    serverName = _session.EndpointUrl;
                }

                // the anchor folder belongs to the exported model's namespace so a strict importer instantiates it
                FolderState serverFolder = new(null)
                {
                    NodeId = new NodeId(Guid.NewGuid(), (ushort)modelNamespaceIndex),
                    BrowseName = new QualifiedName(serverName, (ushort)modelNamespaceIndex),
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

                    // export only variables that belong to the selected namespace
                    if (nodeInfo.NodeId.NamespaceIndex != modelNamespaceIndex)
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
                        // custom DataType: reference it directly (its defining namespace becomes a required model)
                        dataType = variableNode.DataType;
                        if (variableNode.DataType.NamespaceIndex != modelNamespaceIndex)
                        {
                            string dataTypeNamespaceUri = context.NamespaceUris.GetString(variableNode.DataType.NamespaceIndex);
                            if (!string.IsNullOrEmpty(dataTypeNamespaceUri))
                            {
                                requiredNamespaceUris.Add(dataTypeNamespaceUri);
                            }
                        }
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

                using MemoryStream stream = new();
                nodeStateCollection.SaveAsNodeSet2(context, stream);

                // SaveAsNodeSet2 does not emit a <Models> entry; add one with the model URI, publication date and dependencies
                stream.Position = 0;
                Opc.Ua.Export.UANodeSet nodeSet = Opc.Ua.Export.UANodeSet.Read(stream);

                // read the publication dates the server advertises for its namespaces (Server.Namespaces metadata)
                Dictionary<string, DateTime> serverPublicationDates = await _client.GetNamespacePublicationDatesAsync(_session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                DateTime publicationDate = DateTime.UtcNow.Date;
                if (serverPublicationDates.TryGetValue(modelUri, out DateTime modelPublicationDate))
                {
                    publicationDate = modelPublicationDate;
                }

                // every namespace the exported nodes depend on becomes a required model
                List<Opc.Ua.Export.ModelTableEntry> requiredModels = new();
                foreach (string requiredNamespaceUri in requiredNamespaceUris)
                {
                    if (string.Equals(requiredNamespaceUri, modelUri, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    requiredModels.Add(CreateRequiredModel(requiredNamespaceUri, serverPublicationDates));
                }

                nodeSet.Models = new[]
                {
                    new Opc.Ua.Export.ModelTableEntry
                    {
                        ModelUri = modelUri,
                        PublicationDate = publicationDate,
                        PublicationDateSpecified = true,
                        Version = "1.0.0",
                        RequiredModel = requiredModels.Count > 0 ? requiredModels.ToArray() : null
                    }
                };
                nodeSet.LastModified = publicationDate;
                nodeSet.LastModifiedSpecified = true;

                using MemoryStream output = new();
                nodeSet.Write(output);

                return File(output.ToArray(), "APPLICATION/octet-stream", "opcuaserver.nodeset2.xml");
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                await PopulateNamespacesAsync().ConfigureAwait(false);
                return View("Browse", _session);
            }
        }

        private static Opc.Ua.Export.ModelTableEntry CreateRequiredModel(string modelUri, Dictionary<string, DateTime> serverPublicationDates)
        {
            Opc.Ua.Export.ModelTableEntry model = new() { ModelUri = modelUri };

            if (serverPublicationDates.TryGetValue(modelUri, out DateTime publicationDate))
            {
                model.PublicationDate = publicationDate;
                model.PublicationDateSpecified = true;
            }

            return model;
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
