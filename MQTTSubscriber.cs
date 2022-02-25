
namespace UA.MQTT.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using MQTTnet;
    using MQTTnet.Adapter;
    using MQTTnet.Client;
    using MQTTnet.Client.Connecting;
    using MQTTnet.Client.Options;
    using MQTTnet.Client.Subscribing;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;
    using DiagnosticInfo = Models.DiagnosticInfo;

    public class MQTTSubscriber : IMQTTSubscriber
    {
        private IMqttClient _client = null;

        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;

        public MQTTSubscriber(ILoggerFactory loggerFactory, IUAClient client)
        {
            _logger = loggerFactory.CreateLogger("MQTTSubscriber");
            _uaClient = client;
        }

        public void Connect()
        {
            try
            {
                // create MQTT password
                string password = Settings.Singleton.MQTTPassword;
                if (Settings.Singleton.CreateMQTTSASToken)
                {
                    // create SAS token as password
                    TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                    int week = 60 * 60 * 24 * 7;
                    string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                    string stringToSign = HttpUtility.UrlEncode(Settings.Singleton.MQTTBrokerName + "/devices/" + Settings.Singleton.MQTTClientName) + "\n" + expiry;
                    HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                    string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(Settings.Singleton.MQTTBrokerName + "/devices/" + Settings.Singleton.MQTTClientName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
                }

                // create MQTT client
                _client = new MqttFactory().CreateMqttClient();
                _client.UseApplicationMessageReceivedHandler(msg => HandleMessageAsync(msg));
                var clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(opt => opt.NoDelay = true)
                    .WithClientId(Settings.Singleton.MQTTClientName)
                    .WithTcpServer(Settings.Singleton.MQTTBrokerName, 8883)
                    .WithTls(new MqttClientOptionsBuilderTlsParameters { UseTls = true })
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                    .WithCommunicationTimeout(TimeSpan.FromSeconds(10))
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                    .WithCleanSession(true) // clear existing subscriptions 
                    .WithCredentials(Settings.Singleton.MQTTUsername, password);

                // setup disconnection handling
                _client.UseDisconnectedHandler(disconnectArgs =>
                {
                    _logger.LogWarning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    // wait a second, then simply reconnect again
                    Task.Delay(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                    Connect();
                });

                try
                {
                    var connectResult = _client.ConnectAsync(clientOptions.Build(), CancellationToken.None).GetAwaiter().GetResult();
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        var status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    var subscribeResult = _client.SubscribeAsync(
                        new MqttTopicFilter
                        {
                            Topic = Settings.Singleton.MQTTCommandTopic,
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                        }).GetAwaiter().GetResult();

                    // make sure subscriptions were successful
                    if (subscribeResult.Items.Count != 1 || subscribeResult.Items[0].ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                    {
                        throw new ApplicationException("Failed to subscribe");
                    }
                }
                catch (MqttConnectingFailedException ex)
                {
                    _logger.LogCritical($"Failed to connect with reason {ex.ResultCode} and message: {ex.Message}");
                    if (ex.Result?.UserProperties != null)
                    {
                        foreach (var prop in ex.Result.UserProperties)
                        {
                            _logger.LogCritical($"{prop.Name}: {prop.Value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed to connect to MQTT broker: " + ex.Message);
            }
        }

        public void Publish(byte[] payload)
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic(Settings.Singleton.MQTTMessageTopic)
                .WithPayload(payload)
                .Build();

            _client.PublishAsync(message).GetAwaiter().GetResult();
        }

        private MqttApplicationMessage BuildResponse(string status, string id, byte[] payload)
        {
            string responseTopic = Settings.Singleton.MQTTResponseTopic;

            return new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic($"{responseTopic}/{status}/{id}")
                .WithPayload(payload)
                .Build();
        }

        // parses status from packet properties
        private int? GetStatus(List<MqttUserProperty> properties)
        {
            var status = properties.FirstOrDefault(up => up.Name == "status");
            if (status == null)
            {
                return null;
            }

            return int.Parse(status.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // handles all incoming messages
        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            _logger.LogInformation($"Received method call with topic: {args.ApplicationMessage.Topic} and payload: {args.ApplicationMessage.ConvertPayloadToString()}");

            string requestTopic = Settings.Singleton.MQTTCommandTopic;
            string requestID = args.ApplicationMessage.Topic.Substring(args.ApplicationMessage.Topic.IndexOf("?"));

            try
            {
                string requestPayload = args.ApplicationMessage.ConvertPayloadToString();
                byte[] responsePayload = null;

                // route this to the right handler
                if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "PublishNodes"))
                {
                    responsePayload = PublishNodes(requestPayload);
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishNodes"))
                {
                    responsePayload = UnpublishNodes(requestPayload);
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishAllNodes"))
                {
                    responsePayload = UnpublishAllNodes();
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetInfo"))
                {
                    responsePayload = GetInfo();
                }
                else
                {
                    _logger.LogError("Unknown command received: " + args.ApplicationMessage.Topic);
                }

                // send reponse to MQTT broker
                await _client.PublishAsync(BuildResponse("200", requestID, responsePayload)).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleMessageAsync");

                // send error to MQTT broker
                await _client.PublishAsync(BuildResponse("500", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)))).ConfigureAwait(false);
            }
        }

        public byte[] PublishNodes(string payload)
        {
            OpcSessionUserAuthenticationMode desiredAuthenticationMode = OpcSessionUserAuthenticationMode.Anonymous;
            List<string> statusResponse = new List<string>();

            PublishNodesMethodRequestModel publishNodesMethodData = JsonConvert.DeserializeObject<PublishNodesMethodRequestModel>(payload);

            if (publishNodesMethodData.OpcAuthenticationMode == OpcSessionUserAuthenticationMode.UsernamePassword)
            {
                if (string.IsNullOrWhiteSpace(publishNodesMethodData.UserName) && string.IsNullOrWhiteSpace(publishNodesMethodData.Password))
                {
                    throw new ArgumentException($"If {nameof(publishNodesMethodData.OpcAuthenticationMode)} is set to '{OpcSessionUserAuthenticationMode.UsernamePassword}', you have to specify '{nameof(publishNodesMethodData.UserName)}' and/or '{nameof(publishNodesMethodData.Password)}'.");
                }

                desiredAuthenticationMode = OpcSessionUserAuthenticationMode.UsernamePassword;
            }

            foreach (OpcNodeOnEndpointModel nodeOnEndpoint in publishNodesMethodData.OpcNodes)
            {
                NodePublishingModel node = new NodePublishingModel {
                    ExpandedNodeId = nodeOnEndpoint.ExpandedNodeId,
                    EndpointUrl = new Uri(publishNodesMethodData.EndpointUrl).ToString(),
                    SkipFirst = nodeOnEndpoint.SkipFirst,
                    DisplayName = nodeOnEndpoint.DisplayName,
                    HeartbeatInterval = nodeOnEndpoint.HeartbeatInterval,
                    OpcPublishingInterval = nodeOnEndpoint.OpcPublishingInterval,
                    OpcSamplingInterval = nodeOnEndpoint.OpcSamplingInterval,
                    AuthCredential = null,
                    OpcAuthenticationMode = desiredAuthenticationMode
                };

                if (desiredAuthenticationMode == OpcSessionUserAuthenticationMode.UsernamePassword)
                {
                    node.AuthCredential = new NetworkCredential(publishNodesMethodData.UserName, publishNodesMethodData.Password);
                }

                _uaClient.PublishNodeAsync(node).GetAwaiter().GetResult();

                string statusMessage = $"Node {node.ExpandedNodeId} on endpoint {node.EndpointUrl} published successfully.";
                statusResponse.Add(statusMessage);
                _logger.LogInformation(statusMessage);
            }

            return BuildResponseAndCropStatus(statusResponse);
        }

        public byte[] UnpublishNodes(string payload)
        {
            List<string> statusResponse = new List<string>();

            UnpublishNodesMethodRequestModel unpublishNodesMethodData = JsonConvert.DeserializeObject<UnpublishNodesMethodRequestModel>(payload);

            foreach (OpcNodeOnEndpointModel nodeOnEndpoint in unpublishNodesMethodData.OpcNodes)
            {
                NodePublishingModel node = new NodePublishingModel {
                    ExpandedNodeId = nodeOnEndpoint.ExpandedNodeId,
                    EndpointUrl = new Uri(unpublishNodesMethodData.EndpointUrl).ToString()
                };

                _uaClient.UnpublishNode(node);

                string statusMessage = $"Node {node.ExpandedNodeId} on endpoint {node.EndpointUrl} unpublished successfully.";
                statusResponse.Add(statusMessage);
                _logger.LogInformation(statusMessage);
            }

            return BuildResponseAndCropStatus(statusResponse);
        }

        public byte[] UnpublishAllNodes()
        {
            _uaClient.UnpublishAllNodes();
        
            return BuildResponseAndTruncateResult("All nodes unpublished successfully.");
        }

        public byte[] GetInfo()
        {
            DiagnosticInfoMethodResponseModel diagnosticInfoResponse = new DiagnosticInfoMethodResponseModel();
            List<DiagnosticInfo> diagnosticInfos = new List<DiagnosticInfo>();

            diagnosticInfos.Add(Diagnostics.Singleton.Info);
            diagnosticInfoResponse.DiagnosticInfos = diagnosticInfos;

            return BuildResponseAndTruncateResult(diagnosticInfoResponse);
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
                statusResponse.Add("Results have been cropped due to package size limitations.");
            }

            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(statusResponse.GetRange(0, maxIndex)));
        }

        private byte[] BuildResponseAndTruncateResult(object result)
        {
            byte[] response = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));

            if (response.Length > Settings.MaxResponsePayloadLength)
            {
                _logger.LogError("Response size is too long");
                Array.Resize(ref response, response.Length > Settings.MaxResponsePayloadLength ? Settings.MaxResponsePayloadLength : response.Length);
            }

            return response;
        }
    }
}