
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
        private string _clientName = Environment.GetEnvironmentVariable("MQTT_CLIENTNAME");

        private readonly ILogger _logger;
        private readonly IUAClient _uaClient;
        private readonly IPeriodicDiagnosticsInfo _diag;

        public MQTTSubscriber(
            ILoggerFactory loggerFactory,
            IUAClient client,
            IPeriodicDiagnosticsInfo diag)
        {
            _logger = loggerFactory.CreateLogger("MQTTSubscriber");
            _uaClient = client;
            _diag = diag;
        }

        public void Connect()
        {
            // create MQTT client
            string brokerName = Environment.GetEnvironmentVariable("MQTT_BROKERNAME");
            string userName = Environment.GetEnvironmentVariable("MQTT_USERNAME");
            string password = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
            string topic = Environment.GetEnvironmentVariable("MQTT_TOPIC");

            if (Environment.GetEnvironmentVariable("CREATE_SAS_PASSWORD") != null)
            {
                // create SAS token as password
                TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                int week = 60 * 60 * 24 * 7;
                string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                string stringToSign = HttpUtility.UrlEncode(brokerName + "/devices/" + _clientName) + "\n" + expiry;
                HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(brokerName + "/devices/" + _clientName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
            }

            // create MQTT client
            _client = new MqttFactory().CreateMqttClient();
            _client.UseApplicationMessageReceivedHandler(msg => HandleMessageAsync(msg));
            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(opt => opt.NoDelay = true)
                .WithClientId(_clientName)
                .WithTcpServer(brokerName, 8883)
                .WithTls(new MqttClientOptionsBuilderTlsParameters { UseTls = true })
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                .WithCommunicationTimeout(TimeSpan.FromSeconds(30))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(300))
                .WithCleanSession(false) // keep existing subscriptions 
                .WithCredentials(userName, password);

            // setup disconnection handling
            _client.UseDisconnectedHandler(disconnectArgs =>
            {
                _logger.LogWarning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                // simply reconnect again
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
                        Topic = topic,
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
                _logger.LogCritical($"Failed to connect, reason code: {ex.ResultCode}");
                if (ex.Result?.UserProperties != null)
                {
                    foreach (var prop in ex.Result.UserProperties)
                    {
                        _logger.LogCritical($"{prop.Name}: {prop.Value}");
                    }
                }
            }
        }

        public void Publish(byte[] payload)
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic($"devices/{_clientName}/messages/events/")
                .WithPayload(payload)
                .Build();

            _client.PublishAsync(message).GetAwaiter().GetResult();
        }

        private MqttApplicationMessage BuildResponse(string status, string id, byte[] payload)
        {
            string responseTopic = Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC");

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

            string requestTopic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
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
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnPublishNodes"))
                {
                    responsePayload = UnpublishNodes(requestPayload);
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnPublishAllNodes"))
                {
                    responsePayload = UnpublishAllNodes(requestPayload);
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetDiagnosticInfo"))
                {
                    responsePayload = GetDiagnosticInfo(requestPayload);
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
                _logger.LogError(ex, "MQTTBrokerPublishReceived");

                // send error to MQTT broker
                await _client.PublishAsync(BuildResponse("500", requestID, Encoding.UTF8.GetBytes(ex.Message))).ConfigureAwait(false);
            }
        }

        public byte[] PublishNodes(string payload)
        {
            string logPrefix = "HandlePublishNodesMethodAsync:";
            OpcSessionUserAuthenticationMode desiredAuthenticationMode = OpcSessionUserAuthenticationMode.Anonymous;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            List<string> statusResponse = new List<string>();
            string statusMessage = string.Empty;
            PublishNodesMethodRequestModel publishNodesMethodData = null;

            try
            {
                _logger.LogDebug($"{logPrefix} called");
                publishNodesMethodData = JsonConvert.DeserializeObject<PublishNodesMethodRequestModel>(payload);

                if (publishNodesMethodData.OpcAuthenticationMode == OpcSessionUserAuthenticationMode.UsernamePassword)
                {
                    if (string.IsNullOrWhiteSpace(publishNodesMethodData.UserName) && string.IsNullOrWhiteSpace(publishNodesMethodData.Password))
                    {
                        throw new ArgumentException($"If {nameof(publishNodesMethodData.OpcAuthenticationMode)} is set to '{OpcSessionUserAuthenticationMode.UsernamePassword}', you have to specify '{nameof(publishNodesMethodData.UserName)}' and/or '{nameof(publishNodesMethodData.Password)}'.");
                    }

                    desiredAuthenticationMode = OpcSessionUserAuthenticationMode.UsernamePassword;
                }
            }
            catch (UriFormatException e)
            {
                statusMessage = $"Exception ({e.Message}) while parsing EndpointUrl '{publishNodesMethodData.EndpointUrl}'";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.NotAcceptable;
            }
            catch (Exception e)
            {
                statusMessage = $"Exception ({e.Message}) while deserializing message payload";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }

            if (statusCode == HttpStatusCode.OK)
            {
                try
                {
                    foreach (OpcNodeOnEndpointModel nodeOnEndpoint in publishNodesMethodData.OpcNodes)
                    {
                        EventPublishingModel node = new EventPublishingModel {
                            ExpandedNodeId = nodeOnEndpoint.ExpandedNodeId,
                            EndpointUrl = new Uri(publishNodesMethodData.EndpointUrl).ToString(),
                            SkipFirst = nodeOnEndpoint.SkipFirst,
                            DisplayName = nodeOnEndpoint.DisplayName,
                            HeartbeatInterval = nodeOnEndpoint.HeartbeatInterval,
                            OpcPublishingInterval = nodeOnEndpoint.OpcPublishingInterval,
                            OpcSamplingInterval = nodeOnEndpoint.OpcSamplingInterval,
                            UseSecurity = publishNodesMethodData.UseSecurity,
                            AuthCredential = null,
                            OpcAuthenticationMode = desiredAuthenticationMode
                        };

                        if (nodeOnEndpoint.ExpandedNodeId == null)
                        {
                            node.ExpandedNodeId = new Opc.Ua.ExpandedNodeId(nodeOnEndpoint.Id);
                        }

                        if (desiredAuthenticationMode == OpcSessionUserAuthenticationMode.UsernamePassword)
                        {
                            node.AuthCredential = new NetworkCredential(publishNodesMethodData.UserName, publishNodesMethodData.Password);
                        }

                        _uaClient.PublishNodeAsync(node).GetAwaiter().GetResult();
                    }
                }
                catch (AggregateException e)
                {
                    foreach (Exception ex in e.InnerExceptions)
                    {
                        _logger.LogError(ex, $"{logPrefix} Exception");
                    }
                    statusMessage = $"EndpointUrl: '{publishNodesMethodData.EndpointUrl}': exception ({e.Message}) while trying to publish";
                    _logger.LogError(e, $"{logPrefix} {statusMessage}");
                    statusResponse.Add(statusMessage);
                    statusCode = HttpStatusCode.InternalServerError;
                }
                catch (Exception e)
                {
                    statusMessage = $"EndpointUrl: '{publishNodesMethodData.EndpointUrl}': exception ({e.Message}) while trying to publish";
                    _logger.LogError(e, $"{logPrefix} {statusMessage}");
                    statusResponse.Add(statusMessage);
                    statusCode = HttpStatusCode.InternalServerError;
                }
            }

            return BuildResponseAndCropStatus(logPrefix, statusResponse);
        }

        public byte[] UnpublishNodes(string payload)
        {
            string logPrefix = "HandleUnpublishNodesMethodAsync:";
            UnpublishNodesMethodRequestModel unpublishNodesMethodData = null;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            List<string> statusResponse = new List<string>();
            string statusMessage = string.Empty;
            try
            {
                _logger.LogDebug($"{logPrefix} called");
                unpublishNodesMethodData = JsonConvert.DeserializeObject<UnpublishNodesMethodRequestModel>(payload);
            }
            catch (UriFormatException e)
            {
                statusMessage = $"Exception ({e.Message}) while parsing EndpointUrl '{unpublishNodesMethodData.EndpointUrl}'";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }
            catch (Exception e)
            {
                statusMessage = $"Exception ({e.Message}) while deserializing message payload";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }

            if (statusCode == HttpStatusCode.OK)
            {
                try
                {
                    foreach (OpcNodeOnEndpointModel nodeOnEndpoint in unpublishNodesMethodData.OpcNodes)
                    {
                        EventPublishingModel node = new EventPublishingModel {
                            ExpandedNodeId = nodeOnEndpoint.ExpandedNodeId,
                            EndpointUrl = new Uri(unpublishNodesMethodData.EndpointUrl).ToString(),
                            SkipFirst = nodeOnEndpoint.SkipFirst,
                            DisplayName = nodeOnEndpoint.DisplayName,
                            HeartbeatInterval = nodeOnEndpoint.HeartbeatInterval,
                            OpcPublishingInterval = nodeOnEndpoint.OpcPublishingInterval,
                            OpcSamplingInterval = nodeOnEndpoint.OpcSamplingInterval,
                        };

                        if (nodeOnEndpoint.ExpandedNodeId == null)
                        {
                            node.ExpandedNodeId = new Opc.Ua.ExpandedNodeId(nodeOnEndpoint.Id);
                        }

                        _uaClient.UnpublishNode(node);

                        // build response
                        statusMessage = $"All monitored items in all subscriptions{(unpublishNodesMethodData.EndpointUrl != null ? $" on endpoint '{unpublishNodesMethodData.EndpointUrl}'" : " ")} tagged for removal";
                        statusResponse.Add(statusMessage);
                        _logger.LogInformation($"{logPrefix} {statusMessage}");
                    }
                }
                catch (AggregateException e)
                {
                    foreach (Exception ex in e.InnerExceptions)
                    {
                        _logger.LogError(ex, $"{logPrefix} Exception");
                    }
                    statusMessage = $"EndpointUrl: '{unpublishNodesMethodData.EndpointUrl}': exception while trying to unpublish";
                    _logger.LogError(e, $"{logPrefix} {statusMessage}");
                    statusResponse.Add(statusMessage);
                    statusCode = HttpStatusCode.InternalServerError;
                }
                catch (Exception e)
                {
                    statusMessage = $"EndpointUrl: '{unpublishNodesMethodData.EndpointUrl}': exception ({e.Message}) while trying to unpublish";
                    _logger.LogError($"e, {logPrefix} {statusMessage}");
                    statusResponse.Add(statusMessage);
                    statusCode = HttpStatusCode.InternalServerError;
                }
            }

            return BuildResponseAndCropStatus(logPrefix, statusResponse);
        }

        public byte[] UnpublishAllNodes(string payload)
        {
            string logPrefix = "HandleUnpublishAllNodesMethodAsync:";
            Uri endpointUri = null;
            UnpublishAllNodesMethodRequestModel unpublishAllNodesMethodData = null;
            HttpStatusCode statusCode = HttpStatusCode.OK;
            List<string> statusResponse = new List<string>();
            string statusMessage = string.Empty;

            try
            {
                _logger.LogDebug($"{logPrefix} called");
                if (!string.IsNullOrEmpty(payload))
                {
                    unpublishAllNodesMethodData = JsonConvert.DeserializeObject<UnpublishAllNodesMethodRequestModel>(payload);
                }
                if (unpublishAllNodesMethodData != null && unpublishAllNodesMethodData?.EndpointUrl != null)
                {
                    endpointUri = new Uri(unpublishAllNodesMethodData.EndpointUrl);
                }
            }
            catch (UriFormatException e)
            {
                statusMessage = $"Exception ({e.Message}) while parsing EndpointUrl '{unpublishAllNodesMethodData.EndpointUrl}'";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }
            catch (Exception e)
            {
                statusMessage = $"Exception ({e.Message}) while deserializing message payload";
                _logger.LogError(e, $"{logPrefix} {statusMessage}");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }

            if (statusCode == HttpStatusCode.OK)
            {
                _uaClient.UnpublishAllNodes();
            }

            return BuildResponseAndCropStatus(logPrefix, statusResponse);
        }

        public byte[] GetDiagnosticInfo(string payload)
        {
            string logPrefix = "HandleGetDiagnosticInfoMethodAsync:";
            HttpStatusCode statusCode = HttpStatusCode.OK;
            List<string> statusResponse = new List<string>();
            string statusMessage = string.Empty;

            // get the diagnostic info
            DiagnosticInfoMethodResponseModel diagnosticInfoResponse = new DiagnosticInfoMethodResponseModel();
            try
            {
                List<DiagnosticInfo> diagnosticInfos = new List<DiagnosticInfo>();
                diagnosticInfos.Add(_diag.Info);
                diagnosticInfoResponse.DiagnosticInfos = diagnosticInfos;
            }
            catch (Exception e)
            {
                statusMessage = $"Exception ({e.Message}) while reading diagnostic info";
                _logger.LogError(e, $"{logPrefix} Exception");
                statusResponse.Add(statusMessage);
                statusCode = HttpStatusCode.InternalServerError;
            }

            // build response
            string resultString = null;
            if (statusCode == HttpStatusCode.OK)
            {
                resultString = JsonConvert.SerializeObject(diagnosticInfoResponse);
            }
            else
            {
                resultString = JsonConvert.SerializeObject(statusResponse);
            }

            return BuildResponseAndTruncateResult(logPrefix, resultString);
        }

        private byte[] BuildResponseAndCropStatus(string logPrefix, List<string> statusResponse)
        {
            byte[] result;
            int maxIndex = statusResponse.Count();
            while (true)
            {
                result = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(statusResponse.GetRange(0, maxIndex)));
                if (result.Length > SettingsConfiguration.MaxResponsePayloadLength)
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

        private byte[] BuildResponseAndTruncateResult(string logPrefix, string resultString)
        {
            byte[] result = Encoding.UTF8.GetBytes(resultString);

            if (result.Length > SettingsConfiguration.MaxResponsePayloadLength)
            {
                _logger.LogError($"{logPrefix} Response size is too long");
                Array.Resize(ref result, result.Length > SettingsConfiguration.MaxResponsePayloadLength ? SettingsConfiguration.MaxResponsePayloadLength : result.Length);
            }

            return result;
        }
    }
}