
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Microsoft.AspNetCore.SignalR;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Web;
    using UA.MQTT.Publisher;
    using UA.MQTT.Publisher.Models;

    public class StatusHub : Hub { }

    public class BrowserController : Controller
    {
        private List<string> _prepopulatedEndpoints = new List<string>();

        private readonly OpcSessionHelper _helper;

        private IHubContext<StatusHub> _hubContext;

        public BrowserController(OpcSessionHelper helper, IHubContext<StatusHub> hubContext)
        {
            _helper = helper;
            _hubContext = hubContext;
        }

        private class MethodCallParameterData
        {
            public string Name { get; set; }

            public string Value { get; set; }

            public string ValueRank { get; set; }

            public string ArrayDimensions { get; set; }

            public string Description { get; set; }

            public string Datatype { get; set; }

            public string TypeName { get; set; }
        }

        [HttpGet]
        public ActionResult Privacy()
        {
            return View("Privacy");
        }

        [HttpGet]
        public ActionResult Index()
        {
            OpcSessionModel sessionModel = new OpcSessionModel();
            sessionModel.SessionId = HttpContext.Session.Id;

            OpcSessionCacheData entry = null;
            if (_helper.OpcSessionCache.TryGetValue(HttpContext.Session.Id, out entry))
            {
                sessionModel.EndpointUrl = entry.EndpointURL;
                HttpContext.Session.SetString("EndpointUrl", entry.EndpointURL);
                return View("Browse", sessionModel);
            }

            sessionModel.PrepopulatedEndpoints = new SelectList(_prepopulatedEndpoints, "Value", "Text");
            return View("Index", sessionModel);
        }

        [HttpGet]
        public ActionResult Error(string errorMessage)
        {
            OpcSessionModel sessionModel = new OpcSessionModel
            {
                ErrorHeader = "Error",
                EndpointUrl = HttpContext.Session.GetString("EndpointUrl"),
                ErrorMessage = HttpUtility.HtmlDecode(errorMessage)
            };

            return Json(sessionModel);
        }

        [HttpPost]
        public async Task<ActionResult> Connect(string endpointUrl, bool enforceTrust = false)
        {
            OpcSessionModel sessionModel = new OpcSessionModel { EndpointUrl = endpointUrl };

            Session session = null;
            try
            {
                session = await _helper.GetSessionAsync(HttpContext.Session.Id, endpointUrl, true);
            }
            catch (Exception exception)
            {
                // Check for untrusted certificate
                ServiceResultException ex = exception as ServiceResultException;
                if ((ex != null) && (ex.InnerResult != null) && (ex.InnerResult.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted))
                {
                    sessionModel.ErrorHeader = "Untrusted";
                    return Json(sessionModel);
                }

                // Generate an error to be shown in the error view and trace.
                string errorMessageTrace = string.Format("Error", exception.Message,
                exception.InnerException?.Message ?? "--", exception?.StackTrace ?? "--");
                Trace.TraceError(errorMessageTrace);
                sessionModel.ErrorHeader = "Error";

                return Json(sessionModel);
            }

            HttpContext.Session.SetString("EndpointUrl", endpointUrl);

            return View("Browse", sessionModel);
        }

        [HttpPost]
        public async Task<ActionResult> ConnectWithTrust(string endpointURL)
        {
            OpcSessionModel sessionModel = new OpcSessionModel { EndpointUrl = endpointURL };

            // Check that there is a session already in our cache data
            OpcSessionCacheData entry;
            if (_helper.OpcSessionCache.TryGetValue(HttpContext.Session.Id, out entry))
            {
                if (string.Equals(entry.EndpointURL, endpointURL, StringComparison.InvariantCultureIgnoreCase))
                {
                    OpcSessionCacheData newValue = new OpcSessionCacheData
                    {
                        CertThumbprint = entry.CertThumbprint,
                        OPCSession = entry.OPCSession,
                        EndpointURL = entry.EndpointURL,
                        Trusted = true
                    };
                    _helper.OpcSessionCache.TryUpdate(HttpContext.Session.Id, newValue, entry);

                    // connect with enforced trust
                    return await Connect(endpointURL, true);
                }
            }

            // Generate an error to be shown in the error view.
            // Since we should only get here when folks are trying to hack the site,
            // make the error generic so not to reveal too much about the internal workings of the site.
            sessionModel.ErrorHeader = "Error";

            return Json(sessionModel);
        }

        [HttpGet]
        public ActionResult Disconnect(string backUrl)
        {
            return Disconnect(null, backUrl);
        }

        [HttpPost]
        public ActionResult Disconnect(IFormCollection form, string backUrl)
        {
            try
            {
                _helper.Disconnect(HttpContext.Session.Id);
                HttpContext.Session.SetString("EndpointUrl", string.Empty);
            }
            catch (Exception exception)
            {
                // Trace an error and return to the connect view.
                var errorMessage = string.Format("Error", exception.Message,
                    exception.InnerException?.Message ?? "--", exception?.StackTrace ?? "--");
                Trace.TraceError(errorMessage);
            }

            OpcSessionModel sessionModel = new OpcSessionModel();
            sessionModel.PrepopulatedEndpoints = new SelectList(_prepopulatedEndpoints, "Value", "Text");
            return View("Index", sessionModel);
        }

        [HttpPost]
        public async Task<ActionResult> GetRootNode()
        {
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;
            var jsonTree = new List<object>();

            bool retry = true;
            while (true)
            {
                try
                {
                    Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl"));

                    session.Browse(
                        null,
                        null,
                        ObjectIds.ObjectsFolder,
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HierarchicalReferences,
                        true,
                        0,
                        out continuationPoint,
                        out references);
                    jsonTree.Add(new { id = ObjectIds.ObjectsFolder.ToString(), text = "Root", children = (references?.Count != 0) });

                    return Json(jsonTree);
                }
                catch (Exception exception)
                {
                    _helper.Disconnect(HttpContext.Session.Id);
                    if (!retry)
                    {
                        return Content(CreateOpcExceptionActionString(exception));
                    }
                    retry = false;
                }
            }
        }

        [HttpPost]
        public async Task<ActionResult> GetChildren(string jstreeNode)
        {
            // This delimiter is used to allow the storing of the OPC UA parent node ID together with the OPC UA child node ID in jstree data structures and provide it as parameter to 
            // Ajax calls.
            string[] delimiter = { "__$__" };
            string[] jstreeNodeSplit = jstreeNode.Split(delimiter, 3, StringSplitOptions.None);
            string node;
            if (jstreeNodeSplit.Length == 1)
            {
                node = jstreeNodeSplit[0];
            }
            else
            {
                node = jstreeNodeSplit[1];
            }

            ReferenceDescriptionCollection references = null;
            Byte[] continuationPoint;
            var jsonTree = new List<object>();

            // read the currently published nodes
            Session session = null;
            string[] publishedNodes = null;
            string endpointUrl = null;
            try
            {
                session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl"));
                endpointUrl = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
            }
            catch (Exception e)
            {
                // do nothing, since we still want to show the tree
                Trace.TraceWarning("Can not read published nodes for endpoint '{0}'.", endpointUrl);
                string errorMessage = string.Format("Error", e.Message,
                    e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                Trace.TraceWarning(errorMessage);
            }

            bool retry = true;
            while (true)
            {
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    try
                    {
                        session.Browse(
                            null,
                            null,
                            node,
                            0u,
                            BrowseDirection.Forward,
                            ReferenceTypeIds.HierarchicalReferences,
                            true,
                            0,
                            out continuationPoint,
                            out references);
                    }
                    catch (Exception e)
                    {
                        // skip this node
                        Trace.TraceError("Can not browse node '{0}'", node);
                        string errorMessage = string.Format("Error", e.Message,
                            e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                        Trace.TraceError(errorMessage);
                    }

                    Trace.TraceInformation("Browsing node '{0}' data took {0} ms", node.ToString(), stopwatch.ElapsedMilliseconds);

                    if (references != null)
                    {
                        var idList = new List<string>();
                        foreach (var nodeReference in references)
                        {
                            bool idFound = false;
                            foreach (var id in idList)
                            {
                                if (id == nodeReference.NodeId.ToString())
                                {
                                    idFound = true;
                                }
                            }
                            if (idFound == true)
                            {
                                continue;
                            }

                            ReferenceDescriptionCollection childReferences = null;
                            Byte[] childContinuationPoint;

                            Trace.TraceInformation("Browse '{0}' count: {1}", nodeReference.NodeId, jsonTree.Count);

                            INode currentNode = null;
                            try
                            {
                                session.Browse(
                                    null,
                                    null,
                                    ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris),
                                    0u,
                                    BrowseDirection.Forward,
                                    ReferenceTypeIds.HierarchicalReferences,
                                    true,
                                    0,
                                    out childContinuationPoint,
                                    out childReferences);

                                currentNode = session.ReadNode(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris));
                            }
                            catch (Exception e)
                            {
                                // skip this node
                                Trace.TraceError("Can not browse or read node '{0}'", nodeReference.NodeId);
                                string errorMessage = string.Format("Error", e.Message,
                                    e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                                Trace.TraceError(errorMessage);

                                continue;
                            }

                            byte currentNodeAccessLevel = 0;
                            byte currentNodeEventNotifier = 0;
                            bool currentNodeExecutable = false;

                            VariableNode variableNode = currentNode as VariableNode;
                            if (variableNode != null)
                            {
                                currentNodeAccessLevel = variableNode.UserAccessLevel;
                            }

                            ObjectNode objectNode = currentNode as ObjectNode;
                            if (objectNode != null)
                            {
                                currentNodeEventNotifier = objectNode.EventNotifier;
                            }

                            ViewNode viewNode = currentNode as ViewNode;
                            if (viewNode != null)
                            {
                                currentNodeEventNotifier = viewNode.EventNotifier;
                            }

                            MethodNode methodNode = currentNode as MethodNode;
                            if (methodNode != null)
                            {
                                currentNodeExecutable = methodNode.UserExecutable;
                            }

                            var isPublished = false;
                            if (publishedNodes != null)
                            {
                                Session stationSession = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl"));
                                string urn = stationSession.ServerUris.GetString(0);
                                foreach (var nodeId in publishedNodes)
                                {
                                    if (nodeId == nodeReference.NodeId.ToString())
                                    {
                                        isPublished = true;
                                    }
                                }
                            }

                            jsonTree.Add(new
                            {
                                id = ("__" + node + delimiter[0] + nodeReference.NodeId.ToString()),
                                text = nodeReference.DisplayName.ToString(),
                                nodeClass = nodeReference.NodeClass.ToString(),
                                accessLevel = currentNodeAccessLevel.ToString(),
                                eventNotifier = currentNodeEventNotifier.ToString(),
                                executable = currentNodeExecutable.ToString(),
                                children = (childReferences.Count == 0) ? false : true,
                                publishedNode = isPublished,
                                relevantNode = true
                            });
                            idList.Add(nodeReference.NodeId.ToString());
                        }

                        // If there are no children, then this is a call to read the properties of the node itself.
                        if (jsonTree.Count == 0)
                        {
                            INode currentNode = null;

                            try
                            {
                                currentNode = session.ReadNode(new NodeId(node));
                            }
                            catch (Exception e)
                            {
                                string errorMessage = string.Format("Error", e.Message,
                                    e.InnerException?.Message ?? "--", e?.StackTrace ?? "--");
                                Trace.TraceError(errorMessage);
                            }

                            if (currentNode == null)
                            {
                                byte currentNodeAccessLevel = 0;
                                byte currentNodeEventNotifier = 0;
                                bool currentNodeExecutable = false;

                                VariableNode variableNode = currentNode as VariableNode;

                                if (variableNode != null)
                                {
                                    currentNodeAccessLevel = variableNode.UserAccessLevel;
                                }

                                ObjectNode objectNode = currentNode as ObjectNode;

                                if (objectNode != null)
                                {
                                    currentNodeEventNotifier = objectNode.EventNotifier;
                                }

                                ViewNode viewNode = currentNode as ViewNode;

                                if (viewNode != null)
                                {
                                    currentNodeEventNotifier = viewNode.EventNotifier;
                                }

                                MethodNode methodNode = currentNode as MethodNode;

                                if (methodNode != null)
                                {
                                    currentNodeExecutable = methodNode.UserExecutable;
                                }

                                jsonTree.Add(new
                                {
                                    id = jstreeNode,
                                    text = currentNode.DisplayName.ToString(),
                                    nodeClass = currentNode.NodeClass.ToString(),
                                    accessLevel = currentNodeAccessLevel.ToString(),
                                    eventNotifier = currentNodeEventNotifier.ToString(),
                                    executable = currentNodeExecutable.ToString(),
                                    children = false
                                });
                            }
                        }
                    }

                    stopwatch.Stop();
                    Trace.TraceInformation("Browing all childeren info of node '{0}' took {0} ms", node, stopwatch.ElapsedMilliseconds);

                    return Json(jsonTree);
                }
                catch (Exception exception)
                {
                    _helper.Disconnect(HttpContext.Session.Id);
                    if (!retry)
                    {
                        return Content(CreateOpcExceptionActionString(exception));
                    }
                    retry = false;
                }
            }
        }

        [HttpPost]
        public async Task<ActionResult> VariableRead(string jstreeNode)
        {
            string[] delimiter = { "__$__" };
            string[] jstreeNodeSplit = jstreeNode.Split(delimiter, 3, StringSplitOptions.None);
            string node;
           
            if (jstreeNodeSplit.Length == 1)
            {
                node = jstreeNodeSplit[0];
            }
            else
            {
                node = jstreeNodeSplit[1];
            }

            bool retry = true;
            while (true)
            {
                try
                { 
                    DataValueCollection values = null;
                    DiagnosticInfoCollection diagnosticInfos = null;
                    ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                    ReadValueId valueId = new ReadValueId();
                    valueId.NodeId = new NodeId(node);
                    valueId.AttributeId = Attributes.Value;
                    valueId.IndexRange = null;
                    valueId.DataEncoding = null;
                    nodesToRead.Add(valueId);

                    UpdateStatus($"Read OPC UA node: {valueId.NodeId}");

                    Session session = await _helper.GetSessionAsync(HttpContext.Session.Id, HttpContext.Session.GetString("EndpointUrl")).ConfigureAwait(false);
                    ResponseHeader responseHeader = session.Read(null, 0, TimestampsToReturn.Both, nodesToRead, out values, out diagnosticInfos);
                    string value = "";
                    string actionResult;
                    if (values.Count > 0 && values[0].Value != null)
                    {
                        if (values[0].WrappedValue.ToString().Length > 40)
                        {
                            value = values[0].WrappedValue.ToString().Substring(0, 40);
                            value += "...";
                        }
                        else
                        {
                            value = values[0].WrappedValue.ToString();
                        }

                        actionResult = $"{{ \"value\": \"{value}\", \"status\": \"{values[0].StatusCode}\", \"sourceTimestamp\": \"{values[0].SourceTimestamp}\", \"serverTimestamp\": \"{values[0].ServerTimestamp}\" }}";
                    }
                    else
                    {
                        actionResult = string.Empty;
                    }


                    return Content(actionResult);
                }
                catch (Exception exception)
                {
                    if (!retry)
                    {
                        return Content(CreateOpcExceptionActionString(exception));
                    }
                    retry = false;
                }
            }
        }

        [HttpPost]
        public ActionResult VariablePublishUnpublish(string jstreeNode, string method)
        {
            string[] delimiter = { "__$__" };
            string[] jstreeNodeSplit = jstreeNode.Split(delimiter, 3, StringSplitOptions.None);
            string node;
            string actionResult = "";
            string publisherSessionId = Guid.NewGuid().ToString();

            if (jstreeNodeSplit.Length == 1)
            {
                node = jstreeNodeSplit[0];
            }
            else
            {
                node = jstreeNodeSplit[1];
            }

            try
            {
                
                return Content(actionResult);
            }
            catch (Exception exception)
            {
                return Content(CreateOpcExceptionActionString(exception));
            }
            finally
            {
                if (publisherSessionId != null)
                {
                    _helper.Disconnect(publisherSessionId);
                }
            }
        }

        /// <summary>
        /// Writes an error message to the trace and generates an HTML encoded string to be sent to the client in case of an error.
        /// </summary>
        private string CreateOpcExceptionActionString(Exception exception)
        {
            // Generate an error response, to be shown in the error view.
            string errorMessage = string.Format("Error", exception.Message,
                exception.InnerException?.Message ?? "--", exception?.StackTrace ?? "--");
            Trace.TraceError(errorMessage);
            errorMessage = "Error";
            string actionResult = HttpUtility.HtmlEncode(errorMessage);
            Response.StatusCode = 1;

            return actionResult;
        }

        /// <summary>
        /// Sends the message to all connected clients as status indication
        /// </summary>
        /// <param name="message">Text to show on web page</param>
        private void UpdateStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(nameof(message));
            }

            _hubContext.Clients.All.SendAsync("addNewMessageToPage", HttpContext?.Session.Id, message).Wait();
        }
    }
}
