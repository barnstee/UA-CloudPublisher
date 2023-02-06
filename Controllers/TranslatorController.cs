
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
        public IActionResult Load(IFormFile file, string endpointUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(endpointUrl))
                {
                    throw new ArgumentException("The endpoint URL specified is invalid!");
                }

                if (file == null)
                {
                    throw new ArgumentException("No file specified!");
                }

                if (file.Length == 0)
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                string payload = string.Empty;
                using (Stream content = file.OpenReadStream())
                {
                    byte[] bytes = new byte[file.Length];
                    content.Read(bytes, 0, (int)file.Length);
                    payload = Encoding.UTF8.GetString(bytes);
                }

                if (string.IsNullOrEmpty(payload))
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                _client.ExecuteCommand("ConfigureAsset", "AssetManagement", "http://opcfoundation.org/UA/EdgeTranslator/", payload, endpointUrl);

                return View("Index", "UA Edge Translator configured successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", ex.Message);
            }
        }
    }
}
