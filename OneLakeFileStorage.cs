
namespace Opc.Ua.Cloud.Publisher
{
    using Azure.Identity;
    using Azure.Storage.Files.DataLake;
    using Azure.Storage.Files.DataLake.Models;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class OneLakeFileStorage : IFileStorage
    {
        private readonly ILogger _logger;

        private string _blobContainerName = "uacloudpublisher";

        private DeviceCodeCredential _credential;

        private DataLakeServiceClient _dataLakeServiceClient;

        private DataLakeFileSystemClient _fileSystemClient;

        private object _lock = new object();

        private Task MyDeviceCodeCallback(DeviceCodeInfo info, CancellationToken cancellation)
        {
            _logger.LogInformation(info.Message);

            Settings.Instance.AuthenticationCode = info.UserCode;

            return Task.CompletedTask;
        }

        public OneLakeFileStorage(ILoggerFactory logger)
        {
            _logger = logger.CreateLogger("OneLakeFileStorage");

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STORAGE_CONTAINER_NAME")))
            {
                _blobContainerName = Environment.GetEnvironmentVariable("STORAGE_CONTAINER_NAME");
            }

            DeviceCodeCredentialOptions options = new()
            {
                DeviceCodeCallback = MyDeviceCodeCallback
            };
            _credential = new(options);
        }

        public Task<string> FindFileAsync(string path, string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                lock (_lock)
                {
                    VerifyOneLakeConnectivity();

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING")))
                    {
                        string[] connectionStringParts = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING").Split("/");

                        string dirName = connectionStringParts[4] + "/Files/" + _blobContainerName + path;
                        foreach (var fspath in _fileSystemClient.GetPaths(dirName))
                        {
                            if (fspath.Name.Contains(dirName + "/" + name))
                            {
                                return Task.FromResult(fspath.Name);
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }

        public Task<string> StoreFileAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path) || (content == null) || (content.Length == 0))
            {
                return null;
            }

            try
            {
                lock (_lock)
                {
                    VerifyOneLakeConnectivity();

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING")))
                    {
                        string[] connectionStringParts = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING").Split("/");

                        string filePath = connectionStringParts[4] + "/Files/" + _blobContainerName + path;
                        DataLakeFileClient client = _fileSystemClient.GetFileClient(filePath);
                        client.Upload(new MemoryStream(content), true);

                        return Task.FromResult(path);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }

        public Task<byte[]> LoadFileAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                lock (_lock)
                {
                    VerifyOneLakeConnectivity();

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING")))
                    {
                        string[] connectionStringParts = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING").Split("/");

                        DataLakeFileClient client = _fileSystemClient.GetFileClient(name);
                        Azure.Response<FileDownloadInfo> response = client.Read();
                        MemoryStream content = new();
                        response.Value.Content.CopyTo(content);
                        return Task.FromResult(content.ToArray());
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }

        private void VerifyOneLakeConnectivity()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING")))
            {
                string[] connectionStringParts = Environment.GetEnvironmentVariable("STORAGE_CONNECTION_STRING").Split("/");

                _dataLakeServiceClient = new DataLakeServiceClient(new Uri("https://" + connectionStringParts[2]), _credential);
                _fileSystemClient = _dataLakeServiceClient.GetFileSystemClient(connectionStringParts[3]);

                // make sure our directory exists
                string dirName = connectionStringParts[4] + "/Files/" + _blobContainerName;
                string authNotification = "Not required - OneLake access authenticated!";
                bool found = false;
                foreach (var fspath in _fileSystemClient.GetPaths(connectionStringParts[4] + "/Files"))
                {
                    if (fspath.Name == dirName)
                    {
                        found = true;
                        Settings.Instance.AuthenticationCode = authNotification;
                        Diagnostics.Singleton.Info.ConnectedToCloudStorage = true;
                    }
                }

                if (!found)
                {
                    _fileSystemClient.CreateDirectory(connectionStringParts[4] + "/Files/" + _blobContainerName);
                    Settings.Instance.AuthenticationCode = authNotification;
                    Diagnostics.Singleton.Info.ConnectedToCloudStorage = true;
                }
            }
        }
    }
}
