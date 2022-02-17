
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

        public PublishedController(ILoggerFactory loggerFactory, IPublishedNodesFileHandler publishedNodesFileHandler, IUAApplication app, IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("PublishedController");
            _publishedNodesFileHandler = publishedNodesFileHandler;
            _app = app;
            _client = client;
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

                // file name validation
                new FileInfo(file.FileName);

                // create seperate directory
                string pathToPublishedNodes = Path.Combine(Directory.GetCurrentDirectory(), "PublishedNodes");
                if (!Directory.Exists(pathToPublishedNodes))
                {
                    Directory.CreateDirectory(pathToPublishedNodes);
                }

                // store the file on the webserver
                string filePath = Path.Combine(pathToPublishedNodes, file.FileName);
                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                if (!await LoadPublishedNodesFile(filePath).ConfigureAwait(false))
                {
                    throw new Exception("Could not parse publishednodes file and publish its nodes!");
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
            IEnumerable<ConfigurationFileEntryModel> publishedNodes = await _client.GetListofPublishedNodesAsync().ConfigureAwait(false);

            List<string> publishedNodesDisplay = new List<string>();
            foreach (ConfigurationFileEntryModel entry in publishedNodes)
            {
                if (entry.OpcEvents != null)
                {
                    foreach (OpcEventOnEndpointModel node in entry.OpcEvents)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Event: " + node.Id + " Name: " + node.DisplayName);
                    }
                }

                if (entry.OpcNodes != null)
                {
                    foreach (OpcNodeOnEndpointModel node in entry.OpcNodes)
                    {
                        publishedNodesDisplay.Add("Endpoint: " + entry.EndpointUrl.ToString() + " Variable: " + node.ExpandedNodeId + " Name: " + node.DisplayName);
                    }
                }
            }

            return publishedNodesDisplay.ToArray();
        }

        private async Task<bool> LoadPublishedNodesFile(string filePath)
        {
            // load publishednodes.json file, if available
            if (System.IO.File.Exists(filePath))
            {
                _logger.LogInformation($"Loading published nodes JSON file from {filePath}...");
                X509Certificate2 certWithPrivateKey = await _app.GetAppConfig().SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null).ConfigureAwait(false);
                if (!_publishedNodesFileHandler.ParseFile(filePath, certWithPrivateKey))
                {
                    _logger.LogInformation("Could not load and parse published nodes JSON file!");
                    return false;
                }
                else
                {
                    _logger.LogInformation("Published nodes JSON file parsed successfully.");
                    return true;
                }
            }
            else
            {
                _logger.LogInformation($"Published nodes JSON file not found in {filePath}.");
                return false;
            }
        }
    }
}
