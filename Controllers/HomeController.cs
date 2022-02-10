
namespace UA.MQTT.Publisher.Controllers
{
    using Microsoft.AspNetCore.Mvc;

    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Privacy()
        {
            return View("Privacy");
        }
    }
}
