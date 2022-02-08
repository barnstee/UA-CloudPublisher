
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;

    public class UAApplication : IUAApplication
    {
        public UAApplication(ILoggerFactory loggerFactory, ISettingsConfiguration settingsConfiguration)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
            _settingsConfiguration = settingsConfiguration;
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            _uaApplicationInstance = new ApplicationInstance {
                ApplicationName = "UA-MQTT-Publisher",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UA-MQTT-Publisher"
            };

            await _uaApplicationInstance.LoadApplicationConfiguration(false).ConfigureAwait(false);

            _settingsConfiguration.UAStackTraceMask = _uaApplicationInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks;
            Opc.Ua.Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(OpcStackLoggingHandler);
            _logger.LogInformation($"opcstacktracemask set to: 0x{_settingsConfiguration.UAStackTraceMask:X}");

            // check the application certificate.
            bool certOK = await _uaApplicationInstance.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // log shopfloor site setting
            if (string.IsNullOrEmpty(_settingsConfiguration.PublisherSite))
            {
                _logger.LogInformation("There is no site configured.");
            }
            else
            {
                _logger.LogInformation($"Publisher is in site '{_settingsConfiguration.PublisherSite}'.");
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

            // check cert validation
            if (_settingsConfiguration.AutoAcceptCerts)
            {
                _logger.LogWarning("Automatically accepting OPC UA server certificates during connection handshake.");
            }

            // create cert validator
            _uaApplicationInstance.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            _uaApplicationInstance.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
        }

        public ApplicationConfiguration GetAppConfig()
        {
            return (_uaApplicationInstance != null) ? _uaApplicationInstance.ApplicationConfiguration : null;
        }

        /// <summary>
        /// Callback to validate OPC UA server certificates
        /// </summary>
        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                if (_settingsConfiguration.AutoAcceptCerts)
                {
                    // accept all OPC UA server certificates for our OPC UA client
                    _logger.LogInformation("Automatically trusting server certificate " + e.Certificate.Subject);
                    e.Accept = true;
                }
            }
        }

        /// <summary>
        /// Callback for logging OPC UA stack output
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpcStackLoggingHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & _settingsConfiguration.UAStackTraceMask) != 0)
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

        private readonly ILogger _logger;
        private readonly ISettingsConfiguration _settingsConfiguration;
        private ApplicationInstance _uaApplicationInstance;
    }
}
