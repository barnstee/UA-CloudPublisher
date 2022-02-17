
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;

    public class Settings
    {
        public Settings()
        {
            // needed for serialization
        }

        private static Settings _instance = null;
        private static object _instanceLock = new object();

        public static Settings Singleton
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = Load().GetAwaiter().GetResult();
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

        private static async Task<Settings> Load()
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

            // store app cert
            if (await storage.StoreFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings", "settings.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.Indented))).ConfigureAwait(false) == null)
            {
                logger.LogError("Could not store settings file. Settings won't be persisted!");
            }
        }

        public string MQTTClientName { get; set; }

        public string MQTTBrokerName { get; set; }

        public string MQTTUsername { get; set; }

        public string MQTTPassword { get; set; }

        public string MQTTTopic { get; set; }

        public string MQTTResponseTopic { get; set; }

        public uint MQTTMessageSize { get; set; } = HubMessageSizeMax;

        public bool CreateMQTTSASToken { get; set; } = true;

        public string PublisherName { get; set; } = "UA-MQTT-Publisher";

        public uint InternalQueueCapacity { get; set; } = 1000; // records

        public uint DefaultSendIntervalSeconds { get; set; } = 1;

        public uint DiagnosticsLoggingInterval { get; set; } = 30; // seconds

        public uint DefaultOpcSamplingInterval { get; set; } = 500;

        public uint DefaultOpcPublishingInterval { get; set; } = 1000;

        public int UAStackTraceMask { get; set; } = 645; // Error, Trace, StartStop, Security

        public bool ReversiblePubSubEncoding { get; set; } = false;

        public const int MaxResponsePayloadLength = (128 * 1024) - 256;
        public const uint HubMessageSizeMax = 256 * 1024;
    }
}
