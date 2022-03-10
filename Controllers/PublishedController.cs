
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class PublishedController : Controller
    {
        private readonly ILogger _logger;
        private readonly IPublishedNodesFileHandler _publishedNodesFileHandler;
        private readonly IUAClient _uaclient;
        private readonly IFileStorage _storage;

        public PublishedController(
            ILoggerFactory loggerFactory,
            IPublishedNodesFileHandler publishedNodesFileHandler,
            IUAClient client,
            IFileStorage storage)
        {
            _logger = loggerFactory.CreateLogger("PublishedController");
            _publishedNodesFileHandler = publishedNodesFileHandler;
            _uaclient = client;
            _storage = storage;
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
                    throw new ArgumentException("No files specified!");
                }
                
                if ((file.Length == 0) || (file.ContentType != "application/json"))
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                using (Stream content = file.OpenReadStream())
                {
                    byte[] bytes = new byte[file.Length];
                    content.Read(bytes, 0, (int)file.Length);

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
                string persistencyFilePath = _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "persistency.json").GetAwaiter().GetResult();
                byte[] persistencyFile = _storage.LoadFileAsync(persistencyFilePath).GetAwaiter().GetResult();
                if (persistencyFile == null)
                {
                    // no file persisted yet
                    _logger.LogInformation("Persistency file not found.");
                }
                else
                {
                    _logger.LogInformation($"Parsing persistency file...");
                    _publishedNodesFileHandler.ParseFile(persistencyFile);
                    _logger.LogInformation("Persistency file parsed successfully.");
                }

                return View("Index", GeneratePublishedNodesArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Persistency file not loaded!");
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
                        node.ExpandedNodeId = ExpandedNodeId.Parse(parts[3]);
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
