
namespace Opc.Ua.Cloud.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;

    public class PublishedNodesFileHandler : IPublishedNodesFileHandler
    {
        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;
        private readonly IUAApplication _uaApplication;

        public int Progress { get; set; } = 0;

        public PublishedNodesFileHandler(
            ILoggerFactory loggerFactory,
            IUAClient client,
            IUAApplication uaApplication)
        {
            _logger = loggerFactory.CreateLogger("PublishedNodesFileHandler");
            _uaClient = client;
            _uaApplication = uaApplication;
        }

        private string DecryptString(string encryptedString)
        {
            if (!string.IsNullOrEmpty(encryptedString))
            {
                X509Certificate2 cert = _uaApplication.IssuerCert;
                using RSA rsa = cert.GetRSAPrivateKey();
                bool isBase64String = Convert.TryFromBase64String(encryptedString, new Span<byte>(new byte[encryptedString.Length]), out int bytesParsed);
                if (isBase64String && (rsa != null))
                {
                    return Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(encryptedString), RSAEncryptionPadding.Pkcs1));
                }
                else
                {
                    return encryptedString;
                }
            }
            else
            {
                return string.Empty;
            }
        }
        public void ParseFile(byte[] content)
        {
            _logger.LogInformation($"Processing persistency file...");
            List<PublishNodesInterfaceModel> _configurationFileEntries = JsonConvert.DeserializeObject<List<PublishNodesInterfaceModel>>(Encoding.UTF8.GetString(content));

            // process loaded config file entries
            if (_configurationFileEntries != null)
            {
                _logger.LogInformation($"Loaded {_configurationFileEntries.Count} config file entry/entries.");

                // figure out how many nodes there are in total and capture all unique OPC UA server endpoints
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
                            _uaClient.GDSServerPush(server.EndpointUrl, server.UserName, DecryptString(server.Password)).GetAwaiter().GetResult();

                            // after the cert push, give the server 5s time to become available again before trying to publish from it
                            Thread.Sleep(5000);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Cannot push new certificates to server " + server.EndpointUrl + "due to " + ex.Message);
                        }

                        currentpublishedNodeCount++;
                        Progress = currentpublishedNodeCount * 100 / totalNodeCount;
                    }
                }
                else
                {
                    // make sure our progress bar is correct
                    currentpublishedNodeCount += uniqueEndpoints.Count;
                    Progress = currentpublishedNodeCount * 100 / totalNodeCount;
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
                                Password = DecryptString(configFileEntry.Password)
                            };

                            publishingInfo.Filter = new List<FilterModel>();
                            publishingInfo.Filter.AddRange(opcEvent.Filter);

                            try
                            {
                                _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                            }
                            catch (Exception ex)
                            {
                                // skip this event and log an error
                                _logger.LogError("Cannot publish event " + publishingInfo.ExpandedNodeId + " on server " + publishingInfo.EndpointUrl + "due to " + ex.Message);
                            }

                            currentpublishedNodeCount++;
                            Progress = currentpublishedNodeCount * 100 / totalNodeCount;
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
                                Password = DecryptString(configFileEntry.Password)
                            };

                            try
                            {
                                _uaClient.PublishNodeAsync(publishingInfo).GetAwaiter().GetResult();
                            }
                            catch (Exception ex)
                            {
                                // skip this variable and log an error
                                _logger.LogError("Cannot publish variable " + publishingInfo.ExpandedNodeId + " on server " + publishingInfo.EndpointUrl + "due to " + ex.Message);
                            }

                            currentpublishedNodeCount++;
                            Progress = currentpublishedNodeCount * 100 / totalNodeCount;
                        }
                    }
                }

                _logger.LogInformation("Publishednodes.json/persistency file processed successfully.");
            }
        }
    }
}
