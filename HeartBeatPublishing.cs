
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Threading;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class HeartBeatPublishing: IDisposable
    {
        /// <summary>
        /// Retrieves the session containing the heartbeat
        /// </summary>
        public Session HeartBeatSession { get; }

        /// <summary>
        /// Retrieves the node Id for the heartbeat
        /// </summary>
        public NodeId HeartBeatNodeId { get; }

        /// <summary>
        /// Retrieves the heartbeat interval
        /// </summary>
        public uint HeartBeatInterval { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// TODO: Possibly reimplement this class using OPC UA key frames instead
        public HeartBeatPublishing(
            uint heartbeatInterval,
            Session session,
            NodeId nodeId,
            ILoggerFactory loggerFactory,
            ISettingsConfiguration settingsConfiguration
        )
        {
            _logger = loggerFactory.CreateLogger("HeartBeatPublishing");
            _settingsConfiguration = settingsConfiguration;

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

        /// <summary>
        /// Stop the heartbeat
        /// </summary>
        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Timer callback for heartbeat telemetry send.
        /// </summary>
        private void HeartbeatSend(object state)
        {
            try
            {
                MessageDataModel messageData = new MessageDataModel();
                messageData.EndpointUrl = HeartBeatSession.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                messageData.ApplicationUri = HeartBeatSession.Endpoint.Server.ApplicationUri + _settingsConfiguration.PublisherSite;
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
                MessageProcessingEngine.Enqueue(messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Message for heartbeat failed with {ex.Message}'.");
            }
        }

        private readonly ILogger _logger;
        private readonly Timer _timer;
        private readonly ISettingsConfiguration _settingsConfiguration;
        private string _displayName;
    }
}
