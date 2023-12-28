
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading.Tasks;

    public class BrowserController : Controller
    {
        private readonly OpcSessionHelper _helper;
        private readonly ILogger _logger;

        public BrowserController(OpcSessionHelper helper, IUAClient client, ILoggerFactory loggerFactory)
        {
            _helper = helper;
            _logger = loggerFactory.CreateLogger("BrowserController");
        }

        [HttpGet]
        public ActionResult Index()
        {
            SessionModel sessionModel = new SessionModel();
            sessionModel.SessionId = HttpContext.Session.Id;

            OpcSessionCacheData entry = null;
            if (_helper.OpcSessionCache.TryGetValue(HttpContext.Session.Id, out entry))
            {
                sessionModel.EndpointUrl = entry.EndpointURL;
                HttpContext.Session.SetString("EndpointUrl", entry.EndpointURL);
                return View("Browse", sessionModel);
            }

            return View("Index", sessionModel);
        }

        [HttpPost]
        public ActionResult UserPassword(string endpointUrl)
        {
            return View("User", new SessionModel { EndpointUrl = endpointUrl });
        }

        [HttpPost]
        public async Task<ActionResult> ConnectAsync(string username, string password, string endpointUrl)
        {
            SessionModel sessionModel = new SessionModel { UserName = username, Password = password, EndpointUrl = endpointUrl };

            Session session = null;

            try
            {
                session = await _helper.GetSessionAsync(HttpContext.Session.Id, endpointUrl, username, password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sessionModel.StatusMessage = ex.Message;
                return View("Index", sessionModel);
            }

            if (string.IsNullOrEmpty(endpointUrl))
            {
                sessionModel.StatusMessage = "The endpoint URL specified is invalid!";
                return View("Index", sessionModel);
            }
            else
            {
                HttpContext.Session.SetString("EndpointUrl", endpointUrl);
            }

            if (!string.IsNullOrEmpty(username) && (password != null))
            {
                HttpContext.Session.SetString("UserName", username);
                HttpContext.Session.SetString("Password", password);
            }
            else
            {
                HttpContext.Session.Remove("UserName");
                HttpContext.Session.Remove("Password");
            }

            if (session == null)
            {
                sessionModel.StatusMessage = "Unable to create session!";
                return View("Index", sessionModel);
            }
            else
            {
                sessionModel.StatusMessage = "Connected to: " + endpointUrl;
                return View("Browse", sessionModel);
            }
        }

        [HttpPost]
        public ActionResult Disconnect()
        {
            try
            {
                _helper.Disconnect(HttpContext.Session.Id);
                HttpContext.Session.SetString("EndpointUrl", string.Empty);
            }
            catch (Exception)
            {
                // do nothing
            }

            return View("Index", new SessionModel());
        }

        [HttpPost]
        public async Task<ActionResult> GeneratePN()
        {
            try
            {
                List<UANodeInformation> results = await BrowseNodeResursiveAsync(null).ConfigureAwait(false);

                Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl")).ConfigureAwait(false);

                PublishNodesInterfaceModel model = new()
                {
                    EndpointUrl = session.Endpoint.EndpointUrl,
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
                SessionModel sessionModel = new SessionModel
                {
                    StatusMessage = ex.Message,
                    SessionId = HttpContext.Session.Id,
                    EndpointUrl = HttpContext.Session.GetString("EndpointUrl")
                };

                return View("Browse", sessionModel);
            }
        }

        [HttpPost]
        public async Task<ActionResult> GenerateCSV()
        {
            try
            {
                List<UANodeInformation> results = await BrowseNodeResursiveAsync(null).ConfigureAwait(false);

                string content = "Endpoint,ApplicationUri,ExpandedNodeId,DisplayName,Type,VariableCurrentValue,VariableType,Parent,References\r\n";
                foreach (UANodeInformation nodeInfo in results)
                {
                    string references = string.Empty;
                    foreach (string reference in nodeInfo.References)
                    {
                        references += reference + " | ";
                    }

                    if (references.Length > 0)
                    {
                        references = references.Substring(0, references.Length - 3);
                    }

                    content += (nodeInfo.Endpoint + ","
                              + nodeInfo.ApplicationUri + ","
                              + nodeInfo.ExpandedNodeId + ","
                              + nodeInfo.DisplayName + ","
                              + nodeInfo.Type + ","
                              + nodeInfo.VariableCurrentValue + ","
                              + nodeInfo.VariableType + ","
                              + nodeInfo.Parent + ","
                              + references + "\r\n");
                }

                return File(Encoding.UTF8.GetBytes(content), "APPLICATION/octet-stream", "opcuaservernodes.csv");
            }
            catch (Exception ex)
            {
                SessionModel sessionModel = new SessionModel
                {
                    StatusMessage = ex.Message,
                    SessionId = HttpContext.Session.Id,
                    EndpointUrl = HttpContext.Session.GetString("EndpointUrl")
                };

                return View("Browse", sessionModel);
            }
        }

        private async Task<ReferenceDescriptionCollection> BrowseNodeAsync(NodeId nodeId)
        {
            ReferenceDescriptionCollection references = new();
            byte[] continuationPoint;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl")).ConfigureAwait(false);

                session.Browse(
                    null,
                    null,
                    nodeId,
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    0,
                    out continuationPoint,
                    out references);
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot browse node {0}: {1}", nodeId, ex.Message);

                throw;
            }
            finally
            {
                stopwatch.Stop();
            }

            _logger.LogInformation("Browing all childeren info of node '{0}' took {0} ms", nodeId, stopwatch.ElapsedMilliseconds);

            return references;
        }

        private async Task<List<UANodeInformation>> BrowseNodeResursiveAsync(NodeId nodeId)
        {
            List<UANodeInformation> results = new();

            try
            {
                if (nodeId == null)
                {
                    nodeId = ObjectIds.ObjectsFolder;
                }

                ReferenceDescriptionCollection references = await BrowseNodeAsync(nodeId).ConfigureAwait(false);
                if (references != null)
                {
                    List<string> processedReferences = new();
                    foreach (ReferenceDescription nodeReference in references)
                    {
                        // filter out duplicates
                        if (processedReferences.Contains(nodeReference.NodeId.ToString()))
                        {
                            continue;
                        }

                        UANodeInformation nodeInfo = new()
                        {
                            DisplayName = nodeReference.DisplayName.Text,
                            Type = nodeReference.NodeClass.ToString()
                        };

                        try
                        {
                            Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl")).ConfigureAwait(false);

                            nodeInfo.ApplicationUri = session.ServerUris.ToArray()[0];

                            nodeInfo.Endpoint = session.Endpoint.EndpointUrl;

                            if (nodeId.NamespaceIndex == 0)
                            {
                                nodeInfo.Parent = "nsu=http://opcfoundation.org/UA;" + nodeId.ToString();
                            }
                            else
                            {
                                nodeInfo.Parent = NodeId.ToExpandedNodeId(ExpandedNodeId.ToNodeId(nodeId, session.NamespaceUris), session.NamespaceUris).ToString();
                            }

                            if (nodeReference.NodeId.NamespaceIndex == 0)
                            {
                                nodeInfo.ExpandedNodeId = "nsu=http://opcfoundation.org/UA;" + nodeReference.NodeId.ToString();
                            }
                            else
                            {
                                nodeInfo.ExpandedNodeId = NodeId.ToExpandedNodeId(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris), session.NamespaceUris).ToString();
                            }

                            if (nodeReference.NodeClass == NodeClass.Variable)
                            {
                                try
                                {
                                    DataValue value = session.ReadValue(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris));
                                    if ((value != null) && (value.WrappedValue != Variant.Null))
                                    {
                                        nodeInfo.VariableCurrentValue = value.ToString();
                                        nodeInfo.VariableType = value.WrappedValue.TypeInfo.ToString();
                                    }
                                }
                                catch (Exception)
                                {
                                    // do nothing
                                }
                            }

                            List<UANodeInformation> childReferences = await BrowseNodeResursiveAsync(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris)).ConfigureAwait(false);

                            nodeInfo.References = new string[childReferences.Count];
                            for (int i = 0; i < childReferences.Count; i++)
                            {
                                nodeInfo.References[i] = childReferences[i].ExpandedNodeId.ToString();
                            }

                            results.AddRange(childReferences);
                        }
                        catch (Exception)
                        {
                            // skip this node
                            continue;
                        }

                        processedReferences.Add(nodeReference.NodeId.ToString());
                        results.Add(nodeInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Cannot browse node {0}: {1}", nodeId, ex.Message);

                throw;
            }

            return results;
        }
    }
}
