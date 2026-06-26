namespace Opc.Ua.Cloud.Publisher.Configuration
{
    using Microsoft.Extensions.Logging;
    using MQTTnet;
    using MQTTnet.Exceptions;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Buffers;
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
        private IMqttClient _altClient = null;
        private bool _isAltBroker = false;

        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IUAApplication _uAApplication;

        private readonly object _decoderLock = new object();

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public MQTTClient(ILoggerFactory loggerFactory, ICommandProcessor commandProcessor, IUAApplication uAApplication)
        {
            _logger = loggerFactory.CreateLogger("MQTTClient");
            _loggerFactory = loggerFactory;
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

                    // try PFX first, then fall back to a plain certificate file
                    try
                    {
                        appCert = X509CertificateLoader.LoadPkcs12FromFile(filePath, string.Empty);
                    }
                    catch
                    {
                        appCert = X509CertificateLoader.LoadCertificateFromFile(filePath);
                    }
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

        public async Task ConnectAsync(bool altBroker = false)
        {
            _isAltBroker = altBroker;

            try
            {
                // disconnect if still connected
                if (_client != null)
                {
                    if (_client.IsConnected)
                    {
                        await _client.DisconnectAsync().ConfigureAwait(false);
                    }

                    _client.Dispose();
                    _client = null;

                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();

                    if (altBroker)
                    {
                        Diagnostics.Singleton.Info.ConnectedToAltBroker = false;
                    }
                    else
                    {
                        Diagnostics.Singleton.Info.ConnectedToBroker = false;
                    }
                }

                // tear down any existing alt metadata client (owned by the primary instance)
                if (_altClient != null)
                {
                    if (_altClient.IsConnected)
                    {
                        await _altClient.DisconnectAsync().ConfigureAwait(false);
                    }

                    _altClient.Dispose();
                    _altClient = null;

                    Diagnostics.Singleton.Info.ConnectedToAltBroker = false;
                }

                // read our settings
                string brokerUrl = altBroker ? Settings.Instance.AltBrokerUrl : Settings.Instance.BrokerUrl;
                uint brokerPort = altBroker ? Settings.Instance.AltBrokerPort : Settings.Instance.BrokerPort;
                string username = altBroker ? Settings.Instance.AltBrokerUsername : Settings.Instance.BrokerUsername;
                string password = altBroker ? Settings.Instance.AltBrokerPassword : Settings.Instance.BrokerPassword;
                string receiveTopic = altBroker ? Settings.Instance.BrokerDataReceivedTopic : Settings.Instance.BrokerCommandTopic;

                if (string.IsNullOrEmpty(brokerUrl))
                {
                    // no broker URL configured = nothing to connect to!
                    _logger.LogError("Broker URL not configured. Cannot connect to broker!");
                    return;
                }

                // create MQTT client
                _client = new MqttClientFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg);

                MqttClientOptionsBuilder clientOptions = BuildClientOptions(brokerUrl, brokerPort, username, password);

                // capture this specific client + cancellation token so the reconnect loop below only ever
                // manages this connection attempt and stops once the client is replaced or shut down
                IMqttClient thisClient = _client;
                CancellationToken thisToken = _cancellationTokenSource.Token;

                // setup disconnection handling with automatic reconnect-until-successful
                _client.DisconnectedAsync += async disconnectArgs =>
                {
                    // ignore disconnects from a client instance we have already replaced
                    if (!ReferenceEquals(_client, thisClient))
                    {
                        return;
                    }

                    _logger.LogWarning($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    await ReconnectLoopAsync(thisClient, clientOptions, receiveTopic, thisToken, () => ReferenceEquals(_client, thisClient), SetConnected, "MQTT broker").ConfigureAwait(false);
                };

                try
                {
                    MqttClientConnectResult connectResult = await _client.ConnectAsync(clientOptions.Build(), _cancellationTokenSource.Token).ConfigureAwait(false);

                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    await SubscribeReceiveTopicAsync(_client, receiveTopic).ConfigureAwait(false);

                    if (altBroker)
                    {
                        Diagnostics.Singleton.Info.ConnectedToAltBroker = true;
                    }
                    else
                    {
                        Diagnostics.Singleton.Info.ConnectedToBroker = true;
                    }

                    _logger.LogInformation("Connected to MQTT broker.");

                    // when the alt broker is used for metadata and is the same kind (MQTT) as the primary broker,
                    // connect a dedicated client to it here (mirrors the Kafka client's alt producer)
                    if (!altBroker && Settings.Instance.UseAltBrokerForMetadata && !Settings.Instance.UseKafkaForAlt)
                    {
                        await ConnectAltMetadataClientAsync().ConfigureAwait(false);
                    }
                }
                catch (MqttCommunicationException ex)
                {
                    _logger.LogCritical($"Failed to connect with reason {ex.HResult} and message: {ex.Message}");
                    if ((ex.Data != null) && (ex.Data.Count > 0))
                    {
                        foreach (var prop in ex.Data)
                        {
                            _logger.LogCritical($"{prop.ToString()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed to connect to MQTT broker: " + ex.Message);
            }
        }

        // Updates the broker-connected diagnostics flag for this instance (primary broker vs. a separate alt instance).
        private void SetConnected(bool connected)
        {
            if (_isAltBroker)
            {
                Diagnostics.Singleton.Info.ConnectedToAltBroker = connected;
            }
            else
            {
                Diagnostics.Singleton.Info.ConnectedToBroker = connected;
            }
        }

        // Subscribes the given client to its receive topic (command topic for the primary broker, data-received
        // topic for a separate alt instance). No-op when no receive topic is configured.
        private async Task SubscribeReceiveTopicAsync(IMqttClient client, string receiveTopic)
        {
            if (string.IsNullOrEmpty(receiveTopic))
            {
                return;
            }

            MqttClientSubscribeResult subscribeResult = await client.SubscribeAsync(
                new MqttTopicFilter
                {
                    Topic = receiveTopic,
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                }).ConfigureAwait(false);

            // make sure subscriptions were successful
            if (subscribeResult.Items.Count != 1 || subscribeResult.Items.ElementAt(0).ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
            {
                throw new ApplicationException("Failed to subscribe");
            }
        }

        // Keeps trying to reconnect the given client until it succeeds, the token is cancelled, or the client is
        // replaced by a newer connection attempt (isCurrent returns false). On a successful reconnect it re-subscribes
        // (clean sessions drop subscriptions on disconnect) and restores the connected diagnostics flag.
        private async Task ReconnectLoopAsync(IMqttClient client, MqttClientOptionsBuilder options, string receiveTopic, CancellationToken token, Func<bool> isCurrent, Action<bool> setConnected, string label)
        {
            // we just lost the connection, so reflect that immediately
            setConnected(false);

            while (!token.IsCancellationRequested && isCurrent() && !client.IsConnected)
            {
                try
                {
                    // wait before (re)trying so we don't hammer the broker
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);

                    MqttClientConnectResult connectResult = await client.ConnectAsync(options.Build(), token).ConfigureAwait(false);

                    if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        // a clean session drops server-side subscriptions, so re-subscribe after reconnecting
                        await SubscribeReceiveTopicAsync(client, receiveTopic).ConfigureAwait(false);

                        _logger.LogInformation($"Reconnected to {label}.");
                        break;
                    }

                    string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                    _logger.LogWarning($"Reconnect to {label} failed. Status: {connectResult.ResultCode}; status: {status}. Retrying in 5 seconds...");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Reconnect to {label} failed: {ex.Message}. Retrying in 5 seconds...");
                }
            }

            // restore the flag only if this client is still the active one and actually connected
            if (isCurrent() && client.IsConnected)
            {
                setConnected(true);
            }
        }

        // Builds the MQTT client options for the given broker, applying the same SAS-token, TLS, WebSocket and
        // certificate logic used for the primary broker so the alt broker connection behaves identically.
        private MqttClientOptionsBuilder BuildClientOptions(string brokerUrl, uint brokerPort, string username, string password)
        {
            string publisherName = Settings.Instance.PublisherName;

            // create MQTT password
            if (Settings.Instance.CreateBrokerSASToken)
            {
                // create SAS token as password
                TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                int week = 60 * 60 * 24 * 7;
                string expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                string stringToSign = HttpUtility.UrlEncode(brokerUrl + "/devices/" + publisherName) + "\n" + expiry;
                using HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(password));
                string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                password = "SharedAccessSignature sr=" + HttpUtility.UrlEncode(brokerUrl + "/devices/" + publisherName) + "&sig=" + HttpUtility.UrlEncode(signature) + "&se=" + expiry;
            }

            MqttClientOptionsBuilder clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(brokerUrl, (int?)brokerPort)
                    .WithClientId(publisherName)
                    .WithTlsOptions(new MqttClientTlsOptions { UseTls = Settings.Instance.UseTLS })
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
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
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
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

            return clientOptions;
        }

        // Connects a dedicated client to the alt broker for publishing metadata. This mirrors the Kafka client's
        // alt producer: the primary instance owns a second connection used only for metadata when the alt broker
        // is configured for metadata and is the same kind (MQTT) as the primary broker.
        private async Task ConnectAltMetadataClientAsync()
        {
            string brokerUrl = Settings.Instance.AltBrokerUrl;
            uint brokerPort = Settings.Instance.AltBrokerPort;
            string username = Settings.Instance.AltBrokerUsername;
            string password = Settings.Instance.AltBrokerPassword;

            if (string.IsNullOrEmpty(brokerUrl))
            {
                _logger.LogError("Alt broker URL not configured. Cannot connect to alt broker!");
                return;
            }

            MqttClientOptionsBuilder altClientOptions = BuildClientOptions(brokerUrl, brokerPort, username, password);

            _altClient = new MqttClientFactory().CreateMqttClient();

            // capture this specific client + cancellation token so the reconnect loop below only ever
            // manages this connection attempt and stops once the client is replaced or shut down
            IMqttClient thisAltClient = _altClient;
            CancellationToken thisAltToken = _cancellationTokenSource.Token;

            // setup disconnection handling with automatic reconnect-until-successful
            _altClient.DisconnectedAsync += async disconnectArgs =>
            {
                // ignore disconnects from a client instance we have already replaced
                if (!ReferenceEquals(_altClient, thisAltClient))
                {
                    return;
                }

                _logger.LogWarning($"Disconnected from alt MQTT broker: {disconnectArgs.Reason}");

                await ReconnectLoopAsync(thisAltClient, altClientOptions, null, thisAltToken, () => ReferenceEquals(_altClient, thisAltClient), connected => Diagnostics.Singleton.Info.ConnectedToAltBroker = connected, "alt MQTT broker").ConfigureAwait(false);
            };

            try
            {
                MqttClientConnectResult connectResult = await _altClient.ConnectAsync(altClientOptions.Build(), _cancellationTokenSource.Token).ConfigureAwait(false);

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    string status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                    throw new Exception($"Connection to alt MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                }

                Diagnostics.Singleton.Info.ConnectedToAltBroker = true;

                _logger.LogInformation("Connected to alt MQTT broker.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed to connect to alt MQTT broker: " + ex.Message);

                _altClient?.Dispose();
                _altClient = null;

                Diagnostics.Singleton.Info.ConnectedToAltBroker = false;
            }
        }

        public async Task PublishAsync(byte[] payload)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("MQTT client is not connected.");
            }

            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic(Settings.Instance.BrokerMessageTopic)
                .WithPayload(payload)
                .Build();

            await _client.PublishAsync(message, _cancellationTokenSource.Token).ConfigureAwait(false);
        }

        public async Task PublishMetadataAsync(byte[] payload)
        {
            // when the alt broker is used for metadata and is the same kind (MQTT) as the primary broker, the
            // primary instance routes metadata to its dedicated alt client (mirrors the Kafka client). A separate
            // alt-broker instance (_isAltBroker) always publishes via its own _client.
            if (!_isAltBroker && Settings.Instance.UseAltBrokerForMetadata && !Settings.Instance.UseKafkaForAlt)
            {
                if (_altClient == null)
                {
                    throw new InvalidOperationException("Alternate MQTT client is not connected.");
                }

                MqttApplicationMessage altMessage = new MqttApplicationMessageBuilder()
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithTopic(Settings.Instance.BrokerMetadataTopic)
                    .WithPayload(payload)
                    .Build();

                await _altClient.PublishAsync(altMessage, _cancellationTokenSource.Token).ConfigureAwait(false);

                return;
            }

            if (_client == null)
            {
                throw new InvalidOperationException("MQTT client is not connected.");
            }

            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic(Settings.Instance.BrokerMetadataTopic)
                .WithPayload(payload)
                .Build();

            await _client.PublishAsync(message, _cancellationTokenSource.Token).ConfigureAwait(false);
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
            if (properties == null)
            {
                return null;
            }

            MqttUserProperty status = properties.FirstOrDefault(up => up.Name == "status");
            if (status == null)
            {
                return null;
            }

            return int.Parse(status.ReadValueAsString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
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
                _logger.LogInformation($"Received cloud command with topic: {args.ApplicationMessage.Topic} and payload: {args.ApplicationMessage.ConvertPayloadToString()}");

                string requestTopic = Settings.Instance.BrokerCommandTopic;
                int queryStart = args.ApplicationMessage.Topic.IndexOf('?');
                if (queryStart >= 0 && queryStart < args.ApplicationMessage.Topic.Length - 1)
                {
                    // skip the leading '?' so the ID is usable on its own
                    requestID = args.ApplicationMessage.Topic.Substring(queryStart + 1);
                }

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
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.PublishNodesAsync(requestPayload).ConfigureAwait(false));
                    response.Success = true;
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishNodes"))
                {
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.UnpublishNodesAsync(requestPayload).ConfigureAwait(false));
                    response.Success = true;
                }
                else if (args.ApplicationMessage.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishAllNodes"))
                {
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.UnpublishAllNodesAsync().ConfigureAwait(false));
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
                    response.Status = "Unknown command " + args.ApplicationMessage.Topic;
                    response.Success = false;
                }

                // send response to MQTT broker
                await _client.PublishAsync(BuildResponse("200", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleMessageAsync");
                response.Status = ex.Message;
                response.Success = false;

                // send error to MQTT broker
                try
                {
                    if (_client != null)
                    {
                        await _client.PublishAsync(BuildResponse("500", requestID, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))), _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception publishEx)
                {
                    _logger.LogError(publishEx, "Failed to publish error response");
                }
            }
        }
    }
}