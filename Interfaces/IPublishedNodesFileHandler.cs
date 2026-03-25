
using System.Threading.Tasks;

namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface IPublishedNodesFileHandler
    {
        int Progress { get; set; }

        Task ParseFileAsync(byte[] content);
    }
}
