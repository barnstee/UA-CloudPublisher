using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Opc.Ua.Cloud.Publisher;
using Opc.Ua.Cloud.Publisher.Interfaces;
using Opc.Ua.Cloud.Publisher.Models;
using System;
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

    public KafkaClient(ILoggerFactory loggerFactory, ICommandProcessor commandProcessor)
    {
        _logger = loggerFactory.CreateLogger("KafkaClient");
        _commandProcessor = commandProcessor;
    }

    public void Connect(bool altBroker = false)
    {
        try
        {
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

            _producer = new ProducerBuilder<Null, string>(config).Build();

            if (Settings.Instance.UseAltBrokerForMetadata)
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

                _altProducer = new ProducerBuilder<Null, string>(altConfig).Build();
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

                _ = Task.Run(HandleCommand);
            }

            Diagnostics.Singleton.Info.ConnectedToBroker = true;

            _logger.LogInformation("Connected to Kafka broker.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Failed to connect to Kafka broker: " + ex.Message);
        }
    }

    public async Task PublishAsync(byte[] payload)
    {
        Message<Null, string> message = new()
        {
            Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
            Value = Encoding.UTF8.GetString(payload)
        };

        await _producer.ProduceAsync(Settings.Instance.BrokerMessageTopic, message, _cancellationTokenSource.Token).ConfigureAwait(false);
    }

    public async Task PublishMetadataAsync(byte[] payload)
    {
        Message<Null, string> message = new()
        {
            Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
            Value = Encoding.UTF8.GetString(payload)
        };

        if (Settings.Instance.UseAltBrokerForMetadata)
        {
            await _altProducer.ProduceAsync(Settings.Instance.BrokerMetadataTopic, message, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
        else
        {
            await _producer.ProduceAsync(Settings.Instance.BrokerMetadataTopic, message, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
    }

    public async Task PublishResponseAsync(byte[] payload)
    {
        Message<Null, string> message = new()
        {
            Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
            Value = Encoding.UTF8.GetString(payload)
        };

        await _producer.ProduceAsync(Settings.Instance.BrokerResponseTopic, message, _cancellationTokenSource.Token).ConfigureAwait(false);
    }

    // handles all incoming commands form the cloud
    private async Task HandleCommand()
    {
        while (true)
        {
            ResponseModel response = new()
            {
                TimeStamp = DateTime.UtcNow,
            };

            try
            {
                ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

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
                    response.Status = Encoding.UTF8.GetString(await _commandProcessor.PublishNodes(requestPayload).ConfigureAwait(false));
                    response.Success = true;
                }
                else if (request.Command == "unpublishnodes")
                {
                    response.Status = Encoding.UTF8.GetString(_commandProcessor.UnpublishNodes(requestPayload));
                    response.Success = true;
                }
                else if (request.Command == "unpublishallnodes")
                {
                    response.Status = Encoding.UTF8.GetString(_commandProcessor.UnpublishAllNodes());
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
                await PublishAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleMessageAsync");
                response.Status = ex.Message;
                response.Success = false;

                // send error to Kafka broker
                try
                {
                    await PublishAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response))).ConfigureAwait(false);
                }
                catch (Exception publishEx)
                {
                    _logger.LogError(publishEx, "Failed to publish error response");
                }
            }
        }
    }
}