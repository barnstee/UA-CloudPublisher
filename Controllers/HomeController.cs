
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Opc.Ua.Cloud.Publisher.Models;

    public class HomeController : Controller
    {
        public static string AuthenticationCode { get; set; } = "Not applicable";

        public IActionResult Index()
        {
            return View("Index", new HomeModel() { AuthCode = AuthenticationCode });
        }

        [HttpGet]
        public ActionResult Privacy()
        {
            return View("Privacy");
        }
    }
}
