
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using UA.MQTT.Publisher.Interfaces;

    public class MQTTPublisher : IMessagePublisher
    {
        private IMQTTSubscriber _client;

        private readonly ILogger _logger;
        private readonly IPeriodicDiagnosticsInfo _diag;

        private Queue<long> _lastMessageLatencies = new Queue<long>();

        public MQTTPublisher(IPeriodicDiagnosticsInfo diag,
                               ILoggerFactory loggerFactory,
                               IMQTTSubscriber subscriber)
        {
            _logger = loggerFactory.CreateLogger("MQTTMessageSink");
            _diag = diag;
            _client = subscriber;
        }

        public void SendMessage(byte[] message)
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            try
            {
                if (_client != null)
                {
                    _client.Publish(message);
                }
                else
                {
                    _logger.LogError("MQTT client not available for sending message.");
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException)
                {
                    ex = ((AggregateException)ex).Flatten();
                }
                _logger.LogError(ex, "Error while sending message. Dropping...");
                _diag.Info.FailedMessages++;
            }

            watch.Stop();

            _lastMessageLatencies.Enqueue(watch.ElapsedMilliseconds);

            // calc the average for the last 100 messages
            if (_lastMessageLatencies.Count > 100)
            {
                _lastMessageLatencies.Dequeue();
            }

            long sum = 0;
            foreach (long latency in _lastMessageLatencies)
            {
                sum += latency;
            }

            _diag.Info.AverageMessageLatency = sum / _lastMessageLatencies.Count;
        }
    }
}
