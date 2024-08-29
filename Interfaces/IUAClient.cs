
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAClient : IDisposable
    {
        Task<string> PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default);

        void UnpublishNode(NodePublishingModel nodeToUnpublish);

        void UnpublishAllNodes(bool updatePersistencyFile = true);

        IEnumerable<PublishNodesInterfaceModel> GetPublishedNodes();

        Task GDSServerPush(string endpointURL, string adminUsername, string adminPassword);

        Task<List<UANodeInformation>> BrowseVariableNodesResursivelyAsync(Session session, NodeId nodeId);

        Task WoTConUpload(string endpoint, string username, string password, byte[] bytes, string assetName);
    }
}