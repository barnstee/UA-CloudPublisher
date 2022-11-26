
namespace Opc.Ua.Cloud.Publisher.Configuration
{
    using Confluent.Kafka;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System;
    using System.Text;
    using System.Threading;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System.Threading.Tasks;

    public class KafkaClient : IBrokerClient
    {
        private IProducer<Null, string> _producer = null;
        private IConsumer<Ignore, byte[]> _consumer = null;

        private readonly ILogger _logger;
        private readonly ICommandProcessor _commandProcessor;

        public KafkaClient(ILoggerFactory loggerFactory, ICommandProcessor commandProcessor)
        {
            _logger = loggerFactory.CreateLogger("KafkaClient");
            _commandProcessor = commandProcessor;
        }

        public void Connect()
        {
            try
            {
                // disconnect if still connected
                if (_producer != null)
                {
                    _producer.Flush();
                    _producer.Dispose();
                    _producer = null;

                    Diagnostics.Singleton.Info.ConnectedToBroker = false;
                }

                if (_consumer != null)
                {
                    _consumer.Close();
                    _consumer.Dispose();
                    _consumer = null;
                }

                // create Kafka client
                var config = new ProducerConfig {
                    BootstrapServers = Settings.Instance.BrokerUrl + ":" + Settings.Instance.BrokerPort,
                    MessageTimeoutMs = 10000,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Settings.Instance.BrokerUsername,
                    SaslPassword = Settings.Instance.BrokerPassword
                };

                _producer = new ProducerBuilder<Null, string>(config).Build();

                if (!string.IsNullOrEmpty(Settings.Instance.BrokerCommandTopic))
                {
                    var conf = new ConsumerConfig
                    {
                        GroupId = Settings.Instance.BrokerClientName,
                        BootstrapServers = Settings.Instance.BrokerUrl + ":" + Settings.Instance.BrokerPort,
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        SecurityProtocol = SecurityProtocol.SaslSsl,
                        SaslMechanism = SaslMechanism.Plain,
                        SaslUsername = Settings.Instance.BrokerUsername,
                        SaslPassword = Settings.Instance.BrokerPassword
                    };

                    _consumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                    _consumer.Subscribe(Settings.Instance.BrokerCommandTopic);

                    _ = Task.Run(() => HandleCommand());
                }

                Diagnostics.Singleton.Info.ConnectedToBroker = true;

                _logger.LogInformation("Connected to Kafka broker.");

            }
            catch (Exception ex)
            {
                _logger.LogCritical("Failed to connect to Kafka broker: " + ex.Message);
            }
        }

        public void Publish(byte[] payload)
        {
            Message<Null, string> message = new() {
                Headers = new Headers() { { "Content-Type", Encoding.UTF8.GetBytes("application/json") } },
                Value = Encoding.UTF8.GetString(payload)
            };

            _producer.ProduceAsync(Settings.Instance.BrokerMessageTopic, message).GetAwaiter().GetResult();
        }

        public void PublishMetadata(byte[] payload)
        {
            _producer.ProduceAsync(Settings.Instance.BrokerMetadataTopic, new Message<Null, string> { Value = Encoding.UTF8.GetString(payload) }).GetAwaiter().GetResult();
        }

        // handles all incoming commands form the cloud
        private void HandleCommand()
        {
            while (true)
            {
                Thread.Sleep(1000);

                try
                {
                    ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

                    _logger.LogInformation($"Received method call with topic: {result.Topic} and payload: {result.Message.Value}");

                    string requestTopic = Settings.Instance.BrokerCommandTopic;
                    string requestPayload = Encoding.UTF8.GetString(result.Message.Value);
                    byte[] responsePayload = null;

                    // try to retrieve the request id from the topic
                    string requestID = null;
                    if (result.Topic.IndexOf("?") != -1)
                    {
                        requestID = result.Topic.Substring(result.Topic.IndexOf("?"));
                    }

                    if (string.IsNullOrEmpty(requestID))
                    {
                        // retrieve it from the message instead
                        // TODO
                    }

                    // route this to the right handler
                    if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "PublishNodes"))
                    {
                        responsePayload = _commandProcessor.PublishNodes(requestPayload);
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishNodes"))
                    {
                        responsePayload = _commandProcessor.UnpublishNodes(requestPayload);
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "UnpublishAllNodes"))
                    {
                        responsePayload = _commandProcessor.UnpublishAllNodes();
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetPublishedNodes"))
                    {
                        responsePayload = _commandProcessor.GetPublishedNodes();
                    }
                    else if (result.Topic.StartsWith(requestTopic.TrimEnd('#') + "GetInfo"))
                    {
                        responsePayload = _commandProcessor.GetInfo();
                    }
                    else
                    {
                        _logger.LogError("Unknown command received: " + result.Topic);
                        responsePayload = Encoding.UTF8.GetBytes("Unkown command!");
                    }

                    // send reponse to Kafka broker
                    Publish(responsePayload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HandleMessageAsync");

                    // send error to Kafka broker
                    Publish(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ex.Message)));
                }
            }
        }
    }
}