
namespace UA.MQTT.Publisher.Configuration
{
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using UA.MQTT.Publisher.Interfaces;

    public class SettingsConfiguration : ISettingsConfiguration
    {
        /// <summary>
        /// Flag to indicate when properties have changed
        /// </summary>
        public bool PropertiesChanged { get; set; } = false;

        /// <summary>
        /// interval for flushing log file
        /// </summary>
        public TimeSpan LogFileFlushTimeSpanSec { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// DeviceConnectionString
        /// </summary>
        public string DeviceConnectionString { get; set; } = null;

        /// <summary>
        /// Path to UA-MQTT-Publisher application data directory
        /// </summary>
        public string AppDataPath { get; set; } = _appDataPath;

        /// <summary>
        /// Name of the log file.
        /// </summary>
        public string LogFileName { get; set; } = _appDataPath + $"{Utils.GetHostName()}-publisher.log";

        /// <summary>
        /// Name of the persistency file
        /// </summary>
        public string PublisherNodePersistencyFilename { get; set; } = _appDataPath + $"{Utils.GetHostName()}-persistency.json";

        /// <summary>
        /// Flag indicating if we are running in an IoT Edge context
        /// </summary>
        public bool RunningInIoTEdgeContext { get; } = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME")) &&
                                                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IOTEDGE_MODULEGENERATIONID")) &&
                                                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI")) &&
                                                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID")) &&
                                                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IOTEDGE_MODULEID")) ||
                                                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EdgeHubConnectionString"));

        /// <summary>
        /// Name of UA-MQTT-Publisher, either by iot edge environment variable
        /// or based on assembly version
        /// </summary>
        public string PublisherName
        {
            get
            {
                string moduleId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");
                return !string.IsNullOrEmpty(moduleId)
                    ? moduleId
                    : "publisher-" + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        /// <summary>
        /// Specifies the queue capacity for monitored item events.
        /// </summary>
        public int MonitoredItemsQueueCapacity
        {
            get
            {
                return _monitoredItemsQueueCapacity;
            }
            set
            {
                SettingsLock.Wait();
                _monitoredItemsQueueCapacity = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }
        /// <summary>
        /// Specifies the message size in bytes used for hub communication.
        /// </summary>
        public uint HubMessageSize
        {
            get
            {
                return _hubMessageSize;
            }
            set
            {
                SettingsLock.Wait();
                _hubMessageSize = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }
        /// <summary>
        /// Specifies the send interval in seconds after which a message is sent to the hub.
        /// </summary>
        public int DefaultSendIntervalSeconds
        {
            get
            {
                return _defaultSendIntervalSeconds;
            }
            set
            {
                SettingsLock.Wait();
                _defaultSendIntervalSeconds = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Interval in seconds to show the diagnostic info.
        /// </summary>
        public int DiagnosticsInterval { get; set; } = 10;

        /// <summary>
        /// Interval in seconds for processing publishing requests
        /// </summary>
        public int PublishingProcessingInterval { get; set; } = 5;

        /// <summary>
        /// Site to be added to telemetry events, identifying the source of the event,
        /// by prepending it to the ApplicationUri value of the event.
        /// </summary>
        public string PublisherSite
        {
            get
            {
                return _publisherSite;
            }
            set
            {
                SettingsLock.Wait();
                _publisherSite = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// SuppressedOpcStatusCodes
        /// </summary>
        public List<uint> SuppressedOpcStatusCodes
        {
            get
            {
                return _suppressedOpcStatusCodes;
            }
            set
            {
                SettingsLock.Wait();
                _suppressedOpcStatusCodes = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Auto Accept Certs
        /// </summary>
        public bool AutoAcceptCerts
        {
            get
            {
                return _autoAcceptCerts;
            }
            set
            {
                SettingsLock.Wait();
                _autoAcceptCerts = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Default OPC UA Sampling Interval in milliseconds
        /// </summary>
        public int DefaultOpcSamplingInterval
        {
            get
            {
                return _defaultOpcSamplingInterval;
            }
            set
            {
                SettingsLock.Wait();
                _defaultOpcSamplingInterval = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Default OPC UA Publishing Internal in milliseconds
        /// </summary>
        public int DefaultOpcPublishingInterval
        {
            get
            {
                return _defaultOpcPublishingInterval;
            }
            set
            {
                SettingsLock.Wait();
                _defaultOpcPublishingInterval = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Use security by default for all sessions
        /// </summary>
        public bool UseSecurity
        {
            get
            {
                return _useSecurity;
            }
            set
            {
                SettingsLock.Wait();
                _useSecurity = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// OPC UA Stack Trace Mask
        /// </summary>
        public int UAStackTraceMask
        {
            get
            {
                return _uaStackTraceMask;
            }
            set
            {
                SettingsLock.Wait();
                _uaStackTraceMask = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Use OPC UA PubSub JSON reversable or non-reversable encoding (basically sending type info or not)
        /// </summary>
        public bool ReversiblePubSubEncoding
        {
            get
            {
                return _reversiblePubSubEncoding;
            }
            set
            {
                SettingsLock.Wait();
                _reversiblePubSubEncoding = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Flag indicating that UA-MQTT-Publisher is ready to be "hydrated" from the cloud
        /// </summary>
        public bool ReadyForHydration
        {
            get
            {
                return _readyForHydration;
            }
            set
            {
                SettingsLock.Wait();
                _readyForHydration = value;
                PropertiesChanged = true;
                SettingsLock.Release();
            }
        }

        /// <summary>
        /// Lock to synchonise making changes to settings
        /// </summary>
        public SemaphoreSlim SettingsLock { get; set; } = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Max allowed payload of an IoT Hub direct method call response.
        /// </summary>
        public const int MaxResponsePayloadLength = (128 * 1024) - 256;

        /// <summary>
        /// HeartbeatIntvervalMax
        /// </summary>
        public const int HeartbeatIntvervalMax = 24 * 60 * 60;

        /// <summary>
        /// SuppressedOpcStatusCodesDefault
        /// </summary>
        public const string SuppressedOpcStatusCodesDefault = "BadNoCommunication, BadWaitingForInitialData";

        /// <summary>
        /// Specifies max message size in byte for hub communication allowed.
        /// </summary>
        public const uint HubMessageSizeMax = 256 * 1024;

        /// <summary>
        /// Settings that can be dynamically changed and applied while UA-MQTT-Publisher is running without requiring a restart to be applied
        /// </summary>
        private int _monitoredItemsQueueCapacity = 20000;
        private uint _hubMessageSize = HubMessageSizeMax;
        private int _defaultSendIntervalSeconds = 10;
        private string _publisherSite = string.Empty;
        private List<uint> _suppressedOpcStatusCodes = new List<uint>();
        private bool _autoAcceptCerts = true;
        private int _defaultOpcSamplingInterval = 1000;
        private int _defaultOpcPublishingInterval = 1000;
        private bool _useSecurity = true;
        private int _uaStackTraceMask = 0x0;
        private bool _reversiblePubSubEncoding = true;
        private bool _readyForHydration = false;
        private static string _appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                           + Path.DirectorySeparatorChar
                                           + "UA-MQTT-Publisher"
                                           + Path.DirectorySeparatorChar;
    }
}
