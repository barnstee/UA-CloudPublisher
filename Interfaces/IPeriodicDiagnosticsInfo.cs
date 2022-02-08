
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Logs periodic diagnostics info
    /// </summary>
    public interface IPeriodicDiagnosticsInfo
    {
        /// <summary>
        /// The Diagnostic info
        /// </summary>
        DiagnosticInfo Info { get; set; }

        /// <summary>
        /// Clear all metrics
        /// </summary>
        void Clear();

        /// <summary>
        /// Kicks of the task to show diagnostic information
        /// </summary>
        Task RunAsync(CancellationToken cancellationToken = default);
    }
}