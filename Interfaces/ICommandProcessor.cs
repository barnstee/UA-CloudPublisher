
namespace UA.MQTT.Publisher.Interfaces
{
    public interface ICommandProcessor
    {
        byte[] PublishNodes(string payload);

        byte[] UnpublishNodes(string payload);

        byte[] UnpublishAllNodes();

        byte[] GetPublishedNodes();

        byte[] GetInfo();
    }
}
