
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class PublishedController : Controller
    {
        private readonly ILogger _logger;
        private readonly IPublishedNodesFileHandler _publishedNodesFileHandler;
        private readonly IUAApplication _app;
        private readonly IUAClient _client;
        private readonly IFileStorage _storage;

        public PublishedController(ILoggerFactory loggerFactory, IPublishedNodesFileHandler publishedNodesFileHandler, IUAApplication app, IUAClient client, IFileStorage storage)
        {
            _logger = loggerFactory.CreateLogger("PublishedController");
            _publishedNodesFileHandler = publishedNodesFileHandler;
            _app = app;
            _client = client;
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
                return View("Index", new string[] { ex.Message });
            }
        }

        private string[] GeneratePublishedNodesArray()
        {
            IEnumerable<PublishNodesInterfaceModel> publishedNodes = _client.GetListofPublishedNodes();

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
