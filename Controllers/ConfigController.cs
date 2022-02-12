
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using UA.MQTT.Publisher.Models;

    public class ConfigController : Controller
    {
        private Settings _settings;

        public ConfigController(Settings settings)
        {
            _settings = settings;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Update(Settings settings)
        {
            if (ModelState.IsValid)
            {
                _settings = settings;
            }

            return View();
        }
    }
}
