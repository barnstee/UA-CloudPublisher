
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Models;

    public class Diagnostics
    {
        private readonly ILogger _logger;
        
        private StatusHub _hub = new StatusHub();

        private long _lastNumMessagesSent = 0;

        private static Diagnostics _instance = null;
        private static object _instanceLock = new object();

        private Diagnostics()
        {
            ILoggerFactory loggerFactory = (ILoggerFactory)Program.AppHost.Services.GetService(typeof(ILoggerFactory));
            _logger = loggerFactory.CreateLogger("Diagnostics");
        }

        public static Diagnostics Singleton
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new Diagnostics();
                        }
                    }
                }

                return _instance;
            }
       
        }

        public DiagnosticInfo Info { get; set; } = new DiagnosticInfo();

        private void Clear()
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
            Info.AverageNotificationsInBrokerMessage = 0;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if ( Settings.Singleton.DiagnosticsLoggingInterval == 0)
            {
                // diagnostics are disabled
                return;
            }

            Clear();

            uint ticks = 0;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                ticks++;

                try
                {
                    await Task.Delay((int)Settings.Singleton.DiagnosticsLoggingInterval * 100, cancellationToken).ConfigureAwait(false);

                    float messagesPerSecond = ((float)(Info.SentMessages - _lastNumMessagesSent)) / Settings.Singleton.DiagnosticsLoggingInterval;
                    List<string> chartValues = new List<string>();

                    _hub.AddOrUpdateTableEntry("Publisher Start Time", Info.PublisherStartTime.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA sessions", Info.NumberOfOpcSessionsConnected.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA subscriptions", Info.NumberOfOpcSubscriptionsConnected.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA monitored items", Info.NumberOfOpcMonitoredItemsMonitored.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA monitored items queue capacity", Settings.Singleton.InternalQueueCapacity.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA monitored items queue current items", Info.MonitoredItemsQueueCount.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA monitored item notifications enqueued", Info.EnqueueCount.ToString());
                    _hub.AddOrUpdateTableEntry("OPC UA monitored item notifications enqueue failure", Info.EnqueueFailureCount.ToString());
                    _hub.AddOrUpdateTableEntry("Messages sent to MQTT broker", Info.SentMessages.ToString());
                    _hub.AddOrUpdateTableEntry("Last successful MQTT broker message sent @", Info.SentLastTime.ToString());
                    _hub.AddOrUpdateTableEntry("Total bytes sent to MQTT broker", Info.SentBytes.ToString());
                    _hub.AddOrUpdateTableEntry("Average MQTT broker message size (bytes)", (Info.SentBytes / (Info.SentMessages == 0 ? 1 : Info.SentMessages)).ToString());
                    _hub.AddOrUpdateTableEntry("Average MQTT broker message latency (ms)", Info.AverageMessageLatency.ToString(), true);
                    _hub.AddOrUpdateTableEntry("Average MQTT broker messages/second sent", messagesPerSecond.ToString(), true);
                    _hub.AddOrUpdateTableEntry("Average number of notifications batched in MQTT broker message", Info.AverageNotificationsInBrokerMessage.ToString());
                    _hub.AddOrUpdateTableEntry("Average number of OPC UA notifications/second sent", (messagesPerSecond * Info.AverageNotificationsInBrokerMessage).ToString(), true);
                    _hub.AddOrUpdateTableEntry("MQTT broker message send failures", Info.FailedMessages.ToString());
                    _hub.AddOrUpdateTableEntry("MQTT broker messages too large to send to MQTT broker", Info.TooLargeCount.ToString());
                    _hub.AddOrUpdateTableEntry("Missed MQTT broker message send intervals", Info.MissedSendIntervalCount.ToString());
                    _hub.AddOrUpdateTableEntry("Number of OPC UA notifications encoded", Info.NumberOfEvents.ToString());
                    _hub.AddOrUpdateTableEntry("Current working set in MB", (Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)).ToString());
                    _hub.AddOrUpdateTableEntry("MQTT broker send interval setting (s)", Settings.Singleton.DefaultSendIntervalSeconds.ToString());
                    _hub.AddOrUpdateTableEntry("MQTT broker message size setting (bytes)", Settings.Singleton.MQTTMessageSize.ToString());
                    
                    chartValues.Add(Info.AverageMessageLatency.ToString());
                    chartValues.Add(messagesPerSecond.ToString());
                    chartValues.Add((messagesPerSecond * Info.AverageNotificationsInBrokerMessage).ToString());
                    _hub.AddChartEntry(DateTime.UtcNow.ToString(), chartValues.ToArray());

                    // write to the log at 10x slower than the UI diagnostics
                    if (ticks % 10 == 0)
                    {
                        _logger.LogInformation("==========================================================================");
                        _logger.LogInformation($"UA-MQTT-Publisher started @ {Info.PublisherStartTime}");
                        _logger.LogInformation("---------------------------------");
                        _logger.LogInformation($"OPC UA sessions: {Info.NumberOfOpcSessionsConnected}");
                        _logger.LogInformation($"OPC UA subscriptions: {Info.NumberOfOpcSubscriptionsConnected}");
                        _logger.LogInformation($"OPC UA monitored items: {Info.NumberOfOpcMonitoredItemsMonitored}");
                        _logger.LogInformation("---------------------------------");
                        _logger.LogInformation($"OPC UA monitored items queue capacity: {Settings.Singleton.InternalQueueCapacity}");
                        _logger.LogInformation($"OPC UA monitored items queue current items: {Info.MonitoredItemsQueueCount}");
                        _logger.LogInformation($"OPC UA monitored item notifications enqueued: {Info.EnqueueCount}");
                        _logger.LogInformation($"OPC UA monitored item notifications enqueue failure: {Info.EnqueueFailureCount}");
                        _logger.LogInformation("---------------------------------");
                        _logger.LogInformation($"Messages sent to MQTT broker: {Info.SentMessages}");
                        _logger.LogInformation($"Last successful MQTT broker message sent @: {Info.SentLastTime}");
                        _logger.LogInformation($"Total bytes sent to MQTT broker: {Info.SentBytes}");
                        _logger.LogInformation($"Average MQTT broker message size (bytes): {Info.SentBytes / (Info.SentMessages == 0 ? 1 : Info.SentMessages)}");
                        _logger.LogInformation($"Average MQTT broker message latency (ms): {Info.AverageMessageLatency}");
                        _logger.LogInformation($"Average MQTT broker messages/second sent: {messagesPerSecond}");
                        _logger.LogInformation($"Average number of notifications batched in MQTT broker message: {Info.AverageNotificationsInBrokerMessage}");
                        _logger.LogInformation($"Average number of OPC UA notifications/second sent: {messagesPerSecond * Info.AverageNotificationsInBrokerMessage}");
                        _logger.LogInformation($"MQTT broker message send failures: {Info.FailedMessages}");
                        _logger.LogInformation($"MQTT broker messages too large to send to MQTT broker: {Info.TooLargeCount}");
                        _logger.LogInformation($"Missed MQTT broker message send intervals: {Info.MissedSendIntervalCount}");
                        _logger.LogInformation($"Number of OPC UA notifications encoded: {Info.NumberOfEvents}");
                        _logger.LogInformation("---------------------------------");
                        _logger.LogInformation($"Current working set in MB: {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)}");
                        _logger.LogInformation($"MQTT broker send interval setting (s): {Settings.Singleton.DefaultSendIntervalSeconds}");
                        _logger.LogInformation($"MQTT broker message size setting (bytes): {Settings.Singleton.MQTTMessageSize}");
                        _logger.LogInformation("==========================================================================");
                    }

                    _lastNumMessagesSent = Info.SentMessages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "writing diagnostics output causing error");
                }
            }
        }
    }
}
