
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Opc.Ua.Cloud.Publisher.Interfaces;

    public class LocalFileStorage : IFileStorage
    {
        private readonly ILogger _logger;

        public LocalFileStorage(ILoggerFactory logger)
        {
            _logger = logger.CreateLogger("LocalFileStorage");
        }

        public Task<string> FindFileAsync(string path, string name, CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (string filePath in Directory.GetFiles(path))
                {
                    if (filePath.Contains(name))
                    {
                        return Task.FromResult(filePath);
                    }
                }

                return Task.FromResult(string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return Task.FromResult(string.Empty);
            }
        }

        public async Task<string> StoreFileAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path) || (content == null))
            {
                return null;
            }

            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                }

                await File.WriteAllBytesAsync(path, content).ConfigureAwait(false);

                return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }

        public async Task<byte[]> LoadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }
    }
}
