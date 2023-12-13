
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAApplication
    {
        ApplicationInstance UAApplicationInstance { get; set; }

        ReverseConnectManager ReverseConnectManager { get; set; }

        Task CreateAsync(CancellationToken cancellationToken = default);
    }
}