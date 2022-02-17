
namespace UA.MQTT.Publisher.Interfaces
{
    using System.Security.Cryptography.X509Certificates;

    public interface IPublishedNodesFileHandler
    {
        bool ParseFile(byte[] content, X509Certificate2 cert);
    }
}
