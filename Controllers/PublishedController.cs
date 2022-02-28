
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

        public async Task<IActionResult> Index()
        {
            return View(await GeneratePublishedNodesArray().ConfigureAwait(false));
        }

        [HttpPost]
        public async Task<IActionResult> Load(IFormFile file)
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

                    X509Certificate2 certWithPrivateKey = _app.GetAppConfig().SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null).GetAwaiter().GetResult();
                    if (!_publishedNodesFileHandler.ParseFile(bytes, certWithPrivateKey))
                    {
                        throw new Exception("Could not parse publishednodes file and publish its nodes!");
                    }
                }
                
                return View("Index", await GeneratePublishedNodesArray().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new string[] { ex.Message });
            }
        }

        private async Task<string[]> GeneratePublishedNodesArray()
        {
            IEnumerable<PublishNodesInterfaceModel> publishedNodes = await _client.GetListofPublishedNodesAsync().ConfigureAwait(false);

            List<string> publishedNodesDisplay = new List<string>();
            foreach (PublishNodesInterfaceModel entry in publishedNodes)
            {
                if (entry.OpcEvents != null)
                {
                    foreach (EventModel node in entry.OpcEvents)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Event: " + node.ExpandedNodeId + " Name: " + node.DisplayName);
                    }
                }

                if (entry.OpcNodes != null)
                {
                    foreach (VariableModel node in entry.OpcNodes)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Variable: " + node.Id + " Name: " + node.DisplayName);
                    }
                }
            }

            return publishedNodesDisplay.ToArray();
        }
    }
}
