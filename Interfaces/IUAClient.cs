
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAClient : IDisposable
    {
        Task PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default);

        void UnpublishNode(NodePublishingModel nodeToUnpublish);

        void UnpublishAllNodes();

        IEnumerable<PublishNodesInterfaceModel> GetPublishedNodes();
    }
}