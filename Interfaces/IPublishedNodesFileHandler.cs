
namespace UA.MQTT.Publisher.Interfaces
{
    using System.Security.Cryptography.X509Certificates;

    public interface IPublishedNodesFileHandler
    {
        void ParseFile(byte[] content);
    }
}
