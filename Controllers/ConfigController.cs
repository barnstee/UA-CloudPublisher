
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Opc.Ua.Cloud.Publisher.Interfaces;

    public class ConfigController : Controller
    {
        private readonly IBrokerClient _subscriber;

        public ConfigController(IBrokerClient subscriber)
        {
            _subscriber = subscriber;
        }

        public IActionResult Index()
        {
            return View("Index", Settings.Instance);
        }

        [HttpPost]
        public IActionResult Update(Settings settings)
        {
            if (ModelState.IsValid)
            {
                Settings.Instance = settings;
                Settings.Instance.Save();

                // reconnect to broker with new settings
                _subscriber.Connect();
            }

            return View("Index", Settings.Instance);
        }
    }
}
