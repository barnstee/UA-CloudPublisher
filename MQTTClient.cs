
namespace Opc.Ua.Cloud.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using MQTTnet;
    using MQTTnet.Adapter;
    using MQTTnet.Client;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Dashboard;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    public class MQTTClient : IBrokerClient
    {
        private IMqttClient _client = null;
        private bool _isAltBroker = false;

        private readonly ILogger _logger;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IUAApplication _uAApplication;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public MQTTClient(ILoggerFactory loggerFactory, ICommandProcessor commandProcessor, IUAApplication uAApplication)
        {
            _logger = loggerFactory.CreateLogger("MQTTClient");
            _commandProcessor = commandProcessor;
            _uAApplication = uAApplication;
        }

        public class MqttClientCertificatesProvider : IMqttClientCertificatesProvider
        {
            private readonly IUAApplication _uAApplication;

            public MqttClientCertificatesProvider(IUAApplication uAApplication)
            {
                _uAApplication = uAApplication;
            }

            X509CertificateCollection IMqttClientCertificatesProvider.GetCertificates()
            {
                X509Certificate2 appCert = null;
                if (Settings.Instance.UseCustomCertAuth)
                {
                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), "customclientcert");
                    appCert = new X509Certificate2(filePath);
                }
                else
                {
                    appCert = _uAApplication.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;
                }

                if (appCert == null)
                {
                    throw new Exception($"Cannot access OPC UA application certificate!");
                }

                return new X509CertificateCollection() { appCert };
            }
        }

        public void Connect(bool altBroker = false)
        {
            _isAltBroker = altBroker;

            try
            {
                // disconnect if still connected
                if ((_client != null) && _client.IsConnected)
                {
                    _client.DisconnectAsync().GetAwaiter().GetResult();

                    _cancellationTokenSource.Cancel();

                    Diagnostics.Singleton.Info.ConnectedToBroker = false;
                }

                // read our settings
                string brokerUrl = altBroker ? Settings.Instance.AltBrokerUrl : Settings.Instance.BrokerUrl;
                uint brokerPort = altBroker ? Settings.Instance.AltBrokerPort : Settings.Instance.BrokerPort;
                string publisherName = Settings.Instance.PublisherName;
                string username = altBroker ? Settings.Instance.AltBrokerUsername : Settings.Instance.BrokerUsername;
                string password = altBroker ? Settings.Instance.AltBrokerPassword : Settings.Instance.BrokerPassword;
                string receiveTopic = altBroker ? Settings.Instance.BrokerDataReceivedTopic : Settings.Instance.BrokerCommandTopic;

                if (string.IsNullOrEmpty(brokerUrl))
                {
                    // no broker URL configured = nothing to connect to!
                    _logger.LogError("Broker URL not configured. Cannot connect to broker!");
                    return;
                }

                // create MQTT password
                if (Settings.Instance.CreateBrokerSASToken)
                {
                    // create SAS token as password
                    TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                    int week = 60 * 60 * 24 * 7;
                    string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                    string stringToSign = HttpUtility.UrlEncode(brokerUrl + "/devices/" + publisherName) + "\n" + expiry;
                    HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                    string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(brokerUrl + "/devices/" + publisherName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
                }

                // create MQTT client
                _client = new MqttFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg);

                MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerUrl, (int?)brokerPort)
                        .WithClientId(publisherName)
                        .WithTlsOptions(new MqttClientTlsOptions { UseTls = Settings.Instance.UseTLS })
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(username, password);

                if (brokerPort == 443)
                {
                    clientOptions = new MqttClientOptionsBuilder()
                        .WithWebSocketServer(o => o.WithUri(brokerUrl))
                        .WithClientId(publisherName)
                        .WithTlsOptions(new MqttClientTlsOptions { UseTls = Settings.Instance.UseTLS })
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(username, password);
                }

                if (Settings.Instance.UseUACertAuth || Settings.Instance.UseCustomCertAuth)
                {
                    clientOptions = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerUrl)
                        .WithClientId(publisherName)
                        .WithTlsOptions(new MqttClientTlsOptions
                        {
                            UseTls = true,
                            AllowUntrustedCertificates = true,
                            IgnoreCertificateChainErrors = true,
                            ClientCertificatesProvider = new MqttClientCertificatesProvider(_uAApplication)
                        })
                        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                        .WithCleanSession(true) // clear existing subscriptions
                        .WithCredentials(publisherName, string.Empty);
                }

                // setup disconnection handling
                _client.DisconnectedAsync += disconnectArgs =>
                {
                    _logger.LogWarning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    // wait a 5 seconds, then simply reconnect again, if needed
                    Task.Delay(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                    MqttClientConnectResult connectResult = _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).GetAwaiter().GetResult();
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    return Task.CompletedTask;
                };

                try
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();

                    MqttClientConnectResult connectResult = _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).GetAwaiter().GetResult();
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    if (!string.IsNullOrEmpty(receiveTopic))
                    {
                        MqttClientSubscribeResult subscribeResult = _client.SubscribeAsync(
                        new MqttTopicFilter
                        {
                            Topic = receiveTopic,
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                        }).GetAwaiter().GetResult();

                        // make sure subscriptions were successful
                        if (subscribeResult.Items.Count != 1 || subscribeResult.Items.ElementAt(0).ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                        {
                            throw new ApplicationException("Failed to subscribe");
                        }
                    }

                    Diagnostics.Singleton.Info.ConnectedToBroker = true;

                    _logger.LogInformation("Connected to MQTT broker.");
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
                .WithTopic(Settings.Instance.BrokerMessageTopic)
                .WithPayload(payload)
                .Build();

            _client.PublishAsync(message, _cancellationTokenSource.Token).GetAwaiter().GetResult();
        }

        public void PublishMetadata(byte[] payload)
        {
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic(Settings.Instance.BrokerMetadataTopic)
                .WithPayload(payload)
                .Build();

            _client.PublishAsync(message, _cancellationTokenSource.Token).GetAwaiter().GetResult();
        }

        private MqttApplicationMessage BuildResponse(string status, string id, byte[] payload)
        {
            string responseTopic = Settings.Instance.BrokerResponseTopic;

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
            string requestID = string.Empty;

            ResponseModel response = new()
            {
                TimeStamp = DateTime.UtcNow,
            };

            try
            {
                // check if this should be a data message (and we are the alternative broker) or a command
                if (_isAltBroker)
                {
                    new UAPubSubBinaryMessageDecoder(_uAApplication, new LoggerFactory()).ProcessMessage(args.ApplicationMessage.PayloadSegment.ToArray(), DateTime.UtcNow, args.ApplicationMessage.ContentType);
                }
                else
                {
                    _logger.LogInformation($"Received cloud command with topic: {args.ApplicationMessage.Topic} and payload: {args.ApplicationMessage.ConvertPayloadToString()}");

                    string requestTopic = Settings.Instance.BrokerCommandTopic;
                    requestID = args.ApplicationMessage.Topic.Substring(args.ApplicationMessage.Topic.IndexOf("?"));

                    string requestPayload = args.ApplicationMessage.ConvertPayloadToString();

                    // parse the message
                    RequestModel request = JsonConvert.DeserializeObject<RequestModel>(requestPayload);

                    // discard messages that are older than 15 seconds
                    if (request.TimeStamp < DateTime.UtcNow.AddSeconds(-15))
                    {
                        _logger.LogInformation($"Discarding old message with timestamp {request.TimeStamp}");
                        return;
                    }

                    response.CorrelationId = request.CorrelationId;
                    // route this to the right handler
                    if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "PublishNodes"))
                    {
                        response.Status = Encoding.UTF8.GetString(_commandProcessor.PublishNodes(requestPayload));
                        response.Success = true;
                    }
                    else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishNodes"))
                    {
                        response.Status = Encoding.UTF8.GetString(_commandProcessor.UnpublishNodes(requestPayload));
                        response.Success = true;
                    }
                    else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishAllNodes"))
                    {
                        response.Status = Encoding.UTF8.GetString(_commandProcessor.UnpublishAllNodes());
                        response.Success = true;
                    }
                    else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetPublishedNodes"))
                    {
                        response.Status = Encoding.UTF8.GetString(_commandProcessor.GetPublishedNodes());
                        response.Success = true;
                    }
                    else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetInfo"))
                    {
                        response.Status = Encoding.UTF8.GetString(_commandProcessor.GetInfo());
                        response.Success = true;
                    }
                    else
                    {
                        _logger.LogError("Unknown command received: " + args.ApplicationMessage.Topic);
                        response.Status = "Unkown command " + args.ApplicationMessage.Topic;
                        response.Success = false;
                    }

                    // send reponse to MQTT broker
                    await _client.PublishAsync(BuildResponse("200", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleMessageAsync");
                response.Status = ex.Message;
                response.Success = false;

                // send error to MQTT broker
                await _client.PublishAsync(BuildResponse("500", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }
    }
}