using System.Threading.Tasks;

namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    public interface ICommandProcessor
    {
        Task<byte[]> PublishNodesAsync(string payload);

        Task<byte[]> UnpublishNodesAsync(string payload);

        Task<byte[]> UnpublishAllNodesAsync();

        byte[] GetPublishedNodes();

        byte[] GetInfo();
    }
}
