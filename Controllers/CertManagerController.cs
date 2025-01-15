
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
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
            return View("Index", new CertManagerModel() { Certs = new SelectList(LoadTrustlist()) });
        }

        private List<string> LoadTrustlist()
        {
            List<string> trustList = new();
            CertificateTrustList ownTrustList = _app.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates;
            foreach (X509Certificate2 cert in ownTrustList.GetCertificates().GetAwaiter().GetResult())
            {
                trustList.Add(cert.Subject + " [" + cert.Thumbprint + "] ");
            }

            return trustList;
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
                    content.ReadExactly(bytes, 0, (int)file.Length);

                    if (file.FileName.ToLower().EndsWith(".pfx"))
                    {
                        certificate = X509CertificateLoader.LoadPkcs12(bytes, string.Empty);
                    }
                    else
                    {
                        certificate = X509CertificateLoader.LoadCertificate(bytes);
                    }
                }

                // store in our own trust list
                await _app.UAApplicationInstance.AddOwnCertificateToTrustedStoreAsync(certificate, CancellationToken.None).ConfigureAwait(false);

                return View("Index", new CertManagerModel() { Certs = new SelectList(LoadTrustlist()) });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new CertManagerModel() { Certs = new SelectList(new List<string>() { ex.Message }) });
            }
        }

        [HttpPost]
        public ActionResult DownloadTrustlist()
        {
            try
            {
                string zipfile = "trustlist.zip";

                if (System.IO.File.Exists(zipfile))
                {
                    System.IO.File.Delete(zipfile);
                }

                string pathToTrustList = Path.Combine(Directory.GetCurrentDirectory(), "pki", "trusted", "certs");
                ZipFile.CreateFromDirectory(pathToTrustList, zipfile);

                return File(System.IO.File.ReadAllBytes(zipfile), "APPLICATION/octet-stream", zipfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new CertManagerModel() { Certs = new SelectList(new List<string>() { ex.Message }) });
            }
        }
        [HttpPost]
        public ActionResult EncryptString(string plainTextString)
        {
            try
            {
                X509Certificate2 cert = _app.IssuerCert;
                using RSA rsa = cert.GetRSAPublicKey();
                if (!string.IsNullOrEmpty(plainTextString) && (rsa != null))
                {
                    return View("Index", new CertManagerModel() { Encrypt = Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(plainTextString), RSAEncryptionPadding.Pkcs1)), Certs = new SelectList(LoadTrustlist()) });
                }
                else
                {
                    throw new Exception("Encryption failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", new CertManagerModel() { Certs = new SelectList(new List<string>() { ex.Message }) });
            }
        }
    }
}
