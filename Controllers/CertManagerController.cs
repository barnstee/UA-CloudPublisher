
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public class CertManagerController : Controller
    {
        private readonly ILogger _logger;
        private readonly IUAApplication _app;

        public CertManagerController(IUAApplication app, ILoggerFactory loggerFactory)
        {
            _app = app;
            _logger = loggerFactory.CreateLogger("CertManagerController");
        }

        public IActionResult Index()
        {
            return LoadTrustlist();
        }

        private IActionResult LoadTrustlist()
        {
            List<string> trustList = new();
            CertificateTrustList ownTrustList = _app.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates;
            foreach (X509Certificate2 cert in ownTrustList.GetCertificates().GetAwaiter().GetResult())
            {
                trustList.Add(cert.Subject + " [" + cert.Thumbprint + "] ");
            }

            return View("Index", new SelectList(trustList));
        }

        [HttpPost]
        public async Task<IActionResult> Load(IFormFile file)
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

                X509Certificate2 certificate = null;
                using (Stream content = file.OpenReadStream())
                {
                    byte[] bytes = new byte[file.Length];
                    content.Read(bytes, 0, (int)file.Length);
                    certificate = new X509Certificate2(bytes);
                }

                // store in our own trust list
                await _app.UAApplicationInstance.AddOwnCertificateToTrustedStoreAsync(certificate, CancellationToken.None).ConfigureAwait(false);

                return LoadTrustlist();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new SelectList(new List<string>() { ex.Message }));
            }
        }
    }
}
