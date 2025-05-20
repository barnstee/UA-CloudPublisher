
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IPublishedNodesFileHandler
    {
        int Progress { get; set; }

        void ParseFile(byte[] content);
    }
}
