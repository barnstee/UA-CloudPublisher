
namespace UA.MQTT.Publisher.Interfaces
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAApplication
    {
        Task CreateAsync(CancellationToken cancellationToken = default);

        ApplicationConfiguration GetAppConfig();
    }
}