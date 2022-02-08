
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Class describing a list of events and fields to publish.
    /// </summary>
    public class OpcEventOnEndpointModel
    {
        /// <summary>
        /// The event source of the event. This is a NodeId, which has the SubscribeToEvents bit set in the EventNotifier attribute.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string Id;

        /// <summary>
        /// A display name which can be added when publishing the event information.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName;

        /// <summary>
        /// The SelectClauses used to select the fields which should be published for an event.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public List<SelectClauseModel> SelectClauses;

        /// <summary>
        /// The WhereClause to specify which events are of interest.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public List<WhereClauseElementModel> WhereClauses;

        /// <summary>
        /// Ctor of an object.
        /// </summary>
        public OpcEventOnEndpointModel()
        {
            Id = string.Empty;
            DisplayName = string.Empty;
            SelectClauses = new List<SelectClauseModel>();
            WhereClauses = new List<WhereClauseElementModel>();
        }

        /// <summary>
        /// Ctor of an object using a configuration object.
        /// </summary>
        public OpcEventOnEndpointModel(EventPublishingModel eventConfiguration)
        {
            Id = eventConfiguration.ExpandedNodeId.ToString();
            DisplayName = eventConfiguration.DisplayName;
            SelectClauses = eventConfiguration.SelectClauses;
            WhereClauses = eventConfiguration.WhereClauses;
        }
    }
}
