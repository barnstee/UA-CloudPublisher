
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAClient : IAsyncDisposable
    {
        Task<ReferenceDescriptionCollection> BrowseAsync(string endpointUrl, string username, string password, BrowseDescription nodeToBrowse, bool throwOnError);

        Task<List<UANodeInformation>> BrowseVariableNodesResursivelyAsync(string endpointUrl, string username, string password, NodeId nodeId);

        Task<string> ReadNodeAsync(string endpointUrl, string username, string password, string nodeId);

        Task<string> PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default);

        Task UnpublishNodeAsync(NodePublishingModel nodeToUnpublish);

        Task UnpublishAllNodesAsync(bool updatePersistencyFile = true);

        IEnumerable<PublishNodesInterfaceModel> GetPublishedNodes();

        Task GDSServerPushAsync(string endpointURL, string adminUsername, string adminPassword);

        Task WoTConUploadAsync(string endpoint, string username, string password, byte[] bytes, string assetName);

        Task UANodesetUploadAsync(string endpoint, string username, string password, byte[] bytes);

        Task DisconnectAsync(string endpointUrl);
    }
}