
namespace UA.MQTT.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class PublishedNodesFileHandler : IPublishedNodesFileHandler
    {
        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;

        public PublishedNodesFileHandler(
            ILoggerFactory loggerFactory,
            IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("PublishedNodesFileHandler");
            _uaClient = client;
        }

        public void ParseFile(byte[] content)
        {
            List<PublishNodesInterfaceModel> _configurationFileEntries = JsonConvert.DeserializeObject<List<PublishNodesInterfaceModel>>(Encoding.UTF8.GetString(content));

            // process loaded config file entries
            if (_configurationFileEntries != null)
            {
                _logger.LogInformation($"Loaded {_configurationFileEntries.Count} config file entry/entries.");
                foreach (PublishNodesInterfaceModel configFileEntry in _configurationFileEntries)
                {
                    if (configFileEntry.OpcAuthenticationMode == UserAuthModeEnum.UsernamePassword)
                    {
                        if (string.IsNullOrWhiteSpace(configFileEntry.UserName) && string.IsNullOrWhiteSpace(configFileEntry.Password))
                        {
                            throw new ArgumentException($"If {nameof(configFileEntry.OpcAuthenticationMode)} is set to '{UserAuthModeEnum.UsernamePassword}', you have to specify username and password.");
                        }
                    }

                    // check for events
                    if (configFileEntry.OpcEvents != null)
                    {
                        foreach (EventModel opcEvent in configFileEntry.OpcEvents)
                        {
                            ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcEvent.ExpandedNodeId);
                            NodePublishingModel publishingInfo = new NodePublishingModel()
                            {
                                ExpandedNodeId = expandedNodeId,
                                EndpointUrl = configFileEntry.EndpointUrl,
                            };

                            publishingInfo.Filter = new List<FilterModel>();
                            publishingInfo.Filter.AddRange(opcEvent.Filter);

                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                        }
                    }

                    // check for variables
                    if (configFileEntry.OpcNodes != null)
                    {
                        foreach (VariableModel opcNode in configFileEntry.OpcNodes)
                        {
                            ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.Id);
                            NodePublishingModel publishingInfo = new NodePublishingModel()
                            {
                                ExpandedNodeId = expandedNodeId,
                                EndpointUrl = configFileEntry.EndpointUrl,
                                OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                HeartbeatInterval = opcNode.HeartbeatInterval,
                                SkipFirst = opcNode.SkipFirst,
                                OpcAuthenticationMode = configFileEntry.OpcAuthenticationMode,
                                Username = configFileEntry.UserName,
                                Password = configFileEntry.Password
                            };

                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                        }
                    }
                }
            }
        }
    }
}
