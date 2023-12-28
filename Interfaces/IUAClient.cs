
namespace Opc.Ua.Cloud.Publisher.Interfaces
{
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

        void ExecuteCommand(string nodeIDString, string parentNodeIDString, string uaNamespace, string argument, string endpoint);
    }
}