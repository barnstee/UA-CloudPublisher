
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using System;
    using System.Threading;

    public interface IMessageProcessor : IDisposable
    {
        void Run(CancellationToken cancellationToken = default);

        void ClearMetadataMessageCache();
    }
}
