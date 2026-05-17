
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;
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
