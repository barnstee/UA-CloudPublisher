
namespace Opc.Ua.Cloud.Publisher.Models
{
    using Newtonsoft.Json;

    public class FilterModel
    {
        [JsonProperty(Required = Required.Always)]
        public string OfType { get; set; }
    }
}
