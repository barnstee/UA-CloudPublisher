
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;

    public class ConfigController : Controller
    {
        private IBrokerClient _brokerClient;
        private IBrokerClient _alternativeBrokerClient;

        private readonly IMessageProcessor _messageProcessor;
        private readonly Settings.BrokerResolver _brokerResolver;

        public ConfigController(Settings.BrokerResolver brokerResolver, IMessageProcessor messageProcessor)
        {
            _brokerResolver = brokerResolver;

            if (Settings.Instance.UseKafka)
            {
                _brokerClient = _brokerResolver("Kafka");
            }
            else
            {
                _brokerClient = _brokerResolver("MQTT");
            }

            _messageProcessor = messageProcessor;
        }

        public IActionResult Index()
        {
            return View("Index", Settings.Instance);
        }

        [HttpGet]
        public ActionResult Privacy()
        {
            return View("Privacy");
        }

        [HttpPost]
        public async Task<ActionResult> LocalCertOpen(IFormFile file)
        {
            try
            {
                if (file == null)
                {
                    throw new ArgumentException("No cert file specified!");
                }

                if (file.Length == 0)
                {
                    throw new ArgumentException("Invalid cert file specified!");
                }

                // file name validation
                new FileInfo(file.FileName);

                // store the cert on the webserver
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "customclientcert");
                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream).ConfigureAwait(false);
                }

                // update cert file hash and expiry
                X509Certificate2 cert = new X509Certificate2(filePath);
                Settings.Instance.UACertThumbprint = cert.Thumbprint;
                Settings.Instance.UACertExpiry = cert.NotAfter;
            }
            catch (Exception ex)
            {
                Settings.Instance.UACertThumbprint = ex.Message;
                Settings.Instance.UACertExpiry = DateTime.MinValue;
            }

            return View("Index", Settings.Instance);
        }

        [HttpPost]
        public IActionResult Apply(Settings settings)
        {
            if (ModelState.IsValid)
            {
                Settings.Instance = settings;
                Settings.Instance.Save();

                if (Settings.Instance.UseKafka)
                {
                    _brokerClient = _brokerResolver("Kafka");
                }
                else
                {
                    _brokerClient = _brokerResolver("MQTT");
                }

                // reconnect to broker with new settings
                _brokerClient.Connect();

                // check if we need a second broker
                if (Settings.Instance.UseAltBrokerForReceivingUAOverMQTT)
                {
                    _alternativeBrokerClient = _brokerResolver("MQTT");
                    _alternativeBrokerClient.Connect(true);
                }

                // clear metadata message cache
                _messageProcessor.ClearMetadataMessageCache();
            }

            return View("Index", Settings.Instance);
        }
    }
}
