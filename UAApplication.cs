
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
    using UA.MQTT.Publisher.Models;

    public class UAApplication : IUAApplication
    {
        private readonly ILogger _logger;
        private readonly Settings _settings;
        private ApplicationInstance _uaApplicationInstance;

        public UAApplication(ILoggerFactory loggerFactory, Settings settings)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
            _settings = settings;
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_settings.PublisherName))
            {
                _settings.PublisherName = "UA-MQTT-Publisher";
            }

            _uaApplicationInstance = new ApplicationInstance {
                ApplicationName = _settings.PublisherName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "UA-MQTT-Publisher"
            };

            await _uaApplicationInstance.LoadApplicationConfiguration(false).ConfigureAwait(false);

            _settings.UAStackTraceMask = _uaApplicationInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks;
            Opc.Ua.Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(OpcStackLoggingHandler);
            _logger.LogInformation($"opcstacktracemask set to: 0x{_settings.UAStackTraceMask:X}");

            // check the application certificate.
            bool certOK = await _uaApplicationInstance.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
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
            if ((e.TraceMask & _settings.UAStackTraceMask) != 0)
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
