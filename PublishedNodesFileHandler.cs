
namespace Opc.Ua.Cloud.Publisher.Configuration
{
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public class PublishedNodesFileHandler : IPublishedNodesFileHandler
    {
        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;
        private readonly StatusHubClient _hubClient;

        public PublishedNodesFileHandler(
            ILoggerFactory loggerFactory,
            IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("PublishedNodesFileHandler");
            _uaClient = client;
            _hubClient = new StatusHubClient((IHubContext<StatusHub>)Program.AppHost.Services.GetService(typeof(IHubContext<StatusHub>)));
        }

        public void ParseFile(byte[] content)
        {
            List<PublishNodesInterfaceModel> _configurationFileEntries = JsonConvert.DeserializeObject<List<PublishNodesInterfaceModel>>(Encoding.UTF8.GetString(content));

            // process loaded config file entries
            if (_configurationFileEntries != null)
            {
                _logger.LogInformation($"Loaded {_configurationFileEntries.Count} config file entry/entries.");

                // figure out how many nodes there are in total
                // and capture all unique OPC UA server endpoints
                Dictionary<string, PublishNodesInterfaceModel> uniqueEndpoints = new();
                int totalNodeCount = 0;
                foreach (PublishNodesInterfaceModel configFileEntry in _configurationFileEntries)
                {
                    if (configFileEntry.OpcEvents != null) 
                    {
                        totalNodeCount += configFileEntry.OpcEvents.Count;
                    }

                    if (configFileEntry.OpcNodes != null)
                    {
                        totalNodeCount += configFileEntry.OpcNodes.Count;
                    }

                    if (!uniqueEndpoints.ContainsKey(configFileEntry.EndpointUrl))
                    {
                        uniqueEndpoints.Add(configFileEntry.EndpointUrl, configFileEntry);
                        totalNodeCount++;
                    }
                }

                int currentpublishedNodeCount = 0;
                
                if (Settings.Instance.PushCertsBeforePublishing)
                {
                    foreach (PublishNodesInterfaceModel server in uniqueEndpoints.Values)
                    {
                        try
                        {
                            _uaClient.GDSServerPush(server.EndpointUrl, server.UserName, server.Password);
                        }
                        catch (Exception ex)
                        {
                            // skip this server and log an error
                            _logger.LogError("Cannot push new certificates to server " + server.EndpointUrl + "due to " + ex.Message);
                        }
                            
                        currentpublishedNodeCount++;
                        _hubClient.UpdateClientProgressAsync(currentpublishedNodeCount * 100 / totalNodeCount).GetAwaiter().GetResult();
                    }
                }
                else
                {
                    // make sure our progress bar is correct
                    currentpublishedNodeCount += uniqueEndpoints.Count;
                    _hubClient.UpdateClientProgressAsync(currentpublishedNodeCount * 100 / totalNodeCount).GetAwaiter().GetResult();
                }

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
                            NodePublishingModel publishingInfo = new NodePublishingModel()
                            {
                                ExpandedNodeId = ExpandedNodeId.Parse(opcEvent.ExpandedNodeId),
                                EndpointUrl = new Uri(configFileEntry.EndpointUrl).ToString(),
                                OpcAuthenticationMode = configFileEntry.OpcAuthenticationMode,
                                Username = configFileEntry.UserName,
                                Password = configFileEntry.Password
                            };

                            publishingInfo.Filter = new List<FilterModel>();
                            publishingInfo.Filter.AddRange(opcEvent.Filter);

                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();

                            currentpublishedNodeCount++;
                            _hubClient.UpdateClientProgressAsync(currentpublishedNodeCount * 100 / totalNodeCount).GetAwaiter().GetResult();
                        }
                    }

                    // check for variables
                    if (configFileEntry.OpcNodes != null)
                    {
                        foreach (VariableModel opcNode in configFileEntry.OpcNodes)
                        {
                            NodePublishingModel publishingInfo = new NodePublishingModel()
                            {
                                ExpandedNodeId = ExpandedNodeId.Parse(opcNode.Id),
                                EndpointUrl = new Uri(configFileEntry.EndpointUrl).ToString(),
                                OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                HeartbeatInterval = opcNode.HeartbeatInterval,
                                SkipFirst = opcNode.SkipFirst,
                                OpcAuthenticationMode = configFileEntry.OpcAuthenticationMode,
                                Username = configFileEntry.UserName,
                                Password = configFileEntry.Password
                            };

                            _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();

                            currentpublishedNodeCount++;
                            _hubClient.UpdateClientProgressAsync(currentpublishedNodeCount * 100 / totalNodeCount).GetAwaiter().GetResult();
                        }
                    }
                }
            }
        }
    }
}
