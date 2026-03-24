namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class StoreForwardPublisher : IMessagePublisher
    {
        private IBrokerClient _client;

        private readonly ILogger _logger;
        private readonly string _pathToStore;

        private readonly ConcurrentQueue<long> _lastMessageLatencies = new();
        private int _latencyCount;
        private long _latencySum;

        public StoreForwardPublisher(ILoggerFactory loggerFactory, Settings.BrokerResolver brokerResolver)
        {
            _logger = loggerFactory.CreateLogger("StoreForwardPublisher");

            _pathToStore = Path.Combine(Directory.GetCurrentDirectory(), "store");
            if (!Directory.Exists(_pathToStore))
            {
                Directory.CreateDirectory(_pathToStore);
            }

            if (Settings.Instance.UseKafka)
            {
                _client = brokerResolver("Kafka");
            }
            else
            {
                _client = brokerResolver("MQTT");
            }
        }

        public void ApplyNewClient(IBrokerClient client)
        {
            _client = client;
        }

        public async Task<bool> SendMetadataAsync(byte[] message)
        {
            bool success = false;
            long startTime = Stopwatch.GetTimestamp();

            try
            {
                if (_client != null)
                {
                    await _client.PublishMetadataAsync(message).ConfigureAwait(false);
                    success = true;

                    Diagnostics.Singleton.Info.SentBytes += message.Length;
                    Diagnostics.Singleton.Info.SentMessages++;
                    Diagnostics.Singleton.Info.SentLastTime = DateTime.UtcNow;
                }
                else
                {
                    _logger.LogError("Broker client not available for sending metadata.");
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException agg)
                {
                    ex = agg.Flatten();
                }

                _logger.LogError(ex, "Error while sending metadata message.");
            }

            long elapsed = Stopwatch.GetTimestamp() - startTime;
            RecordLatency(Stopwatch.GetElapsedTime(0, elapsed).Milliseconds);

            return success;
        }

        public async Task<bool> SendMessageAsync(byte[] message)
        {
            bool success = false;
            long startTime = Stopwatch.GetTimestamp();

            try
            {
                if (_client != null)
                {
                    await _client.PublishAsync(message).ConfigureAwait(false);
                    success = true;

                    Diagnostics.Singleton.Info.SentBytes += message.Length;
                    Diagnostics.Singleton.Info.SentMessages++;
                    Diagnostics.Singleton.Info.SentLastTime = DateTime.UtcNow;

                    // forward stored messages in the background
                    _ = Task.Run(async () => await ForwardStoredMessageAsync().ConfigureAwait(false));
                }
                else
                {
                    throw new InvalidOperationException("Broker client not available for sending message.");
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException agg)
                {
                    ex = agg.Flatten();
                }

                _logger.LogError(ex, "Error while sending message. Storing locally for later forward...");
                Diagnostics.Singleton.Info.FailedMessages++;

                await File.WriteAllBytesAsync(Path.Combine(_pathToStore, Path.GetRandomFileName()), message).ConfigureAwait(false);
            }

            long elapsed = Stopwatch.GetTimestamp() - startTime;
            RecordLatency(Stopwatch.GetElapsedTime(0, elapsed).Milliseconds);

            return success;
        }

        private async Task ForwardStoredMessageAsync()
        {
            try
            {
                string[] filePaths = Directory.GetFiles(_pathToStore);
                if (filePaths.Length == 0)
                {
                    // nothing to send
                    return;
                }

                byte[] bytes = await File.ReadAllBytesAsync(filePaths[0]).ConfigureAwait(false);
                await _client.PublishAsync(bytes).ConfigureAwait(false);

                File.Delete(filePaths[0]);

                Diagnostics.Singleton.Info.SentBytes += bytes.Length;
                Diagnostics.Singleton.Info.SentMessages++;
                Diagnostics.Singleton.Info.FailedMessages = Math.Max(0, Diagnostics.Singleton.Info.FailedMessages - 1);
                Diagnostics.Singleton.Info.SentLastTime = DateTime.UtcNow;

                _logger.LogInformation("There are {Count} stored messages left to send.", filePaths.Length - 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending stored message failed, will retry later.");
            }
        }

        private void RecordLatency(long milliseconds)
        {
            _lastMessageLatencies.Enqueue(milliseconds);
            Interlocked.Add(ref _latencySum, milliseconds);
            int count = Interlocked.Increment(ref _latencyCount);

            while (count > 100 && _lastMessageLatencies.TryDequeue(out long old))
            {
                Interlocked.Add(ref _latencySum, -old);
                count = Interlocked.Decrement(ref _latencyCount);
            }

            long sum = Interlocked.Read(ref _latencySum);
            Diagnostics.Singleton.Info.AverageMessageLatency = count > 0 ? sum / count : 0;
        }
    }
}
