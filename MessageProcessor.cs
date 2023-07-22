
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using System.Linq;

    public class MessageProcessor : IMessageProcessor
    {
        private static ulong _messageID = 0;
        private bool _batchEmpty = true;
        private bool _singleMessageSend = false;
        private int _messageClosingParenthesisSize = 2;
        DateTime _nextSendTime = DateTime.UtcNow;

        private Queue<long> _lastNotificationInBatch = new Queue<long>();
        private int _notificationsInBatch = 0;

        MemoryStream _batchBuffer = new MemoryStream();
        private static BlockingCollection<MessageProcessorModel> _monitoredItemsDataQueue;

        private Dictionary<ushort, string> _metadataMessages = new Dictionary<ushort, string>();
        private object _metadataMessagesLock = new object();

        private Timer _metadataTimer;
        private Timer _statusTimer;
        private bool _isRunning = false;

        private static ILogger _logger;
        private readonly IMessageEncoder _encoder;
        private readonly IMessagePublisher _sink;

        public MessageProcessor(
            IMessageEncoder encoder,
            ILoggerFactory loggerFactory,
            IMessagePublisher sink)
        {
            _logger = loggerFactory.CreateLogger("MessageProcessor");
            _encoder = encoder;
            _sink = sink;

            if (Settings.Instance.MetadataSendInterval != 0)
            {
                _metadataTimer = new Timer(SendMetadataOnTimer, null, (int)Settings.Instance.MetadataSendInterval * 1000, (int)Settings.Instance.MetadataSendInterval * 1000);
            }

            if (Settings.Instance.SendUAStatus)
            {
                _statusTimer = new Timer(SendStatusOnTimer, null, (int)Settings.Instance.DiagnosticsLoggingInterval * 1000, (int)Settings.Instance.DiagnosticsLoggingInterval * 1000);
            }
        }

        public void ClearMetadataMessageCache()
        {
            lock (_metadataMessagesLock)
            {
                _metadataMessages.Clear();
            }
        }

        public void Dispose()
        {
            _batchBuffer.Dispose();

            if (_monitoredItemsDataQueue != null)
            {
                _monitoredItemsDataQueue.Dispose();
            }
        }

        public static void Enqueue(MessageProcessorModel json)
        {
            if (_monitoredItemsDataQueue != null)
            {
                if (_monitoredItemsDataQueue.TryAdd(json) == false)
                {
                    Diagnostics.Singleton.Info.EnqueueFailureCount++;

                    // log an error message for every 10K messages lost
                    if (Diagnostics.Singleton.Info.EnqueueFailureCount % 10000 == 0)
                    {
                        _logger.LogError($"The internal monitored item message queue is above its capacity of {_monitoredItemsDataQueue.BoundedCapacity}. We have lost {Diagnostics.Singleton.Info.EnqueueFailureCount} monitored item notifications so far.");
                    }
                }
                else
                {
                    Diagnostics.Singleton.Info.EnqueueCount++;
                    Diagnostics.Singleton.Info.MonitoredItemsQueueCount = _monitoredItemsDataQueue.Count;
                }
            }
        }

        public void Run(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                _logger.LogError("Message Processor is already running.");
                return;
            }

            Init();
            _isRunning = true;

            while (true)
            {
                try
                {
                    // read the next message from our queue
                    MessageProcessorModel messageData = new MessageProcessorModel();
                    int timeout = CalculateBatchTimeout(cancellationToken);
                    bool gotItem = _monitoredItemsDataQueue.TryTake(out messageData, timeout, cancellationToken);
                    if (!gotItem)
                    {
                        // timeout or shutdown case (cancellation)
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogInformation($"Cancellation requested.");
                            _monitoredItemsDataQueue.CompleteAdding();
                            break;
                        }
                        else
                        {
                            // timeout (i.e. send interval reached). Check if there is something in the buffer and send it now
                            _logger.LogTrace($"Send interval reached at {_nextSendTime}");
                            if (!_batchEmpty)
                            {
                                // send what we have so far
                                SendBatch(FinishBatch());
                                continue;
                            }
                            else
                            {
                                // nothing to send, reset the clock and keep waiting
                                _logger.LogTrace("Adding {seconds} seconds to current nextSendTime {nextSendTime}...", Settings.Instance.DefaultSendIntervalSeconds, _nextSendTime);
                                _nextSendTime += TimeSpan.FromSeconds(Settings.Instance.DefaultSendIntervalSeconds);
                                continue;
                            }
                        }
                    }
                    else
                    {
                        Diagnostics.Singleton.Info.MonitoredItemsQueueCount = _monitoredItemsDataQueue.Count;
                    }

                    // check if we should send the new item straight away (single message send case or if there are events)
                    if (_singleMessageSend || (messageData.EventValues.Count > 0))
                    {
                        BatchMessage(JsonEncodeMessage(messageData));
                        SendBatch(FinishBatch());
                    }
                    else
                    {
                        // batch message instead
                        string jsonMessage = JsonEncodeMessage(messageData);
                        int jsonMessageSize = Encoding.UTF8.GetByteCount(jsonMessage);
                        uint hubMessageBufferSize = Settings.Instance.BrokerMessageSize > 0 ? Settings.Instance.BrokerMessageSize : Settings.HubMessageSizeMax;
                        int encodedMessagePropertiesLengthMax = 512;

                        // reduce the message payload by the space occupied by the message properties
                        hubMessageBufferSize -= (uint)encodedMessagePropertiesLengthMax;

                        // check if the message will fit into our batch in principle
                        if (jsonMessageSize > hubMessageBufferSize)
                        {
                            _logger.LogError($"Configured hub message size {hubMessageBufferSize} too small to even fit the generated telemetry message of {jsonMessageSize}. Please adjust. The telemetry message will be discarded!");
                            Diagnostics.Singleton.Info.TooLargeCount++;
                            continue;
                        }

                        // check if the message still fits into out batch, otherwise send what we have so far and start a new batch with the message
                        if ((_batchBuffer.Position + jsonMessageSize + _messageClosingParenthesisSize) < hubMessageBufferSize)
                        {
                            BatchMessage(jsonMessage);
                        }
                        else
                        {
                            SendBatch(FinishBatch());
                            BatchMessage(jsonMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }
                    else
                    {
                        _logger.LogError(ex, "Error while processing messages!");
                    }
                }
            }
        }

        private void Init()
        {
            _logger.LogInformation($"Message processing configured with a send interval of {Settings.Instance.DefaultSendIntervalSeconds} sec and a message buffer size of {Settings.Instance.BrokerMessageSize} bytes.");

            // create the queue for monitored items
            _monitoredItemsDataQueue = new BlockingCollection<MessageProcessorModel>((int)Settings.Instance.InternalQueueCapacity);

            _singleMessageSend = Settings.Instance.DefaultSendIntervalSeconds == 0 && Settings.Instance.BrokerMessageSize == 0;

            InitBatch();

            // init our send time
            _nextSendTime = DateTime.UtcNow + TimeSpan.FromSeconds(Settings.Instance.DefaultSendIntervalSeconds);
        }

        private void BatchMessage(string jsonMessage)
        {
            _batchBuffer.Write(Encoding.UTF8.GetBytes(jsonMessage));
            _batchBuffer.Write(Encoding.UTF8.GetBytes(","));

            _logger.LogDebug($"Batching message with size {Encoding.UTF8.GetByteCount(jsonMessage)}, size is now {_batchBuffer.Position - 1}.");

            _batchEmpty = false;

            _notificationsInBatch++;
        }

        private byte[] FinishBatch()
        {
            // remove the trailing comma and finish the JSON message
            _batchBuffer.Position -= 1;

            _batchBuffer.Write(Encoding.UTF8.GetBytes("]}"));

            _lastNotificationInBatch.Enqueue(_notificationsInBatch);

            // calc the average for the last 100 batches
            if (_lastNotificationInBatch.Count > 100)
            {
                _lastNotificationInBatch.Dequeue();
            }

            long sum = 0;
            foreach (long notificationInBatch in _lastNotificationInBatch)
            {
                sum += notificationInBatch;
            }

            Diagnostics.Singleton.Info.AverageNotificationsInBrokerMessage = sum / _lastNotificationInBatch.Count;

            return _batchBuffer.ToArray();
        }

        private void SendBatch(byte[] bytesToSend)
        {
            if (_sink.SendMessage(bytesToSend))
            {
                _logger.LogDebug($"Sent {bytesToSend.Length} bytes to broker!");
            }

            // reset our batch
            InitBatch();

            // reset our send time
            _nextSendTime = DateTime.UtcNow + TimeSpan.FromSeconds(Settings.Instance.DefaultSendIntervalSeconds);
        }

        private void SendStatusOnTimer(object state)
        {
            // stop the timer while we're sending
            _statusTimer.Change(Timeout.Infinite, Timeout.Infinite);

            using (MemoryStream buffer = new MemoryStream())
            {
                buffer.Write(Encoding.UTF8.GetBytes(_encoder.EncodeStatus(_messageID++)));
                if (_sink.SendMetadata(buffer.ToArray()))
                {
                    _logger.LogDebug($"Sent status message to broker!");
                }
            }

            // restart the timer
            _statusTimer.Change((int)Settings.Instance.DiagnosticsLoggingInterval * 1000, (int)Settings.Instance.DiagnosticsLoggingInterval * 1000);
        }

        private void SendMetadataOnTimer(object state)
        {
            // stop the timer while we're sending
            _metadataTimer.Change(Timeout.Infinite,Timeout.Infinite);

            if (_metadataMessages.Count > 0)
            {
                KeyValuePair<ushort, string>[] currentMessages = null;
                lock (_metadataMessagesLock)
                {
                    currentMessages = _metadataMessages.ToArray();
                }

                if (currentMessages != null)
                {
                    foreach (KeyValuePair<ushort, string> metadataMessage in currentMessages)
                    {
                        using (MemoryStream buffer = new MemoryStream())
                        {
                            buffer.Write(Encoding.UTF8.GetBytes(_encoder.EncodeHeader(_messageID++, true)));
                            buffer.Write(Encoding.UTF8.GetBytes(","));
                            buffer.Write(Encoding.UTF8.GetBytes(metadataMessage.Value));

                            if (_sink.SendMetadata(buffer.ToArray()))
                            {
                                _logger.LogDebug($"Sent {_batchBuffer.Length} metadata bytes to broker!");
                            }
                        }
                    }
                }
            }

            // restart the timer
            _metadataTimer.Change((int)Settings.Instance.MetadataSendInterval * 1000, (int)Settings.Instance.MetadataSendInterval * 1000);
        }

        private string JsonEncodeMessage(MessageProcessorModel messageData)
        {
            ushort hash;
            string jsonMessage = _encoder.EncodePayload(messageData, out hash);

            if (Settings.Instance.SendUAMetadata)
            {
                string metadataMessage = _encoder.EncodeMetadata(messageData);
                if (!_metadataMessages.ContainsKey(hash))
                {
                    lock (_metadataMessagesLock)
                    {
                        _metadataMessages.Add(hash, metadataMessage);
                    }

                    using (MemoryStream buffer = new MemoryStream())
                    {
                        buffer.Write(Encoding.UTF8.GetBytes(_encoder.EncodeHeader(_messageID++, true)));
                        buffer.Write(Encoding.UTF8.GetBytes(","));
                        buffer.Write(Encoding.UTF8.GetBytes(metadataMessage));

                        if (_sink.SendMetadata(buffer.ToArray()))
                        {
                            _logger.LogDebug($"Sent {_batchBuffer.Length} metadata bytes to broker!");
                        }
                    }
                }
            }

            Diagnostics.Singleton.Info.NumberOfEvents++;

            return jsonMessage;
        }

        private int CalculateBatchTimeout(CancellationToken cancellationToken = default)
        {
            int timeout;

            // sanity check the send interval
            if (Settings.Instance.DefaultSendIntervalSeconds > 0)
            {
                TimeSpan timeTillNextSend = _nextSendTime.Subtract(DateTime.UtcNow);
                if (timeTillNextSend < TimeSpan.Zero)
                {
                    Diagnostics.Singleton.Info.MissedSendIntervalCount++;

                    // no wait if the send interval was missed
                    timeTillNextSend = TimeSpan.Zero;
                }

                long millisLong = (long)timeTillNextSend.TotalMilliseconds;
                if (millisLong < 0 || millisLong > int.MaxValue)
                {
                    timeout = 0;
                }
                else
                {
                    timeout = (int)millisLong;
                }
            }
            else
            {
                // no wait if shutdown is requested, else infinite wait if send interval is not set
                timeout = cancellationToken.IsCancellationRequested ? 0 : Timeout.Infinite;
            }

            return timeout;
        }

        private void InitBatch()
        {
            _batchEmpty = true;
            _batchBuffer.Position = 0;
            _batchBuffer.SetLength(0);
            _notificationsInBatch = 0;

            string pubSubJSONNetworkMessageHeader = _encoder.EncodeHeader(_messageID++);

            _batchBuffer.Write(Encoding.UTF8.GetBytes(pubSubJSONNetworkMessageHeader));
        }
    }
}