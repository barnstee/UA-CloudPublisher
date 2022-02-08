
namespace UA.MQTT.Publisher.Interfaces
{
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Published nodes file parser interface
    /// </summary>
    public interface IPublishedNodesFileHandler
    {
        /// <summary>
        /// Parses the provided file and publishes the OPC UA nodes specified
        /// </summary>
        bool ParseFile(string filePath, X509Certificate2 cert);
    }
}
