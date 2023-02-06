
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.IO;
    using System.Text;

    public class TranslatorController : Controller
    {
        private readonly IUAClient _client;
        private readonly ILogger _logger;

        private static string _payload = string.Empty;

        public TranslatorController(IUAClient client, ILoggerFactory loggerFactory)
        {
            _client = client;
            _logger = loggerFactory.CreateLogger("TranslatorController");
        }

        public IActionResult Index()
        {
            return View("Index", string.Empty);
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

                if (file.Length == 0)
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                using (Stream content = file.OpenReadStream())
                {
                    byte[] bytes = new byte[file.Length];
                    content.Read(bytes, 0, (int)file.Length);
                    _payload = Encoding.UTF8.GetString(bytes);
                }

                return View("Index", "Thing Description loaded successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", ex.Message);
            }
        }

        [HttpPost]
        public ActionResult Configure(string endpointUrl)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                return View("Index", "The endpoint URL specified is invalid!");
            }

            if (string.IsNullOrEmpty(_payload))
            {
                return View("Index", "The Web of Things Thing Description is invalid!");
            }

            try
            {
                _client.ExecuteCommand("ConfigureAsset", "AssetManagement", "http://opcfoundation.org/UA/EdgeTranslator/", _payload, endpointUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", ex.Message);
            }

            return View("Index", "UA Edge Translator configured successfully!");
        }
    }
}
