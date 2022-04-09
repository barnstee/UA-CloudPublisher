
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using UA.MQTT.Publisher.Interfaces;

    public class StoreForwardPublisher : IMessagePublisher
    {
        private IBrokerClient _client;

        private readonly ILogger _logger;

        private Queue<long> _lastMessageLatencies = new Queue<long>();

        public StoreForwardPublisher(ILoggerFactory loggerFactory, IBrokerClient subscriber)
        {
            _logger = loggerFactory.CreateLogger("StoreForwardPublisher");
            _client = subscriber;
        }

        public bool SendMetadata(byte[] message)
        {
            bool success = false;

            try
            {
                if (_client != null)
                {
                    _client.PublishMetadata(message);
                    success = true;
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

                _logger.LogError(ex, "Error while sending metadata message.");
            }

            return success;
        }

        public bool SendMessage(byte[] message)
        {
            bool success = false;

            string pathToStore = Path.Combine(Directory.GetCurrentDirectory(), "store");

            Stopwatch watch = new Stopwatch();
            watch.Start();

            try
            {
                if (_client != null)
                {
                    _client.Publish(message);
                    success = true;

                    Diagnostics.Singleton.Info.SentBytes += message.Length;
                    Diagnostics.Singleton.Info.SentMessages++;
                    Diagnostics.Singleton.Info.SentLastTime = DateTime.UtcNow;

                    if (!Directory.Exists(pathToStore))
                    {
                        Directory.CreateDirectory(pathToStore);
                    }

                    // check if there are still messages in our store we should also send
                    string[] filePaths = Directory.GetFiles(pathToStore);
                    if (filePaths.Length > 0)
                    {
                        // send at least 1
                        _logger.LogInformation("Forwarding stored message to Broker, now that the connection has been re-established...");

                        try
                        {
                            byte[] bytes = File.ReadAllBytes(filePaths[0]);
                            _client.Publish(bytes);

                            File.Delete(filePaths[0]);

                            Diagnostics.Singleton.Info.SentBytes += bytes.Length;
                            Diagnostics.Singleton.Info.SentMessages++;
                            Diagnostics.Singleton.Info.FailedMessages--;
                            Diagnostics.Singleton.Info.SentLastTime = DateTime.UtcNow;

                            _logger.LogInformation($"There are {filePaths.Length - 1} stored messages left to send.");
                        }
                        catch (Exception ex)
                        {
                            // do nothing, just try again next time around
                            _logger.LogError(ex, $"Sending stored message failed, will retry later. There are {filePaths.Length} stored messages left to send.");
                        }
                    }
                }
                else
                {
                    throw new Exception("MQTT client not available for sending message.");
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException)
                {
                    ex = ((AggregateException)ex).Flatten();
                }

                _logger.LogError(ex, "Error while sending message. Storing locally for later forward...");
                Diagnostics.Singleton.Info.FailedMessages++;

                File.WriteAllBytes(Path.Combine(pathToStore, Path.GetRandomFileName()), message);
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

            Diagnostics.Singleton.Info.AverageMessageLatency = sum / _lastMessageLatencies.Count;

            return success;
        }
    }
}
