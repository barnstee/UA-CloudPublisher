
namespace UA.MQTT.Publisher.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    /// <summary>
    /// Model for a diagnostic info response.
    /// </summary>
    public class DiagnosticInfoMethodResponseModel
    {
        /// <summary>
        /// The array of diagnostics infos
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public IEnumerable<DiagnosticInfo> DiagnosticInfos;
    }
}
