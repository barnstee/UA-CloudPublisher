
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    /// <summary>
    /// Logs periodic diagnostics info
    /// </summary>
    public class PeriodicDiagnosticsInfo : IPeriodicDiagnosticsInfo
    {
        public PeriodicDiagnosticsInfo(ILoggerFactory loggerFactory, ISettingsConfiguration settingsConfiguration)
        {
            _logger = loggerFactory.CreateLogger("PeriodicDiagnosticsInfo");
            _settingsConfiguration = settingsConfiguration;
        }

        /// <summary>
        /// The Diagnostic info
        /// </summary>
        public DiagnosticInfo Info { get; set; } = new DiagnosticInfo();

        /// <summary>
        /// Clear all metrics
        /// </summary>
        public void Clear()
        {
            Info.PublisherStartTime = DateTime.UtcNow;
            Info.NumberOfOpcSessionsConnected = 0;
            Info.NumberOfOpcSubscriptionsConnected = 0;
            Info.NumberOfOpcMonitoredItemsMonitored = 0;
            Info.MonitoredItemsQueueCount = 0;
            Info.EnqueueCount = 0;
            Info.EnqueueFailureCount = 0;
            Info.NumberOfEvents = 0;
            Info.MissedSendIntervalCount = 0;
            Info.TooLargeCount = 0;
            Info.SentBytes = 0;
            Info.SentMessages = 0;
            Info.SentLastTime = DateTime.UtcNow;
            Info.FailedMessages = 0;
            Info.AverageNotificationsInHubMessage = 0;
        }

        /// <summary>
        /// Kicks of the task to show diagnostic information
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if ( _settingsConfiguration.DiagnosticsInterval == 0)
            {
                // period diagnostics are disabled
                return;
            }

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(_settingsConfiguration.DiagnosticsInterval * 1000, cancellationToken).ConfigureAwait(false);

                    float messagesPerSecond = ((float)(Info.SentMessages - _lastNumMessagesSent)) / _settingsConfiguration.DiagnosticsInterval;

                    _logger.LogInformation("==========================================================================");
                    _logger.LogInformation($"UA-MQTT-Publisher status for {Info.AssetID} telemetry pipeline @ {DateTime.UtcNow} (started @ {Info.PublisherStartTime})");
                    _logger.LogInformation("---------------------------------");
                    _logger.LogInformation($"OPC UA sessions: {Info.NumberOfOpcSessionsConnected}");
                    _logger.LogInformation($"OPC UA subscriptions: {Info.NumberOfOpcSubscriptionsConnected}");
                    _logger.LogInformation($"OPC UA monitored items: {Info.NumberOfOpcMonitoredItemsMonitored}");
                    _logger.LogInformation("---------------------------------");
                    _logger.LogInformation($"OPC UA monitored items queue capacity: {_settingsConfiguration.MonitoredItemsQueueCapacity}");
                    _logger.LogInformation($"OPC UA monitored items queue current items: {Info.MonitoredItemsQueueCount}");
                    _logger.LogInformation($"OPC UA monitored item notifications enqueued: {Info.EnqueueCount}");
                    _logger.LogInformation($"OPC UA monitored item notifications enqueue failure: {Info.EnqueueFailureCount}");
                    _logger.LogInformation("---------------------------------");
                    _logger.LogInformation($"Messages sent to IoT Hub: {Info.SentMessages}");
                    _logger.LogInformation($"Last successful IoT Hub message sent @: {Info.SentLastTime}");
                    _logger.LogInformation($"Total bytes sent to IoT Hub: {Info.SentBytes}");
                    _logger.LogInformation($"Average IoT Hub message size (bytes): {Info.SentBytes / (Info.SentMessages == 0 ? 1 : Info.SentMessages)}");
                    _logger.LogInformation($"Average IoT Hub message latency (ms): {Info.AverageMessageLatency}");
                    _logger.LogInformation($"Average IoT Hub messages/second sent: {messagesPerSecond}");
                    _logger.LogInformation($"Average number of OPC UA notifications batched in IoT Hub message: {Info.AverageNotificationsInHubMessage}");
                    _logger.LogInformation($"Average number of OPC UA notifications/second sent: {messagesPerSecond * Info.AverageNotificationsInHubMessage}");
                    _logger.LogInformation($"IoT Hub message send failures: {Info.FailedMessages}");
                    _logger.LogInformation($"IoT Hub messages too large to sent to IoT Hub: {Info.TooLargeCount}");
                    _logger.LogInformation($"Missed IoT Hub message send intervals: {Info.MissedSendIntervalCount}");
                    _logger.LogInformation($"Number of OPC UA notifications encoded: {Info.NumberOfEvents}");
                    _logger.LogInformation("---------------------------------");
                    _logger.LogInformation($"Current working set in MB: {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)}");
                    _logger.LogInformation($"IoT Hub send interval setting: {_settingsConfiguration.DefaultSendIntervalSeconds}");
                    _logger.LogInformation($"IoT Hub message size setting: {_settingsConfiguration.HubMessageSize}");
                    _logger.LogInformation($"IoT Hub protocol setting: {Info.HubProtocol}");
                    _logger.LogInformation("==========================================================================");

                    _lastNumMessagesSent = Info.SentMessages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "writing diagnostics output causing error");
                }
            }
        }

        private readonly ILogger _logger;
        private readonly ISettingsConfiguration _settingsConfiguration;
        private long _lastNumMessagesSent = 0;
    }
}
