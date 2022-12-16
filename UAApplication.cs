
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Configuration;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAApplication : IUAApplication
    {
        private readonly ILogger _logger;
        private readonly IFileStorage _storage;

        private ApplicationInstance _uaApplicationInstance;

        public UAApplication(ILoggerFactory loggerFactory, IFileStorage storage)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
            _storage = storage;
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(Settings.Instance.PublisherName))
            {
                Settings.Instance.PublisherName = "UACloudPublisher";
            }

            try
            {
                // load app cert from storage
                string certFilePath = await _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), Settings.Instance.PublisherName).ConfigureAwait(false);
                byte[] certFile = await _storage.LoadFileAsync(certFilePath).ConfigureAwait(false);
                if (certFile == null)
                {
                    _logger.LogError("Cloud not load cert file, creating a new one. This means the new cert needs to be trusted by all OPC UA servers we connect to!");
                }
                else
                {
                    if (!Path.IsPathRooted(certFilePath))
                    {
                        certFilePath = Path.DirectorySeparatorChar.ToString() + certFilePath;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(certFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(certFilePath));
                    }

                    File.WriteAllBytes(certFilePath, certFile);
                }

                // load app private key from storage
                string keyFilePath = await _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "private"), Settings.Instance.PublisherName).ConfigureAwait(false);
                byte[] keyFile = await _storage.LoadFileAsync(keyFilePath).ConfigureAwait(false);
                if (keyFile == null)
                {
                    _logger.LogError("Cloud not load key file, creating a new one. This means the new cert generated from the key needs to be trusted by all OPC UA servers we connect to!");
                }
                else
                {
                    if (!Path.IsPathRooted(keyFilePath))
                    {
                        keyFilePath = Path.DirectorySeparatorChar.ToString() + keyFilePath;
                    }

                    if (!Directory.Exists(Path.GetDirectoryName(keyFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath));
                    }

                    File.WriteAllBytes(keyFilePath, keyFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud not load cert or private key files, creating a new ones. This means the new cert needs to be trusted by all OPC UA servers we connect to!");
            }

            // create UA app
            _uaApplicationInstance = new ApplicationInstance {
                ApplicationName = Settings.Instance.PublisherName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UACloudPublisher"
            };

            // overwrite app name in UA config file, while removing spaces from the app name
            string fileContent = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "UACloudPublisher.Config.xml"));
            fileContent = fileContent.Replace("UACloudPublisher", _uaApplicationInstance.ApplicationName.Replace(" ", string.Empty));
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UACloudPublisher.Config.xml"), fileContent);

            // now load UA config file
            await _uaApplicationInstance.LoadApplicationConfiguration(false).ConfigureAwait(false);
            _uaApplicationInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks = Settings.Instance.UAStackTraceMask;
            Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(OpcStackLoggingHandler);
            _logger.LogInformation($"OPC UA stack trace mask set to: 0x{Settings.Instance.UAStackTraceMask:X}");

            // check the application certificate
            bool certOK = await _uaApplicationInstance.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }
            else
            {
                // store app cert
                foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), "*.der"))
                {
                    await _storage.StoreFileAsync(filePath, await File.ReadAllBytesAsync(filePath).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }

                // store private key
                foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "private"), "*.pfx"))
                {
                    await _storage.StoreFileAsync(filePath, await File.ReadAllBytesAsync(filePath).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation($"Trusted Issuer store type is: {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StoreType}");
            _logger.LogInformation($"Trusted Issuer Certificate store path is: {Utils.ReplaceSpecialFolderNames(_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath)}");

            _logger.LogInformation($"Trusted Peer Certificate store type is: {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StoreType}");
            _logger.LogInformation($"Trusted Peer Certificate store path is: {Utils.ReplaceSpecialFolderNames(_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath)}");

            _logger.LogInformation($"Rejected certificate store type is: {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StoreType}");
            _logger.LogInformation($"Rejected Certificate store path is: {Utils.ReplaceSpecialFolderNames(_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StorePath)}");

            _logger.LogInformation($"Rejection of SHA1 signed certificates is {(_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates ? "enabled" : "disabled")}");
            _logger.LogInformation($"Minimum certificate key size set to {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize}");

            _logger.LogInformation($"Application Certificate store type is: {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StoreType}");
            _logger.LogInformation($"Application Certificate store path is: {Utils.ReplaceSpecialFolderNames(_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.StorePath)}");
            _logger.LogInformation($"Application Certificate subject name is: {_uaApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            // create cert validator
            _uaApplicationInstance.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            _uaApplicationInstance.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
        }

        public ApplicationConfiguration GetAppConfig()
        {
            return (_uaApplicationInstance != null) ? _uaApplicationInstance.ApplicationConfiguration : null;
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                // auto-accept as we are the client
                e.Accept = true;
            }
        }

        private void OpcStackLoggingHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & Settings.Instance.UAStackTraceMask) != 0)
            {
                if (e.Arguments != null)
                {
                    _logger.LogInformation("OPC UA Stack: " + string.Format(CultureInfo.InvariantCulture, e.Format, e.Arguments).Trim());
                }
                else
                {
                    _logger.LogInformation("OPC UA Stack: " + e.Format.Trim());
                }
            }
        }
    }
}
