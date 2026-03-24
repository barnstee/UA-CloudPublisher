
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IMessageProcessor : IDisposable
    {
        Task RunAsync(CancellationToken cancellationToken = default);

        void ClearMetadataMessageCache();
    }
}
