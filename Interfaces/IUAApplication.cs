
namespace UA.MQTT.Publisher.Interfaces
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAApplication
    {
        /// <summary>
        /// Creates a new OPC UA application
        /// </summary>
        Task CreateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the application configuration
        /// </summary>
        /// <returns>The OPC UA Application Configuration</returns>
        ApplicationConfiguration GetAppConfig();
    }
}