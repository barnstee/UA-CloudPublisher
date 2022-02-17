
namespace UA.MQTT.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.IO;
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
                List<ConfigurationFileEntryLegacyModel> _configurationFileEntries = null;
                try
                {
                    string json = Encoding.UTF8.GetString(content);
                    _configurationFileEntries = JsonConvert.DeserializeObject<List<ConfigurationFileEntryLegacyModel>>(json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Loading of the node configuration file failed with {ex.Message}.");
                }

                // process loaded config file entries
                if (_configurationFileEntries != null)
                {
                    _logger.LogInformation($"Loaded {_configurationFileEntries.Count} config file entry/entries.");
                    foreach (ConfigurationFileEntryLegacyModel publisherConfigFileEntryLegacy in _configurationFileEntries)
                    {
                        // decrypt username and password, if required
                        NetworkCredential decryptedCreds = null;
                        if (publisherConfigFileEntryLegacy.EncryptedAuthCredential != null)
                        {
                            decryptedCreds = publisherConfigFileEntryLegacy.EncryptedAuthCredential.Decrypt(cert);
                        }

                        if (publisherConfigFileEntryLegacy.NodeId == null)
                        {
                            // new node configuration syntax.
                            //checked for nodes or events
                            if (publisherConfigFileEntryLegacy.OpcEvents != null)
                            {
                                // process event configuration
                                foreach (OpcEventOnEndpointModel opcEvent in publisherConfigFileEntryLegacy.OpcEvents)
                                {
                                    ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcEvent.Id);
                                    EventPublishingModel publishingInfo = new EventPublishingModel()
                                    {
                                        ExpandedNodeId = expandedNodeId,
                                        EndpointUrl = publisherConfigFileEntryLegacy.EndpointUrl.OriginalString,
                                        UseSecurity = publisherConfigFileEntryLegacy.UseSecurity,
                                        DisplayName = opcEvent.DisplayName
                                    };
                                    publishingInfo.SelectClauses.AddRange(opcEvent.SelectClauses);
                                    publishingInfo.WhereClauses.AddRange(opcEvent.WhereClauses);
                                    _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                                }
                            }
                            else
                            {
                                foreach (OpcNodeOnEndpointModel opcNode in publisherConfigFileEntryLegacy.OpcNodes)
                                {
                                    if (opcNode.ExpandedNodeId != null)
                                    {
                                        ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.ExpandedNodeId);
                                        EventPublishingModel publishingInfo = new EventPublishingModel()
                                        {
                                            ExpandedNodeId = expandedNodeId,
                                            EndpointUrl = publisherConfigFileEntryLegacy.EndpointUrl.OriginalString,
                                            UseSecurity = publisherConfigFileEntryLegacy.UseSecurity,
                                            OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                            OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                            DisplayName = opcNode.DisplayName,
                                            HeartbeatInterval = opcNode.HeartbeatInterval,
                                            SkipFirst = opcNode.SkipFirst,
                                            OpcAuthenticationMode = publisherConfigFileEntryLegacy.OpcAuthenticationMode,
                                            AuthCredential = decryptedCreds
                                        };
                                        _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                                    }
                                    else
                                    {
                                        // check Id string to check which format we have
                                        if (opcNode.Id.StartsWith("nsu=", StringComparison.InvariantCulture))
                                        {
                                            // ExpandedNodeId format
                                            ExpandedNodeId expandedNodeId = ExpandedNodeId.Parse(opcNode.Id);
                                            EventPublishingModel publishingInfo = new EventPublishingModel()
                                            {
                                                ExpandedNodeId = expandedNodeId,
                                                EndpointUrl = publisherConfigFileEntryLegacy.EndpointUrl.OriginalString,
                                                UseSecurity = publisherConfigFileEntryLegacy.UseSecurity,
                                                OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                                OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                                DisplayName = opcNode.DisplayName,
                                                HeartbeatInterval = opcNode.HeartbeatInterval,
                                                SkipFirst = opcNode.SkipFirst,
                                                OpcAuthenticationMode = publisherConfigFileEntryLegacy.OpcAuthenticationMode,
                                                AuthCredential = decryptedCreds
                                            };
                                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                                        }
                                        else
                                        {
                                            // NodeId format
                                            NodeId nodeId = NodeId.Parse(opcNode.Id);
                                            EventPublishingModel publishingInfo = new EventPublishingModel
                                            {
                                                ExpandedNodeId = nodeId,
                                                EndpointUrl = publisherConfigFileEntryLegacy.EndpointUrl.OriginalString,
                                                UseSecurity = publisherConfigFileEntryLegacy.UseSecurity,
                                                OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                                OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                                DisplayName = opcNode.DisplayName,
                                                HeartbeatInterval = opcNode.HeartbeatInterval,
                                                SkipFirst = opcNode.SkipFirst,
                                                OpcAuthenticationMode = publisherConfigFileEntryLegacy.OpcAuthenticationMode,
                                                AuthCredential = decryptedCreds
                                            };
                                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // NodeId (ns=) format node configuration syntax using default sampling and publishing interval.
                            EventPublishingModel publishingInfo = new EventPublishingModel()
                            {
                                ExpandedNodeId = publisherConfigFileEntryLegacy.NodeId,
                                EndpointUrl = publisherConfigFileEntryLegacy.EndpointUrl.OriginalString,
                                UseSecurity = publisherConfigFileEntryLegacy.UseSecurity,
                                OpcAuthenticationMode = publisherConfigFileEntryLegacy.OpcAuthenticationMode,
                                AuthCredential = decryptedCreds
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
