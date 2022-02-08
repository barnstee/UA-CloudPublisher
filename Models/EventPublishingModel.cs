
namespace UA.MQTT.Publisher.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Describes the information of an event.
    /// </summary>
    public class EventPublishingModel : NodePublishingModel
    {
        public EventPublishingModel()
        {
            SelectClauses = new List<SelectClauseModel>();
            WhereClauses = new List<WhereClauseElementModel>();
        }

        /// <summary>
        /// The select clauses of the event.
        /// </summary>
        public List<SelectClauseModel> SelectClauses { get; }

        /// <summary>
        /// The where clauses of the event.
        /// </summary>
        public List<WhereClauseElementModel> WhereClauses { get; }
    }
}
