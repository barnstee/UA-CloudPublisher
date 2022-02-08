
namespace UA.MQTT.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Web;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;
    using uPLibrary.Networking.M2Mqtt;
    using uPLibrary.Networking.M2Mqtt.Messages;
    using DiagnosticInfo = Models.DiagnosticInfo;

    public class MQTTSubscriber : IMQTTSubscriber
    {
        private MqttClient _mqttClient = null;
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
            _mqttClient = new MqttClient(brokerName, 8883, true, MqttSslProtocols.TLSv1_2, CertificateValidationCallback, null);

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

            // register publish received and disconnect handler callbacks
            _mqttClient.MqttMsgPublishReceived += PublishReceived;
            _mqttClient.ConnectionClosed += ConnectionClosed;

            // subscribe to all our topics
            _mqttClient.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            // connect to MQTT broker
            byte returnCode = _mqttClient.Connect(_clientName, userName, password, false, 5);
            if (returnCode != MqttMsgConnack.CONN_ACCEPTED)
            {
                _logger.LogError("Connection to MQTT broker failed with " + returnCode.ToString() + "!");
            }
            else
            {
                _logger.LogInformation("Connected to MQTT broker.");
            }
        }

        public void Publish(byte[] payload)
        {
            string topic = "devices/" + _clientName + "/messages/events/";
            _mqttClient.Publish(topic, payload, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
        }

        private void ConnectionClosed(object sender, EventArgs e)
        {
            _logger.LogWarning("Disconnected from MQTT broker.");

            // simply reconnect again
            Connect();
        }

        private void PublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string requestTopic = Environment.GetEnvironmentVariable("MQTT_TOPIC");
            string responseTopic = Environment.GetEnvironmentVariable("MQTT_RESPONSE_TOPIC");
            string requestID = e.Topic.Substring(e.Topic.IndexOf("?"));

            try
            {
                string requestPayload = Encoding.UTF8.GetString(e.Message);
                byte[] responsePayload = null;

                // route this to the right handler
                if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "PublishNodes"))
                {
                    responsePayload = PublishNodes(requestPayload);
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnPublishNodes"))
                {
                    responsePayload = UnpublishNodes(requestPayload);
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnPublishAllNodes"))
                {
                    responsePayload = UnpublishAllNodes(requestPayload);
                }
                else if (e.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetDiagnosticInfo"))
                {
                    responsePayload = GetDiagnosticInfo(requestPayload);
                }
                else
                {
                    _logger.LogError("Unknown command received: " + e.Topic);
                }

                // send reponse to MQTT broker
                _mqttClient.Publish(responseTopic + "/200/" + requestID, responsePayload, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTTBrokerPublishReceived");

                // send error to MQTT broker
                _mqttClient.Publish(responseTopic + "/500/" + requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
            }
        }

        private bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // always trust the MQTT broker certificate
            return true;
        }

        /// <summary>
        /// Handle publish node method call.
        /// </summary>
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

        /// <summary>
        /// Handle unpublish node method call.
        /// </summary>
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

        /// <summary>
        /// Handle unpublish all nodes method call.
        /// </summary>
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

        /// <summary>
        /// Handle method call to get diagnostic information.
        /// </summary>
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