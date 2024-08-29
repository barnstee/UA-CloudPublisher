
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Publisher;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading.Tasks;

    public class BrowserController : Controller
    {
        private readonly IUAApplication _app;
        private readonly IUAClient _client;
        private readonly OpcSessionHelper _helper;
        private readonly ILogger _logger;

        private SessionModel _session;

        public BrowserController(OpcSessionHelper helper, IUAApplication app, IUAClient client, ILoggerFactory loggerFactory)
        {
            _app = app;
            _client = client;
            _helper = helper;
            _logger = loggerFactory.CreateLogger("BrowserController");
            _session = new();
        }

        [HttpPost]
        public IActionResult DownloadUACert()
        {
            _session.SessionId = HttpContext.Session.Id;
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
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            OpcSessionCacheData entry = null;
            if (_helper.OpcSessionCache.TryGetValue(_session.SessionId, out entry))
            {
                _session.EndpointUrl = entry.EndpointURL;
                return View("Browse", _session);
            }

            return View("Index", _session);
        }

        [HttpPost]
        public ActionResult UserPassword(string endpointUrl)
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = endpointUrl;

            HttpContext.Session.SetString("EndpointUrl", endpointUrl);

            return View("User", _session);
        }

        [HttpPost]
        public async Task<ActionResult> ConnectAsync(string username, string password)
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            _session.UserName = username;
            _session.Password = password;

            Client.Session session = null;
            try
            {
                session = await _helper.GetSessionAsync(_session.SessionId, _session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _session.StatusMessage = ex.Message;
                return View("Index", _session);
            }

            if (session == null)
            {
                _session.StatusMessage = "Unable to create session!";
                return View("Index", _session);
            }
            else
            {
                _session.StatusMessage = "Connected to: " + _session.EndpointUrl;
                return View("Browse", _session);
            }
        }

        [HttpPost]
        public ActionResult Disconnect()
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            try
            {
                _helper.Disconnect(_session.SessionId);
            }
            catch (Exception)
            {
                // do nothing
            }

            return View("Index", _session);
        }

        [HttpPost]
        public async Task<ActionResult> GeneratePN()
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            try
            {
                Client.Session session = await _helper.GetSessionAsync(_session.SessionId, _session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(session, null).ConfigureAwait(false);

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
                _session.StatusMessage = ex.Message;
                return View("Browse", _session);
            }
        }

        [HttpPost]
        public async Task<ActionResult> GenerateCSV()
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            try
            {
                Client.Session session = await _helper.GetSessionAsync(_session.SessionId, _session.EndpointUrl, _session.UserName, _session.Password).ConfigureAwait(false);

                List<UANodeInformation> results = await _client.BrowseVariableNodesResursivelyAsync(session, null).ConfigureAwait(false);

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
                _session.StatusMessage = ex.Message;
                return View("Browse", _session);
            }
        }
        
        [HttpPost]
        public async Task<ActionResult> PushCert()
        {
            _session.SessionId = HttpContext.Session.Id;
            _session.EndpointUrl = HttpContext.Session.GetString("EndpointUrl");

            try
            {
                OpcSessionCacheData entry;
                if (_helper.OpcSessionCache.TryGetValue(_session.SessionId, out entry))
                {
                    if (entry.OPCSession != null)
                    {
                        await _client.GDSServerPush(_session.EndpointUrl, entry.Username, entry.Password).ConfigureAwait(false);
                    }
                }

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
