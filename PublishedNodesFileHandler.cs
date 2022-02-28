
namespace UA.MQTT.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
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

        public bool ParseFile(byte[] content, X509Certificate2 cert)
        {
            try
            {
                List<PublishNodesInterfaceModel> _configurationFileEntries = null;
                try
                {
                    string json = Encoding.UTF8.GetString(content);
                    _configurationFileEntries = JsonConvert.DeserializeObject<List<PublishNodesInterfaceModel>>(json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Loading of the node configuration file failed with {ex.Message}.");
                }

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
                                    DisplayName = opcEvent.DisplayName
                                };

                                publishingInfo.SelectClauses = new List<SelectClauseModel>();
                                publishingInfo.SelectClauses.AddRange(opcEvent.SelectClauses);

                                publishingInfo.WhereClauses = new List<WhereClauseModel>();
                                publishingInfo.WhereClauses.AddRange(opcEvent.WhereClauses);

                                _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                            }
                        }
                            
                        foreach (VariableModel opcNode in configFileEntry.OpcNodes)
                        {
                            ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.Id);
                            NodePublishingModel publishingInfo = new NodePublishingModel()
                            {
                                ExpandedNodeId = expandedNodeId,
                                EndpointUrl = configFileEntry.EndpointUrl,
                                OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                DisplayName = opcNode.DisplayName,
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

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Could not parse published nodes file and publish all nodes: " + ex.Message);
                return false;
            }
        }
    }
}
