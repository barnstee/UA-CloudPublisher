
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAClient : IDisposable
    {
        Task<ReferenceDescriptionCollection> Browse(string endpointUrl, string username, string password, BrowseDescription nodeToBrowse, bool throwOnError);

        Task<List<UANodeInformation>> BrowseVariableNodesResursivelyAsync(string endpointUrl, string username, string password, NodeId nodeId);

        string ReadNode(string endpointUrl, string username, string password, ref string nodeId);

        Task<string> PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default);

        void UnpublishNode(NodePublishingModel nodeToUnpublish);

        void UnpublishAllNodes(bool updatePersistencyFile = true);

        IEnumerable<PublishNodesInterfaceModel> GetPublishedNodes();

        Task GDSServerPush(string endpointURL, string adminUsername, string adminPassword);

        Task WoTConUpload(string endpoint, string username, string password, byte[] bytes, string assetName);
        
        void Disconnect(string endpointUrl);
    }
}