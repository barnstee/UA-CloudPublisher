
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Configuration;
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAApplication : IUAApplication
    {
        private readonly ILogger _logger;

        public X509Certificate2 IssuerCert { get; set; }

        public ApplicationInstance UAApplicationInstance { get; set; }

        public ReverseConnectManager ReverseConnectManager { get; set; } = new();

        public UAApplication(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Creating OPC UA app named {Settings.Instance.PublisherName}");

            // create UA app
            UAApplicationInstance = new ApplicationInstance
            {
                ApplicationName = Settings.Instance.PublisherName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UACloudPublisher"
            };

            // overwrite app name in UA config file, while removing spaces from the app name
            string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UACloudPublisher.Config.xml"));
            fileContent = fileContent.Replace("UACloudPublisher", UAApplicationInstance.ApplicationName.Replace(" ", string.Empty));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UACloudPublisher.Config.xml"), fileContent);

            // now load UA config file
            await UAApplicationInstance.LoadApplicationConfiguration(false).ConfigureAwait(false);

            // set trace masks
            UAApplicationInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks = Settings.Instance.UAStackTraceMask;
            Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(OpcStackLoggingHandler);
            _logger.LogInformation($"OPC UA stack trace mask set to: 0x{Settings.Instance.UAStackTraceMask:X}");

            // check the application certificate
            bool certOK = await UAApplicationInstance.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }
            else
            {
                // store UA cert thumbprint
                Settings.Instance.UAClientCertThumbprint = UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.Thumbprint;
                Settings.Instance.UAClientCertExpiry = UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.NotAfter;
            }

            _logger.LogInformation($"Application Certificate subject name is: {UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            await CreateIssuerCert().ConfigureAwait(false);

            _logger.LogInformation("Creating reverse connection endpoint on local port 50000.");
            ReverseConnectManager.AddEndpoint(new Uri("opc.tcp://localhost:50000"));
            ReverseConnectManager.StartService(UAApplicationInstance.ApplicationConfiguration);
        }

        private async Task CreateIssuerCert()
        {
            string pathToIssuerStore = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "private");
            if (!Directory.Exists(pathToIssuerStore))
            {
                Directory.CreateDirectory(pathToIssuerStore);
            }

            string[] issuerCerts = Directory.GetFiles(pathToIssuerStore);
            if ((issuerCerts == null) || (issuerCerts.Count() == 0))
            {
                _logger.LogError("Could not load issuer cert file, creating a new one. This means all conected OPC UA servers need to be issued a new cert!");

                string subjectName = "CN=" + Settings.Instance.PublisherName + ", O=OPC Foundation";
                IssuerCert = await CertificateFactory.CreateCertificate(subjectName)
                  .SetNotBefore(DateTime.Today.AddDays(-1))
                  .SetLifeTime(12)
                  .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(2048))
                  .SetCAConstraint()
                  .SetRSAKeySize(2048)
                  .CreateForRSA()
                  .AddToStoreAsync(CertificateStoreType.Directory, Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer"))
                  .ConfigureAwait(false);
            }
            else
            {
                IssuerCert = new X509Certificate2(File.ReadAllBytes(issuerCerts[0]));
            }

            Settings.Instance.UAIssuerCertThumbprint = IssuerCert.Thumbprint;
            Settings.Instance.UAIssuerCertExpiry = IssuerCert.NotAfter;
        }

        private void OpcStackLoggingHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & Settings.Instance.UAStackTraceMask) != 0)
            {
                if (e.Exception != null)
                {
                    _logger.LogError(e.Exception, e.Format, e.Arguments);
                    return;
                }

                switch (e.TraceMask)
                {
                    case Utils.TraceMasks.StartStop:
                    case Utils.TraceMasks.Information: _logger.LogInformation(e.Format, e.Arguments); break;
                    case Utils.TraceMasks.Error: _logger.LogError(e.Format, e.Arguments); break;
                    case Utils.TraceMasks.StackTrace:
                    case Utils.TraceMasks.Security: _logger.LogWarning(e.Format, e.Arguments); break;
                    default: _logger.LogTrace(e.Format, e.Arguments); break;
                }
            }
        }
    }
}
