
namespace UA.MQTT.Publisher.Interfaces
{
    using UA.MQTT.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUAClient : IDisposable
    {
        Task PublishNodeAsync(EventPublishingModel nodeToPublish, CancellationToken cancellationToken = default);

        void UnpublishNode(EventPublishingModel nodeToUnpublish);

        void UnpublishAllNodes();

        Task<IEnumerable<ConfigurationFileEntryModel>> GetListofPublishedNodesAsync(CancellationToken cancellationToken = default);
    }
}