
namespace UA.MQTT.Publisher.Interfaces
{
    using System;
    using System.Threading;

    public interface IMessageProcessingEngine : IDisposable
    {
        /// <summary>
        /// Run the engine
        /// </summary>
        /// <param name="cancellationToken"></param>
        void Run(CancellationToken cancellationToken = default);
    }
}
