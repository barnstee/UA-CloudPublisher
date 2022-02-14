
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using UA.MQTT.Publisher.Interfaces;

    public class PublishedController : Controller
    {
        private readonly ILogger _logger;
        private readonly IPublishedNodesFileHandler _publishedNodesFileHandler;
        private readonly IUAApplication _app;

        public PublishedController(ILoggerFactory loggerFactory, IPublishedNodesFileHandler publishedNodesFileHandler, IUAApplication app)
        {
            _logger = loggerFactory.CreateLogger("PublishedController");
            _publishedNodesFileHandler = publishedNodesFileHandler;
            _app = app;
        }

        public IActionResult Index()
        {
            return View();
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

                LoadPublishedNodesFile(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

            return View("Index");
        }

        private void LoadPublishedNodesFile(string filePath)
        {
            // load publishednodes.json file, if available
            if (System.IO.File.Exists(filePath))
            {
                _logger.LogInformation($"Loading published nodes JSON file from {filePath}...");
                X509Certificate2 certWithPrivateKey = _app.GetAppConfig().SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null).GetAwaiter().GetResult();
                if (!_publishedNodesFileHandler.ParseFile(filePath, certWithPrivateKey))
                {
                    _logger.LogInformation("Could not load and parse published nodes JSON file!");
                }
                else
                {
                    _logger.LogInformation("Published nodes JSON file parsed successfully.");
                }
            }
            else
            {
                _logger.LogInformation($"Published nodes JSON file not found in {filePath}.");
            }
        }
    }
}
