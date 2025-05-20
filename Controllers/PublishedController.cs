
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    public class PublishedController : Controller
    {
        private readonly ILogger _logger;
        private readonly IPublishedNodesFileHandler _publishedNodesFileHandler;
        private readonly IUAClient _uaclient;

        public PublishedController(
            ILoggerFactory loggerFactory,
            IPublishedNodesFileHandler publishedNodesFileHandler,
            IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("PublishedController");
            _publishedNodesFileHandler = publishedNodesFileHandler;
            _uaclient = client;
        }

        public IActionResult Index()
        {
            return View(GeneratePublishedNodesArray());
        }

        [HttpPost]
        public IActionResult Load(IFormFile file)
        {
            try
            {
                if (file == null)
                {
                    throw new ArgumentException("No file specified!");
                }

                if ((file.Length == 0) || (file.ContentType != "application/json"))
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                using (Stream content = file.OpenReadStream())
                {
                    byte[] bytes = new byte[file.Length];
                    content.ReadExactly(bytes, 0, (int)file.Length);

                    _publishedNodesFileHandler.ParseFile(bytes);
                }

                return View("Index", GeneratePublishedNodesArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new string[] { "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DownloadFile()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_uaclient.GetPublishedNodes(), Formatting.Indented);
                return File(Encoding.UTF8.GetBytes(json), "APPLICATION/octet-stream", "publishednodes.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not generate publishednodes.json file");
                return View("Index", new string[] { "Error:" + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult LoadPersisted()
        {
            try
            {
                byte[] persistencyFile = System.IO.File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "settings", "persistency.json"));
                if (persistencyFile == null)
                {
                    // no file persisted yet
                    throw new Exception("Persistency file not found.");
                }
                else
                {
                    _ = Task.Run(() => _publishedNodesFileHandler.ParseFile(persistencyFile));
                }

                return View("Index", GeneratePublishedNodesArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new string[] { "Error: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DeleteNode()
        {
            try
            {
                NodePublishingModel node = new NodePublishingModel();
                foreach (string key in Request.Form.Keys)
                {
                    if (key.Contains("Endpoint:"))
                    {
                        string[] parts = key.Split(' ');
                        node.EndpointUrl = parts[1];

                        // get the nodeId from the key
                        string nodeId = key.Substring(key.IndexOf("Variable: ") + 10);

                        node.ExpandedNodeId = ExpandedNodeId.Parse(nodeId);
                        break;
                    }
                }

                _uaclient.UnpublishNode(node);
                _logger.LogInformation($"Node {node.ExpandedNodeId} on endpoint {node.EndpointUrl} unpublished successfully.");

                return View("Index", GeneratePublishedNodesArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unpublish node!");
                return View("Index", new string[] { "Error: " + ex.Message });
            }
        }

        private string[] GeneratePublishedNodesArray()
        {
            IEnumerable<PublishNodesInterfaceModel> publishedNodes = _uaclient.GetPublishedNodes();

            List<string> publishedNodesDisplay = new List<string>();
            foreach (PublishNodesInterfaceModel entry in publishedNodes)
            {
                if (entry.OpcEvents != null)
                {
                    foreach (EventModel node in entry.OpcEvents)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Event: " + node.ExpandedNodeId);
                    }
                }

                if (entry.OpcNodes != null)
                {
                    foreach (VariableModel node in entry.OpcNodes)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Variable: " + node.Id);
                    }
                }
            }

            return publishedNodesDisplay.ToArray();
        }
    }
}
