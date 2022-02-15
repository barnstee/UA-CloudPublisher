
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System;
    using System.IO;

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
                            _instance = Load();
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

        private static Settings Load()
        {
            try
            {
                return JsonConvert.DeserializeObject<Settings>(File.ReadAllText("./Settings/settings.json"));
            }
            catch (Exception ex)
            {
                // no file persisted yet
                Console.WriteLine("Creating new settings file due to: " + ex.Message);
                Settings newInstance = new Settings();
                newInstance.Save();
                return newInstance;
            }
        }

        public void Save()
        {
            File.WriteAllText("./Settings/settings.json", JsonConvert.SerializeObject(this, Formatting.Indented));
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
