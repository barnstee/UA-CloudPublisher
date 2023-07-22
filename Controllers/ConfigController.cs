
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Opc.Ua.Cloud.Publisher.Interfaces;

    public class ConfigController : Controller
    {
        private readonly IBrokerClient _subscriber;
        private readonly IMessageProcessor _messageProcessor;

        public ConfigController(IBrokerClient subscriber, IMessageProcessor messageProcessor)
        {
            _subscriber = subscriber;
            _messageProcessor = messageProcessor;
        }

        public IActionResult Index()
        {
            return View("Index", Settings.Instance);
        }

        [HttpPost]
        public IActionResult Apply(Settings settings)
        {
            if (ModelState.IsValid)
            {
                Settings.Instance = settings;
                Settings.Instance.Save();

                // reconnect to broker with new settings
                _subscriber.Connect();

                // clear metadata message cache
                _messageProcessor.ClearMetadataMessageCache();
            }

            return View("Index", Settings.Instance);
        }
    }
}
