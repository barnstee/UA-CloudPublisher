
namespace UA.MQTT.Publisher.Interfaces
{
    using System;
    using System.Threading;

    public interface IMessageProcessor : IDisposable
    {
        void Run(CancellationToken cancellationToken = default);
    }
}
