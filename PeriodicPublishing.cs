
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Threading;

    public class PeriodicPublishing : IDisposable
    {

        private readonly ILogger _logger;
        private readonly Timer _timer;

        public ISession HeartBeatSession { get; }

        public NodeId HeartBeatNodeId { get; }

        public uint HeartBeatInterval { get; }

        public string DisplayName { get; }

        public PeriodicPublishing(
            uint heartbeatInterval,
            ISession session,
            NodeId nodeId,
            string name,
            ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("PeriodicPublishing");

            HeartBeatSession = session;
            HeartBeatNodeId = nodeId;
            HeartBeatInterval = heartbeatInterval;
            DisplayName = name;

            if (heartbeatInterval > 0)
            {
                _timer = new Timer(HeartbeatSend, null, heartbeatInterval, heartbeatInterval);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async void HeartbeatSend(object state)
        {
            try
            {
                MessageProcessorModel messageData = new MessageProcessorModel
                {
                    ExpandedNodeId = NodeId.ToExpandedNodeId(HeartBeatNodeId, HeartBeatSession.NamespaceUris).ToString(),
                    ApplicationUri = HeartBeatSession.Endpoint.Server.ApplicationUri,
                    MessageContext = HeartBeatSession.MessageContext,
                    Name = DisplayName
                };

                DataValue value = await HeartBeatSession.ReadValueAsync(HeartBeatNodeId).ConfigureAwait(false);
                if (value != null)
                {
                    messageData.Value = value;
                }

                // enqueue the message
                MessageProcessor.Enqueue(messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Message for heartbeat failed with {ex.Message}'.");
            }
        }
    }
}
