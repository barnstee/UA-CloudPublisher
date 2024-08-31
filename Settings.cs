
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    public class Settings
    {
        public Settings()
        {
            // needed for serialization
        }

        public delegate IBrokerClient BrokerResolver(string key);

        private static Settings _instance = null;
        private static object _instanceLock = new object();

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = LoadAsync().GetAwaiter().GetResult();
                        }
                    }
                }

                return _instance;
            }
            set
            {
                lock (_instanceLock)
                {
                    _instance = value;
                }
            }
        }

        private static async Task<Settings> LoadAsync()
        {
            ILoggerFactory loggerFactory = (ILoggerFactory)Program.AppHost.Services.GetService(typeof(ILoggerFactory));
            ILogger logger = loggerFactory.CreateLogger("Settings");
            IFileStorage storage = (IFileStorage)Program.AppHost.Services.GetService(typeof(IFileStorage));

            try
            {
                string settingsFilePath = await storage.FindFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "settings.json").ConfigureAwait(false);
                byte[] settingsFile = await storage.LoadFileAsync(settingsFilePath).ConfigureAwait(false);
                if (settingsFile == null)
                {
                    // no file persisted yet
                    logger.LogError("Creating new settings file as none was persisted so far.");
                    Settings newInstance = new Settings();
                    newInstance.Save();
                    return newInstance;
                }
                else
                {
                    return JsonConvert.DeserializeObject<Settings>(Encoding.UTF8.GetString(settingsFile));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Loading settings file failed. Creating a new one.");
                Settings newInstance = new Settings();
                newInstance.Save();
                return newInstance;
            }
        }

        public async void Save()
        {
            ILoggerFactory loggerFactory = (ILoggerFactory)Program.AppHost.Services.GetService(typeof(ILoggerFactory));
            ILogger logger = loggerFactory.CreateLogger("Settings");
            IFileStorage storage = (IFileStorage)Program.AppHost.Services.GetService(typeof(IFileStorage));

            if (await storage.StoreFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings", "settings.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.Indented))).ConfigureAwait(false) == null)
            {
                logger.LogError("Could not store settings file. Settings won't be persisted!");
            }
        }

        public string AuthenticationCode { get; set; } = "Not applicable";

        public string UACertThumbprint { get; set; } = string.Empty;

        public DateTime UACertExpiry { get; set; } = DateTime.MinValue;

        public string PublisherName { get; set; } = "UACloudPublisher";

        public string BrokerUrl { get; set; } = string.Empty;

        public uint BrokerPort { get; set; } = 8883;

        public string BrokerUsername { get; set; } = string.Empty;

        public string BrokerPassword { get; set; } = string.Empty;

        public bool UseAltBrokerForMetadata { get; set; } = false;

        public bool UseAltBrokerForReceivingUAOverMQTT { get; set; } = false;

        public string AltBrokerUrl { get; set; } = string.Empty;

        public uint AltBrokerPort { get; set; } = 8883;

        public string AltBrokerUsername { get; set; } = string.Empty;

        public string AltBrokerPassword { get; set; } = string.Empty;

        public string BrokerMessageTopic { get; set; } = string.Empty;

        public string BrokerMetadataTopic { get; set; } = string.Empty;

        public bool SendUAMetadata { get; set; } = false;

        public bool SendUAStatus { get; set; } = false;

        public uint MetadataSendInterval { get; set; } = 30; // seconds

        public string BrokerCommandTopic { get; set; } = string.Empty;

        public string BrokerDataReceivedTopic { get; set; } = string.Empty;

        public string BrokerResponseTopic { get; set; } = string.Empty;

        public uint BrokerMessageSize { get; set; } = HubMessageSizeMax;

        public bool UseKafka { get; set; } = false;

        public bool CreateBrokerSASToken { get; set; } = false;

        public bool UseTLS { get; set; } = true;

        public bool UseUACertAuth { get; set; } = false;

        public bool UseCustomCertAuth { get; set; } = false;

        public bool UseReverseConnect { get; set; } = false;

        public bool PushCertsBeforePublishing { get; set; } = false;

        public uint InternalQueueCapacity { get; set; } = 1000; // records

        public uint DefaultSendIntervalSeconds { get; set; } = 1;

        public uint DiagnosticsLoggingInterval { get; set; } = 30; // seconds

        public uint DefaultOpcSamplingInterval { get; set; } = 500;

        public uint DefaultOpcPublishingInterval { get; set; } = 1000;

        public int UAStackTraceMask { get; set; } = 645; // Error, Trace, StartStop, Security

        public bool ReversiblePubSubEncoding { get; set; } = false;

        public bool AutoLoadPersistedNodes { get; set; } = false;

        public string AzureOpenAIAPIEndpoint { get; set; } = string.Empty;

        public string AzureOpenAIAPIKey { get; set; } = string.Empty;

        public string AzureOpenAIDeploymentName { get; set; } = string.Empty;


        public const int MaxResponsePayloadLength = (128 * 1024) - 256;
        public const uint HubMessageSizeMax = 256 * 1024;
    }
}
