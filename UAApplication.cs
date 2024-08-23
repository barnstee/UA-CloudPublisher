
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Configuration;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAApplication : IUAApplication
    {
        private readonly ILogger _logger;
        private readonly IFileStorage _storage;

        public ApplicationInstance UAApplicationInstance { get; set; }

        public ReverseConnectManager ReverseConnectManager { get; set; } = new();

        public UAApplication(ILoggerFactory loggerFactory, IFileStorage storage)
        {
            _logger = loggerFactory.CreateLogger("UAApplication");
            _storage = storage;
        }

        public async Task CreateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Creating OPC UA app named {Settings.Instance.PublisherName}");

            try
            {
                // load app cert from storage
                string certFilePath = await _storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), Settings.Instance.PublisherName).ConfigureAwait(false);
                byte[] certFile = await _storage.LoadFileAsync(certFilePath).ConfigureAwait(false);
                if (certFile == null)
                {
                    _logger.LogError("Could not load cert file, creating a new one. This means the new cert needs to be trusted by all OPC UA servers we connect to!");
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
                    _logger.LogError("Could not load key file, creating a new one. This means the new cert generated from the key needs to be trusted by all OPC UA servers we connect to!");
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
            UAApplicationInstance = new ApplicationInstance {
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
                Settings.Instance.UACertThumbprint = UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.Thumbprint;
                Settings.Instance.UACertExpiry = UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate.NotAfter;

                // store app certs
                foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "certs"), "*.der"))
                {
                    await _storage.StoreFileAsync(filePath, await File.ReadAllBytesAsync(filePath).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }

                // store private keys
                foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "own", "private"), "*.pfx"))
                {
                    await _storage.StoreFileAsync(filePath, await File.ReadAllBytesAsync(filePath).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }

                // store trusted certs
                foreach (string filePath in Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "trusted", "certs"), "*.der"))
                {
                    await _storage.StoreFileAsync(filePath, await File.ReadAllBytesAsync(filePath).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation($"Application Certificate subject name is: {UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            _logger.LogInformation("Creating reverse connection endpoint on local port 50000.");
            ReverseConnectManager.AddEndpoint(new Uri("opc.tcp://localhost:50000"));
            ReverseConnectManager.StartService(UAApplicationInstance.ApplicationConfiguration);
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
