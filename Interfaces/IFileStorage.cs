
namespace UA.MQTT.Publisher.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IFileStorage
    {
        Task<string> FindFileAsync(string path, string name, CancellationToken cancellationToken = default);

        Task<string> StoreFileAsync(string name, byte[] content, CancellationToken cancellationToken = default);

        Task<byte[]> LoadFileAsync(string name, CancellationToken cancellationToken = default);
    }
}
