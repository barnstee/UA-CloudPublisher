
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Threading;
    using UA.MQTT.Publisher.Models;

    public class PeriodicPublishing: IDisposable
    {

        private readonly ILogger _logger;
        private readonly Timer _timer;
        private string _displayName;

        public Session HeartBeatSession { get; }

        public NodeId HeartBeatNodeId { get; }

        public uint HeartBeatInterval { get; }

        public PeriodicPublishing(
            uint heartbeatInterval,
            Session session,
            NodeId nodeId,
            ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("PeriodicPublishing");

            HeartBeatSession = session;
            HeartBeatNodeId = nodeId;
            HeartBeatInterval = heartbeatInterval;

            // setup heartbeat processing
            if (heartbeatInterval > 0)
            {
                // setup the heartbeat timer
                _timer = new Timer(HeartbeatSend, null, heartbeatInterval * 1000, heartbeatInterval * 1000);
                _logger.LogDebug($"Setting up {heartbeatInterval} sec heartbeat for node '{nodeId}'.");
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

        private void HeartbeatSend(object state)
        {
            try
            {
                MessageProcessorModel messageData = new MessageProcessorModel();
                messageData.EndpointUrl = HeartBeatSession.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                messageData.ApplicationUri = HeartBeatSession.Endpoint.Server.ApplicationUri;
                messageData.ExpandedNodeId = NodeId.ToExpandedNodeId(HeartBeatNodeId, HeartBeatSession.NamespaceUris).ToString();
                messageData.DataSetWriterId = messageData.ApplicationUri + ":" + (HeartBeatInterval * 1000).ToString();
                messageData.MessageContext = HeartBeatSession.MessageContext;

                DataValue value = HeartBeatSession.ReadValue(HeartBeatNodeId);
                if (value != null)
                {
                    messageData.Value = value;
                }

                // read display name and cache it
                if (_displayName == null)
                {
                    VariableNode node = (VariableNode)HeartBeatSession.ReadNode(HeartBeatNodeId);
                    if (node != null)
                    {
                        _displayName = node.DisplayName.Text;
                    }
                }

                messageData.DisplayName = _displayName;

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
