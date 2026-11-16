
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Configuration;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAApplication : IUAApplication
    {
        private readonly ILogger _logger;

        public ConsoleTelemetry Telemetry { get; } = new();

        public Certificate IssuerCert { get; set; }

        public ApplicationInstance UAApplicationInstance { get; set; }

        public ReverseConnectManager ReverseConnectManager { get; set; }

        public UAApplication(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Creating OPC UA app named {Settings.Instance.PublisherName}");

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // create UA app
            UAApplicationInstance = new ApplicationInstance(Telemetry)
            {
                ApplicationName = Settings.Instance.PublisherName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UACloudPublisher"
            };

            ReverseConnectManager = new(Telemetry);

            // overwrite app name in UA config file, while removing spaces from the app name
            // The replacement must be idempotent: the file is rewritten on every start, and if the app name contains
            // the placeholder (e.g. "UACloudPublisher.local"), a plain Replace() would append the suffix again each time
            // ("UACloudPublisher.local.local..."). Match the placeholder plus any number of already-applied suffixes
            // and replace the whole thing, which also heals files corrupted by earlier runs.
            const string placeholder = "UACloudPublisher";
            string appName = UAApplicationInstance.ApplicationName.Replace(" ", string.Empty);
            string pattern = Regex.Escape(placeholder);
            if (appName.StartsWith(placeholder, StringComparison.Ordinal) && appName.Length > placeholder.Length)
            {
                pattern += "(?:" + Regex.Escape(appName.Substring(placeholder.Length)) + ")*";
            }

            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "UACloudPublisher.Config.xml");
            string fileContent = File.ReadAllText(configFilePath);
            string newContent = Regex.Replace(fileContent, pattern, appName);
            if (newContent != fileContent)
            {
                File.WriteAllText(configFilePath, newContent);
            }

            // now load UA config file
            await UAApplicationInstance.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            // check the application certificate
            bool certOK = await UAApplicationInstance.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }
            else
            {
                // only the public certificate is needed for the thumbprint and expiry; the directory store
                // enumerates public certificates, so requesting the private key here would not find it
                using var applicationCertificate = await CertificateIdentifierResolver.ResolveAsync(
                    UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate,
                    needPrivateKey: false,
                    telemetry: Telemetry).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The OPC UA application certificate could not be resolved.");

                Settings.Instance.UAClientCertThumbprint = applicationCertificate.Thumbprint;
                Settings.Instance.UAClientCertExpiry = applicationCertificate.NotAfter;
            }

            _logger.LogInformation($"Application Certificate subject name is: {UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            // Preserve the existing policy of accepting untrusted OPC UA server certificates.
            UAApplicationInstance.ApplicationConfiguration.CertificateManager.AcceptError = (certificate, error) =>
            {
                if (error.StatusCode != StatusCodes.BadCertificateUntrusted)
                {
                    return false;
                }

                _logger.LogInformation($"Auto-accepting untrusted OPC UA server certificate {certificate.Subject}");
                return true;
            };

            await CreateIssuerCertAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Creating reverse connection endpoint on local port 50000.");
            ReverseConnectManager.AddEndpoint(new Uri("opc.tcp://localhost:50000"));
            await ReverseConnectManager.StartServiceAsync(UAApplicationInstance.ApplicationConfiguration, cancellationToken).ConfigureAwait(false);
        }

        private async Task CreateIssuerCertAsync(CancellationToken cancellationToken = default)
        {
            string pathToIssuerStore = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "private");
            if (!Directory.Exists(pathToIssuerStore))
            {
                Directory.CreateDirectory(pathToIssuerStore);
            }

            string[] issuerCerts = Directory.GetFiles(pathToIssuerStore);
            if ((issuerCerts == null) || (issuerCerts.Count() == 0))
            {
                _logger.LogError("Could not load issuer cert file, creating a new one. This means all connected OPC UA servers need to be issued a new cert!");

                string subjectName = "CN=" + Settings.Instance.PublisherName + ", O=OPC Foundation";
                IssuerCert = await DefaultCertificateFactory.Instance.CreateCertificate(subjectName)
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
                IssuerCert = Certificate.From(X509CertificateLoader.LoadPkcs12FromFile(issuerCerts[0], string.Empty));
            }

            // also add the issuer cert to our trusted store (public key only) so that certificates
            // issued by it are trusted during certificate chain validation
            try
            {
                using var issuerPublicKey = Certificate.FromRawData(IssuerCert.RawData);
                await UAApplicationInstance.AddOwnCertificateToTrustedStoreAsync(issuerPublicKey, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation($"Added issuer certificate {issuerPublicKey.Subject} [{issuerPublicKey.Thumbprint}] to the trusted store.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not add the issuer certificate to the trusted store.");
            }

            Settings.Instance.UAIssuerCertThumbprint = IssuerCert.Thumbprint;
            Settings.Instance.UAIssuerCertExpiry = IssuerCert.NotAfter;
            Settings.Instance.Save();
        }
    }
}
