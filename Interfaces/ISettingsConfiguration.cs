
namespace UA.MQTT.Publisher.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public interface ISettingsConfiguration
    {
        /// <summary>
        /// Flag to indicate when properties have changed
        /// </summary>
        bool PropertiesChanged { get; set; }

        /// <summary>
        /// interval for flushing log file
        /// </summary>
        TimeSpan LogFileFlushTimeSpanSec { get; set; }

        /// <summary>
        /// DeviceConnectionString
        /// </summary>
        string DeviceConnectionString { get; set; }

        /// <summary>
        /// Name of the log file.
        /// </summary>
        string LogFileName { get; set; }

        /// <summary>
        /// Name of the persistency file
        /// </summary>
        string PublisherNodePersistencyFilename { get; set; }

        /// <summary>
        /// Specifies the queue capacity for monitored item events.
        /// </summary>
        int MonitoredItemsQueueCapacity { get; set; }

        /// <summary>
        /// Specifies the message size in bytes used for hub communication.
        /// </summary>
        uint HubMessageSize { get; set; }

        /// <summary>
        /// Specifies the send interval in seconds after which a message is sent to the hub.
        /// </summary>
        int DefaultSendIntervalSeconds { get; set; }

        /// <summary>
        /// Interval in seconds to show the diagnostic info.
        /// </summary>
        int DiagnosticsInterval { get; set; }

        /// <summary>
        /// Interval in seconds for processing publishing requests
        /// </summary>
        public int PublishingProcessingInterval { get; set; }

        /// <summary>
        /// Site to be added to telemetry events, identifying the source of the event,
        /// by prepending it to the ApplicationUri value of the event.
        /// </summary>
        string PublisherSite { get; set; }

        /// <summary>
        /// SuppressedOpcStatusCodes
        /// </summary>
        List<uint> SuppressedOpcStatusCodes { get; set; }

        /// <summary>
        /// Auto Accept Certs
        /// </summary>
        bool AutoAcceptCerts { get; set; }

        /// <summary>
        /// Default OPC UA Sampling Interval in milliseconds
        /// </summary>
        int DefaultOpcPublishingInterval { get; set; }

        /// <summary>
        /// Default OPC UA Publishing Internal in milliseconds
        /// </summary>
        int DefaultOpcSamplingInterval { get; set; }

        /// <summary>
        /// Use secuirty by default for all sessions
        /// </summary>
        bool UseSecurity { get; set; }

        /// <summary>
        /// OPC UA Stack Trace Mask
        /// </summary>
        int UAStackTraceMask { get; set; }

        /// <summary>
        /// Use OPC UA PubSub JSON reversable or non-reversable encoding (basically sending type info or not)
        /// </summary>
        bool ReversiblePubSubEncoding { get; set; }

        /// <summary>
        /// Lock to synchonise making changes to settings
        /// </summary>
        SemaphoreSlim SettingsLock { get; set; }

        /// <summary>
        /// Name of UA-MQTT-Publisher, either by iot edge environment variable
        /// or based on assembly version
        /// </summary>
        string PublisherName { get; }

        /// <summary>
        /// Path to UA-MQTT-Publisher application data directory
        /// </summary>
        string AppDataPath { get; set; }
    }
}