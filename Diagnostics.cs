
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    public class Diagnostics
    {
        private readonly ILogger _logger;

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
            Clear();

            if (Settings.Instance.DiagnosticsLoggingInterval == 0)
            {
                // diagnostics are disabled
                return;
            }

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
                    await Task.Delay((int)Settings.Instance.DiagnosticsLoggingInterval * 1000, cancellationToken).ConfigureAwait(false);

                    float messagesPerSecond = ((float)(Info.SentMessages - _lastNumMessagesSent)) / Settings.Instance.DiagnosticsLoggingInterval;

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

                    _lastNumMessagesSent = Info.SentMessages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "writing diagnostics output error");
                }
            }
        }

        private void DiagnosticsSend(string displayName, DataValue value)
        {
            value.ServerTimestamp = DateTime.UtcNow;

            try
            {
                MessageProcessorModel messageData = new()
                {
                    ExpandedNodeId = "nsu=http://opcfoundation.org/UA/CloudPublisher/;s=" + displayName,
                    ApplicationUri = "urn:" + Settings.Instance.PublisherName,
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
