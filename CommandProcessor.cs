
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class CommandProcessor : ICommandProcessor
    {
        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;

        public CommandProcessor(ILoggerFactory loggerFactory, IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("CommandProcessor");
            _uaClient = client;
        }

        public async Task<byte[]> PublishNodes(string payload)
        {
            UserAuthModeEnum desiredAuthenticationMode = UserAuthModeEnum.Anonymous;
            List<string> statusResponse = new List<string>();

            PublishNodesInterfaceModel publishNodesMethodData = JsonConvert.DeserializeObject<PublishNodesInterfaceModel>(payload);

            if (publishNodesMethodData.OpcAuthenticationMode == UserAuthModeEnum.UsernamePassword)
            {
                if (string.IsNullOrWhiteSpace(publishNodesMethodData.UserName) && string.IsNullOrWhiteSpace(publishNodesMethodData.Password))
                {
                    throw new ArgumentException($"If {nameof(publishNodesMethodData.OpcAuthenticationMode)} is set to '{UserAuthModeEnum.UsernamePassword}', you have to specify username and password.");
                }
            }

            // check for events
            if (publishNodesMethodData.OpcEvents != null)
            {
                foreach (EventModel opcEvent in publishNodesMethodData.OpcEvents)
                {
                    NodePublishingModel node = new NodePublishingModel()
                    {
                        ExpandedNodeId = ExpandedNodeId.Parse(opcEvent.ExpandedNodeId),
                        EndpointUrl = new Uri(publishNodesMethodData.EndpointUrl).ToString(),
                        Username = publishNodesMethodData.UserName,
                        Password = publishNodesMethodData.Password,
                        OpcAuthenticationMode = desiredAuthenticationMode
                    };

                    node.Filter = new List<FilterModel>();
                    node.Filter.AddRange(opcEvent.Filter);

                    await _uaClient.PublishNodeAsync(node).ConfigureAwait(false);

                    string statusMessage = $"Event {node.ExpandedNodeId} on endpoint {node.EndpointUrl} published successfully.";
                    statusResponse.Add(statusMessage);
                    _logger.LogInformation(statusMessage);
                }
            }

            // check for variables
            if (publishNodesMethodData.OpcNodes != null)
            {
                foreach (VariableModel nodeOnEndpoint in publishNodesMethodData.OpcNodes)
                {
                    NodePublishingModel node = new NodePublishingModel
                    {
                        ExpandedNodeId = ExpandedNodeId.Parse(nodeOnEndpoint.Id),
                        EndpointUrl = new Uri(publishNodesMethodData.EndpointUrl).ToString(),
                        SkipFirst = nodeOnEndpoint.SkipFirst,
                        HeartbeatInterval = nodeOnEndpoint.HeartbeatInterval,
                        OpcPublishingInterval = nodeOnEndpoint.OpcPublishingInterval,
                        OpcSamplingInterval = nodeOnEndpoint.OpcSamplingInterval,
                        Username = publishNodesMethodData.UserName,
                        Password = publishNodesMethodData.Password,
                        OpcAuthenticationMode = desiredAuthenticationMode
                    };

                    await _uaClient.PublishNodeAsync(node).ConfigureAwait(false);

                    string statusMessage = $"Node {node.ExpandedNodeId} on endpoint {node.EndpointUrl} published successfully.";
                    statusResponse.Add(statusMessage);
                    _logger.LogInformation(statusMessage);
                }
            }

            return BuildResponseAndCropStatus(statusResponse);
        }

        public byte[] UnpublishNodes(string payload)
        {
            List<string> statusResponse = new List<string>();

            UnpublishNodesInterfaceModel unpublishNodesMethodData = JsonConvert.DeserializeObject<UnpublishNodesInterfaceModel>(payload);

            // check for events
            if (unpublishNodesMethodData.OpcEvents != null)
            {
                foreach (EventModel opcEvent in unpublishNodesMethodData.OpcEvents)
                {
                    NodePublishingModel node = new NodePublishingModel()
                    {
                        ExpandedNodeId = ExpandedNodeId.Parse(opcEvent.ExpandedNodeId),
                        EndpointUrl = new Uri(unpublishNodesMethodData.EndpointUrl).ToString()
                    };

                    _uaClient.UnpublishNode(node);

                    string statusMessage = $"Event {node.ExpandedNodeId} on endpoint {node.EndpointUrl} unpublished successfully.";
                    statusResponse.Add(statusMessage);
                    _logger.LogInformation(statusMessage);
                }
            }

            // check for variables
            if (unpublishNodesMethodData.OpcNodes != null)
            {
                foreach (VariableModel nodeOnEndpoint in unpublishNodesMethodData.OpcNodes)
                {
                    NodePublishingModel node = new NodePublishingModel
                    {
                        ExpandedNodeId = ExpandedNodeId.Parse(nodeOnEndpoint.Id),
                        EndpointUrl = new Uri(unpublishNodesMethodData.EndpointUrl).ToString()
                    };

                    _uaClient.UnpublishNode(node);

                    string statusMessage = $"Node {node.ExpandedNodeId} on endpoint {node.EndpointUrl} unpublished successfully.";
                    statusResponse.Add(statusMessage);
                    _logger.LogInformation(statusMessage);
                }
            }

            return BuildResponseAndCropStatus(statusResponse);
        }

        public byte[] UnpublishAllNodes()
        {
            _uaClient.UnpublishAllNodes();

            return BuildResponseAndTruncateResult("All nodes unpublished successfully.");
        }

        public byte[] GetPublishedNodes()
        {
            return BuildResponseAndTruncateResult(_uaClient.GetPublishedNodes());
        }

        public byte[] GetInfo()
        {
            return BuildResponseAndTruncateResult(Diagnostics.Singleton.Info);
        }

        private byte[] BuildResponseAndCropStatus(List<string> statusResponse)
        {
            byte[] result;
            int maxIndex = statusResponse.Count();
            while (true)
            {
                result = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(statusResponse.GetRange(0, maxIndex)));
                if (result.Length > Settings.MaxResponsePayloadLength)
                {
                    maxIndex /= 2;
                    continue;
                }
                else
                {
                    break;
                }
            }
            if (maxIndex != statusResponse.Count())
            {
                statusResponse.RemoveRange(maxIndex, statusResponse.Count() - maxIndex);
                statusResponse.Add("Results have been cropped due to message size limitations!");
            }

            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(statusResponse.GetRange(0, maxIndex)));
        }

        private byte[] BuildResponseAndTruncateResult(object result)
        {
            byte[] response = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));

            if (response.Length > Settings.MaxResponsePayloadLength)
            {
                _logger.LogError("Results have been cropped due to message size limitations!");
                Array.Resize(ref response, response.Length > Settings.MaxResponsePayloadLength ? Settings.MaxResponsePayloadLength : response.Length);
            }

            return response;
        }

    }
}
