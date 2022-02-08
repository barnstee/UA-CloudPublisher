
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using UA.MQTT.Publisher;

    /// <summary>
    /// Class describing where clauses for an event filter.
    /// </summary>
    public class WhereClauseElementModel
    {
        /// <summary>
        /// Ctor of an object.
        /// </summary>
        public WhereClauseElementModel()
        {
            Operands = new List<WhereClauseOperandModel>();
        }

        /// <summary>
        /// Ctor of an object using the given operator and operands.
        /// </summary>
        public WhereClauseElementModel(string op, List<WhereClauseOperandModel> operands)
        {
            op.ResolveFilterOperator();
            Operands = operands;
        }

        /// <summary>
        /// The Operator of the WhereClauseElement.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string Operator;

        /// <summary>
        /// The Operands of the WhereClauseElement.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public List<WhereClauseOperandModel> Operands;
    }
}
