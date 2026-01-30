
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAApplication
    {
        public ConsoleTelemetry Telemetry { get; }

        public X509Certificate2 IssuerCert { get; set; }

        ApplicationInstance UAApplicationInstance { get; set; }

        ReverseConnectManager ReverseConnectManager { get; set; }

        Task CreateAsync(CancellationToken cancellationToken = default);
    }
}