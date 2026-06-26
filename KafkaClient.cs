using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Opc.Ua.Cloud.Publisher;
using Opc.Ua.Cloud.Publisher.Interfaces;
using Opc.Ua.Cloud.Publisher.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class KafkaClient : IBrokerClient
{
    private IProducer<Null, string> _producer = null;
    private IProducer<Null, string> _altProducer = null;
    private IConsumer<Ignore, byte[]> _consumer = null;

    private readonly ILogger _logger;
    private readonly ICommandProcessor _commandProcessor;

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    // Periodic liveness probe: catches "broker came back" while we are idle.
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
    private Timer _healthCheckTimer;
    private int _healthCheckInFlight;

    public KafkaClient(ILoggerFactory loggerFactory, ICommandProcessor commandProcessor)
    {
        _logger = loggerFactory.CreateLogger("KafkaClient");
        _commandProcessor = commandProcessor;
    }

    // Updates the diagnostics flag for the given broker (primary vs alt) and logs transitions.
    private void SetBrokerConnected(bool isAlt, bool connected, string reason)
    {
        bool previous = isAlt
            ? Diagnostics.Singleton.Info.ConnectedToAltBroker
            : Diagnostics.Singleton.Info.ConnectedToBroker;

        if (isAlt)
        {
            Diagnostics.Singleton.Info.ConnectedToAltBroker = connected;
        }
        else
        {
            Diagnostics.Singleton.Info.ConnectedToBroker = connected;
        }

        if (previous != connected)
        {
            string label = isAlt ? "alt Kafka broker" : "Kafka broker";
            if (connected)
            {
                _logger.LogInformation($"Connection to {label} restored ({reason}).");
            }
            else
            {
                _logger.LogWarning($"Connection to {label} lost ({reason}).");
            }
        }
    }

    // Builds the librdkafka asynchronous error handler. This fires on broker-down,
    // all-brokers-down, transport errors and fatal errors, even when we are not
    // actively producing - giving us a live signal to flip the diagnostics flag.
    private Action<IProducer<Null, string>, Error> BuildProducerErrorHandler(bool isAlt)
    {
        return (producer, error) =>
        {
            if (error == null)
            {
                return;
            }

            // Treat fatal errors and known disconnect codes as "not connected".
            bool isDisconnect =
                error.IsFatal
                || error.Code == ErrorCode.Local_AllBrokersDown
                || error.Code == ErrorCode.Local_Transport
                || error.Code == ErrorCode.Local_Authentication
                || error.Code == ErrorCode.BrokerNotAvailable
                || error.Code == ErrorCode.NetworkException;

            if (isDisconnect)
            {
                SetBrokerConnected(isAlt, false, $"{error.Code}: {error.Reason}");
            }
            else
            {
                _logger.LogWarning($"Kafka producer error ({(isAlt ? "alt" : "primary")}): {error.Code} - {error.Reason}");
            }
        };
    }

    // Wraps ProduceAsync so we can flip the diagnostics flag on each outcome:
    // success -> connected = true (recovery), ProduceException -> connected = false (e.g. Local_MsgTimedOut).
    private async Task TryPublishAsync(IProducer<Null, string> producer, string topic, Message<Null, string> message, bool isAlt)
    {
        try
        {
            await producer.ProduceAsync(topic, message, _cancellationTokenSource.Token).ConfigureAwait(false);

            SetBrokerConnected(isAlt, true, "publish succeeded");
        }
        catch (ProduceException<Null, string> ex)
        {
            SetBrokerConnected(isAlt, false, $"publish failed: {ex.Error.Code} - {ex.Error.Reason}");

            throw;
        }
    }

    // Lazily starts a background timer that periodically probes each producer with GetMetadata.
    // This catches the "broker came back" case while we are idle (no publishes in flight).
    private void EnsureHealthCheckTimerStarted()
    {
        if (_healthCheckTimer != null)
        {
            return;
        }

        _healthCheckTimer = new Timer(
            HealthCheckCallback,
            state: null,
            dueTime: HealthCheckInterval,
            period: HealthCheckInterval);
    }

    private void HealthCheckCallback(object _)
    {
        // Prevent overlapping probes if a previous one is still running.
        if (Interlocked.Exchange(ref _healthCheckInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            ProbeProducerHealth(_producer, isAlt: false);
            ProbeProducerHealth(_altProducer, isAlt: true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Kafka health check failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _healthCheckInFlight, 0);
        }
    }

    private void ProbeProducerHealth(IProducer<Null, string> producer, bool isAlt)
    {
        if (producer == null)
        {
            return;
        }

        try
        {
            using var adminClient = new DependentAdminClientBuilder(producer.Handle).Build();
            Metadata metadata = adminClient.GetMetadata(HealthCheckTimeout);

            bool healthy = metadata?.Brokers != null && metadata.Brokers.Count > 0;

            SetBrokerConnected(isAlt, healthy, healthy ? "health check ok" : "health check returned no brokers");
        }
        catch (KafkaException ex)
        {
            SetBrokerConnected(isAlt, false, $"health check failed: {ex.Error.Code} - {ex.Error.Reason}");
        }
        catch (Exception ex)
        {
            SetBrokerConnected(isAlt, false, $"health check failed: {ex.Message}");
        }
    }

    // Verifies the broker is actually reachable by requesting cluster metadata.
    // ProducerBuilder.Build() does not open a connection (it connects lazily on first use),
    // so we piggy-back a dependent AdminClient on the producer's handle and call GetMetadata
    // with a bounded timeout. Returns true if at least one broker responded, otherwise false.
    private bool VerifyBrokerConnection(IProducer<Null, string> producer, string brokerLabel)
    {
        if (producer == null)
        {
            return false;
        }

        try
        {
            using var adminClient = new DependentAdminClientBuilder(producer.Handle).Build();
            Metadata metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));

            if (metadata?.Brokers == null || metadata.Brokers.Count == 0)
            {
                _logger.LogError($"No brokers returned in metadata from {brokerLabel}.");
                return false;
            }

            _logger.LogInformation($"Verified connection to {brokerLabel}: {metadata.Brokers.Count} broker(s) reachable.");
            return true;
        }
        catch (KafkaException ex)
        {
            _logger.LogError($"Failed to verify connection to {brokerLabel}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error while verifying connection to {brokerLabel}: {ex.Message}");
            return false;
        }
    }

    public async Task ConnectAsync(bool altBroker = false)
    {
        try
        {
            if (altBroker)
            {
                // only (re)build the alt-broker producer; leave primary producer/consumer alone
                if (_altProducer != null)
                {
                    _altProducer.Flush();
                    _altProducer.Dispose();
                    _altProducer = null;

                    Diagnostics.Singleton.Info.ConnectedToAltBroker = false;
                }

                if (string.IsNullOrEmpty(Settings.Instance.AltBrokerUrl))
                {
                    _logger.LogError("Alt broker URL not configured. Cannot connect to alt broker!");

                    return;
                }

                var altConfig = new ProducerConfig
                {
                    BootstrapServers = Settings.Instance.AltBrokerUrl + ":" + Settings.Instance.AltBrokerPort,
                    MessageTimeoutMs = 10000,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Settings.Instance.AltBrokerUsername,
                    SaslPassword = Settings.Instance.AltBrokerPassword,
                    MessageSendMaxRetries = 2,
                    RetryBackoffMs = 100,
                    EnableIdempotence = true,
                    MaxInFlight = 5
                };

                _altProducer = new ProducerBuilder<Null, string>(altConfig)
                    .SetErrorHandler(BuildProducerErrorHandler(isAlt: true))
                    .Build();

                if (VerifyBrokerConnection(_altProducer, "alt Kafka broker"))
                {
                    Diagnostics.Singleton.Info.ConnectedToAltBroker = true;

                    _logger.LogInformation("Connected to alt Kafka broker.");
                }
                else
                {
                    _altProducer.Dispose();
                    _altProducer = null;

                    Diagnostics.Singleton.Info.ConnectedToAltBroker = false;

                    _logger.LogError("Could not verify connection to alt Kafka broker.");
                }

                EnsureHealthCheckTimerStarted();

                return;
            }

            // primary broker (re)connect: cancel any pending primary operations
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            // disconnect if still connected
            if (_producer != null)
            {
                _producer.Flush();
                _producer.Dispose();
                _producer = null;

                Diagnostics.Singleton.Info.ConnectedToBroker = false;
            }

            if (_altProducer != null)
            {
                _altProducer.Flush();
                _altProducer.Dispose();
                _altProducer = null;

                Diagnostics.Singleton.Info.ConnectedToAltBroker = false;
            }

            if (_consumer != null)
            {
                _consumer.Close();
                _consumer.Dispose();
                _consumer = null;
            }

            if (string.IsNullOrEmpty(Settings.Instance.BrokerUrl))
            {
                // no broker URL configured = nothing to connect to!
                _logger.LogError("Broker URL not configured. Cannot connect to broker!");
                return;
            }

            // create Kafka client
            var config = new ProducerConfig
            {
                BootstrapServers = Settings.Instance.BrokerUrl + ":" + Settings.Instance.BrokerPort,
                MessageTimeoutMs = 10000,
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = Settings.Instance.BrokerUsername,
                SaslPassword = Settings.Instance.BrokerPassword,
                MessageSendMaxRetries = 2, // Reduce internal retries to fail fast and let store-and-forward handle it
                RetryBackoffMs = 100,
                EnableIdempotence = true, // Enable idempotence for exactly-once semantics
                MaxInFlight = 5 // Limit in-flight requests to prevent ordering issues during retries
            };

            _producer = new ProducerBuilder<Null, string>(config)
                .SetErrorHandler(BuildProducerErrorHandler(isAlt: false))
                .Build();

            if (VerifyBrokerConnection(_producer, "Kafka broker"))
            {
                Diagnostics.Singleton.Info.ConnectedToBroker = true;
            }
            else
            {
                _producer.Dispose();
                _producer = null;

                Diagnostics.Singleton.Info.ConnectedToBroker = false;

                _logger.LogError("Could not verify connection to Kafka broker.");
                return;
            }

            if (Settings.Instance.UseAltBrokerForMetadata && Settings.Instance.UseKafkaForAlt)
            {
                var altConfig = new ProducerConfig
                {
                    BootstrapServers = Settings.Instance.AltBrokerUrl + ":" + Settings.Instance.AltBrokerPort,
                    MessageTimeoutMs = 10000,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Settings.Instance.AltBrokerUsername,
                    SaslPassword = Settings.Instance.AltBrokerPassword,
                    MessageSendMaxRetries = 2,
                    RetryBackoffMs = 100,
                    EnableIdempotence = true,
                    MaxInFlight = 5
                };

                _altProducer = new ProducerBuilder<Null, string>(altConfig)
                    .SetErrorHandler(BuildProducerErrorHandler(isAlt: true))
                    .Build();

                if (VerifyBrokerConnection(_altProducer, "alt Kafka broker"))
                {
                    Diagnostics.Singleton.Info.ConnectedToAltBroker = true;
                }
                else
                {
                    _altProducer.Dispose();
                    _altProducer = null;

                    Diagnostics.Singleton.Info.ConnectedToAltBroker = false;

                    _logger.LogError("Could not verify connection to alt Kafka broker (metadata path).");
                }
            }

            if (!string.IsNullOrEmpty(Settings.Instance.BrokerCommandTopic))
            {
                var conf = new ConsumerConfig
                {
                    GroupId = Settings.Instance.PublisherName,
                    BootstrapServers = Settings.Instance.BrokerUrl + ":" + Settings.Instance.BrokerPort,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Settings.Instance.BrokerUsername,
                    SaslPassword = Settings.Instance.BrokerPassword
                };

                _consumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                _consumer.Subscribe(Settings.Instance.BrokerCommandTopic);

                // start background task to handle incoming commands
                _ = Task.Run(async () => await HandleCommandAsync().ConfigureAwait(false));
            }

            EnsureHealthCheckTimerStarted();

            _logger.LogInformation("Connected to Kafka broker.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Failed to connect to Kafka broker: " + ex.Message);
        }
    }

    public async Task PublishAsync(byte[] payload)
    {
        if (_producer == null)
        {
            throw new InvalidOperationException("Kafka producer is not connected.");
        }

        Message<Null, string> message = new()
        {
            Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
            Value = Encoding.UTF8.GetString(payload)
        };

        await TryPublishAsync(_producer, Settings.Instance.BrokerMessageTopic, message, isAlt: false).ConfigureAwait(false);
    }

    public async Task PublishMetadataAsync(byte[] payload, IReadOnlyDictionary<string, string> cloudEventAttributes = null)
    {
        Headers headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } };

        if (cloudEventAttributes != null)
        {
            // CloudEvents binary content mode for Kafka: each attribute is carried as a "ce_"-prefixed header
            // ('datacontenttype' is conveyed by the Content-Type header above).
            foreach (KeyValuePair<string, string> attribute in cloudEventAttributes)
            {
                if (string.Equals(attribute.Key, "datacontenttype", StringComparison.Ordinal))
                {
                    continue;
                }

                headers.Add("ce_" + attribute.Key, Encoding.UTF8.GetBytes(attribute.Value));
            }
        }

        Message<Null, string> message = new()
        {
            Headers = headers,
            Value = Encoding.UTF8.GetString(payload)
        };

        if (Settings.Instance.UseAltBrokerForMetadata && Settings.Instance.UseKafkaForAlt)
        {
            if (_altProducer == null)
            {
                throw new InvalidOperationException("Alternate Kafka producer is not connected.");
            }

            await TryPublishAsync(_altProducer, Settings.Instance.BrokerMetadataTopic, message, isAlt: true).ConfigureAwait(false);
        }
        else
        {
            if (_producer == null)
            {
                throw new InvalidOperationException("Kafka producer is not connected.");
            }

            await TryPublishAsync(_producer, Settings.Instance.BrokerMetadataTopic, message, isAlt: false).ConfigureAwait(false);
        }
    }

    public async Task PublishResponseAsync(byte[] payload)
    {
        if (_producer == null)
        {
            throw new InvalidOperationException("Kafka producer is not connected.");
        }

        Message<Null, string> message = new()
        {
            Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
            Value = Encoding.UTF8.GetString(payload)
        };

        await TryPublishAsync(_producer, Settings.Instance.BrokerResponseTopic, message, isAlt: false).ConfigureAwait(false);
    }

    // handles all incoming commands form the cloud
    private async Task HandleCommandAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            ResponseModel response = new()
            {
                TimeStamp = DateTime.UtcNow,
            };

            try
            {
                ConsumeResult<Ignore, byte[]> result = _consumer.Consume(_cancellationTokenSource.Token);

                string requestPayload = Encoding.UTF8.GetString(result.Message.Value);
                _logger.LogInformation($"Received method call with topic: {result.Topic} and payload: {requestPayload}");

                // parse the message
                RequestModel request = JsonConvert.DeserializeObject<RequestModel>(requestPayload);

                // discard messages that are older than 15 seconds
                if (request.TimeStamp < DateTime.UtcNow.AddSeconds(-15))
                {
                    _logger.LogInformation($"Discarding old message with timestamp {request.TimeStamp}");
                    continue;
                }

                response.CorrelationId = request.CorrelationId;

                // route this to the right handler
                if (request.Command == "publishnodes")
                {
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.PublishNodesAsync(requestPayload).ConfigureAwait(false));
                    response.Success = true;
                }
                else if (request.Command == "unpublishnodes")
                {
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.UnpublishNodesAsync(requestPayload).ConfigureAwait(false));
                    response.Success = true;
                }
                else if (request.Command == "unpublishallnodes")
                {
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.UnpublishAllNodesAsync().ConfigureAwait(false));
                    response.Success = true;
                }
                else if (request.Command == "getpublishednodes")
                {
                    response.Status = Encoding.UTF8.GetString(_commandProcessor.GetPublishedNodes());
                    response.Success = true;
                }
                else if (request.Command == "getinfo")
                {
                    response.Status = Encoding.UTF8.GetString(_commandProcessor.GetInfo());
                    response.Success = true;
                }
                else
                {
                    _logger.LogError("Unknown command received: " + result.Topic);
                    response.Status = "Unknown command " + result.Topic;
                    response.Success = false;
                }

                // send response to Kafka broker
                await PublishResponseAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    break;
                }

                _logger.LogError(ex, "HandleMessageAsync");
                response.Status = ex.Message;
                response.Success = false;

                // send error to Kafka broker
                try
                {
                    await PublishResponseAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))).ConfigureAwait(false);
                }
                catch (Exception publishEx)
                {
                    _logger.LogError(publishEx, "Failed to publish error response");
                }
            }
        }
    }
}