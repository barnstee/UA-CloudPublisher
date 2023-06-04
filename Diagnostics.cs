
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Opc.Ua.Cloud.Publisher.Models;

    public class Diagnostics
    {
        private readonly ILogger _logger;
        private readonly StatusHubClient _hubClient;

        private long _lastNumMessagesSent = 0;

        private static Diagnostics _instance = null;
        private static object _instanceLock = new object();

        private Diagnostics()
        {
            ILoggerFactory loggerFactory = (ILoggerFactory)Program.AppHost.Services.GetService(typeof(ILoggerFactory));
            _logger = loggerFactory.CreateLogger("Diagnostics");
            _hubClient = new StatusHubClient((IHubContext<StatusHub>)Program.AppHost.Services.GetService(typeof(IHubContext<StatusHub>)));
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

        public DiagnosticsModel Info { get; set; } = new DiagnosticsModel();

        private void Clear()
        {
            Info.PublisherStartTime = DateTime.UtcNow;
            Info.ConnectedToBroker = false;
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
            if ( Settings.Instance.DiagnosticsLoggingInterval == 0)
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
                    await Task.Delay((int)Settings.Instance.DiagnosticsLoggingInterval * 100, cancellationToken).ConfigureAwait(false);

                    float messagesPerSecond = ((float)(Info.SentMessages - _lastNumMessagesSent)) / Settings.Instance.DiagnosticsLoggingInterval;
                    List<string> chartValues = new List<string>();

                    _hubClient.AddOrUpdateTableEntry("Publisher Start Time", Info.PublisherStartTime.ToString());
                    _hubClient.AddOrUpdateTableEntry("Connected to broker", Info.ConnectedToBroker.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA sessions", Info.NumberOfOpcSessionsConnected.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA subscriptions", Info.NumberOfOpcSubscriptionsConnected.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA monitored items", Info.NumberOfOpcMonitoredItemsMonitored.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA monitored items queue capacity", Settings.Instance.InternalQueueCapacity.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA monitored items queue current items", Info.MonitoredItemsQueueCount.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA monitored items enqueued", Info.EnqueueCount.ToString());
                    _hubClient.AddOrUpdateTableEntry("OPC UA monitored items enqueue failure", Info.EnqueueFailureCount.ToString());
                    _hubClient.AddOrUpdateTableEntry("Messages sent to broker", Info.SentMessages.ToString());
                    _hubClient.AddOrUpdateTableEntry("Last successful broker message sent @", Info.SentLastTime.ToString());
                    _hubClient.AddOrUpdateTableEntry("Total bytes sent to broker", Info.SentBytes.ToString());
                    _hubClient.AddOrUpdateTableEntry("Average broker message size (bytes)", (Info.SentBytes / (Info.SentMessages == 0 ? 1 : Info.SentMessages)).ToString());
                    _hubClient.AddOrUpdateTableEntry("Average broker message latency (ms)", Info.AverageMessageLatency.ToString(), true);
                    _hubClient.AddOrUpdateTableEntry("Average broker messages/second sent", messagesPerSecond.ToString(), true);
                    _hubClient.AddOrUpdateTableEntry("Average number of OPC UA monitored items batched in broker message", Info.AverageNotificationsInBrokerMessage.ToString());
                    _hubClient.AddOrUpdateTableEntry("Average number of OPC UA monitored items/second sent", (messagesPerSecond * Info.AverageNotificationsInBrokerMessage).ToString(), true);
                    _hubClient.AddOrUpdateTableEntry("Broker message send failures", Info.FailedMessages.ToString());
                    _hubClient.AddOrUpdateTableEntry("Broker messages too large to send to broker", Info.TooLargeCount.ToString());
                    _hubClient.AddOrUpdateTableEntry("Missed broker message send intervals", Info.MissedSendIntervalCount.ToString());
                    _hubClient.AddOrUpdateTableEntry("Number of OPC UA monitored items encoded", Info.NumberOfEvents.ToString());
                    _hubClient.AddOrUpdateTableEntry("Current working set in MB", (Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)).ToString());
                    _hubClient.AddOrUpdateTableEntry("Broker send interval setting (s)", Settings.Instance.DefaultSendIntervalSeconds.ToString());
                    _hubClient.AddOrUpdateTableEntry("Broker message size setting (bytes)", Settings.Instance.BrokerMessageSize.ToString());

                    chartValues.Add(Info.AverageMessageLatency.ToString());
                    chartValues.Add(messagesPerSecond.ToString());
                    chartValues.Add((messagesPerSecond * Info.AverageNotificationsInBrokerMessage).ToString());
                    _hubClient.AddChartEntry(DateTime.UtcNow.ToString(), chartValues.ToArray());

                    // write to the log at 10x slower than the UI diagnostics
                    if (ticks % 10 == 0)
                    {
                        DiagnosticsSend("ConnectedToBroker", new DataValue(Info.ConnectedToBroker));
                        DiagnosticsSend("NumOpcSessions", new DataValue(Info.NumberOfOpcSessionsConnected));
                        DiagnosticsSend("NumOpcSubscriptions", new DataValue(Info.NumberOfOpcSubscriptionsConnected));
                        DiagnosticsSend("NumOpcMonitoredItems", new DataValue(Info.NumberOfOpcMonitoredItemsMonitored));
                        DiagnosticsSend("QueueCapacity", new DataValue((int)Settings.Instance.InternalQueueCapacity));
                        DiagnosticsSend("QueueCount", new DataValue(Info.MonitoredItemsQueueCount));
                        DiagnosticsSend("EnqueueFailures", new DataValue(Info.EnqueueFailureCount));
                        DiagnosticsSend("SentMessages", new DataValue(Info.SentMessages));
                        DiagnosticsSend("BrokerMessageSize", new DataValue(Info.SentBytes / (Info.SentMessages == 0 ? 1 : Info.SentMessages)));
                        DiagnosticsSend("BrokerMessageLatency", new DataValue(Info.AverageMessageLatency));
                        DiagnosticsSend("BrokerMessagesSecond", new DataValue(messagesPerSecond));
                        DiagnosticsSend("NumOpcMonitoredItemsSecond", new DataValue(messagesPerSecond * Info.AverageNotificationsInBrokerMessage));
                        DiagnosticsSend("BrokerMessageSendFailures", new DataValue(Info.FailedMessages));
                        DiagnosticsSend("CurrentWorkingSetMBs", new DataValue(Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)));
                    }

                    _lastNumMessagesSent = Info.SentMessages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "writing diagnostics output causing error");
                }
            }
        }

        private void DiagnosticsSend(string displayName, DataValue value)
        {
            value.ServerTimestamp = DateTime.UtcNow;

            try
            {
                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = "nsu=http://opcfoundation.org/UA/CloudPublisher/;s=" + displayName,
                    ApplicationUri = "urn:localhost:UACloudPublisher",
                    MessageContext = ServiceMessageContext.GlobalContext,
                    Name = displayName,
                    Value = value
                };

                // enqueue the message
                MessageProcessor.Enqueue(messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Message for diagnostics failed with {ex.Message}'.");
            }
        }
    }
}
