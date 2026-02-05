
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class WoTTDParser
    {
        private readonly ILogger _logger;

        public WoTTDParser(ILogger logger)
        {
            _logger = logger;
        }

        public List<VariableModel> ParseWoTThingDescription(byte[] wotBytes)
        {
            Dictionary<string, VariableModel> opcNodes = new();

            try
            {
                string wotContent = Encoding.UTF8.GetString(wotBytes).Trim('\uFEFF'); // strip BOM, if present
                JObject wotTD = JObject.Parse(wotContent);

                // Extract properties from the WoT Thing Description
                JToken properties = wotTD["properties"];
                if (properties == null || !properties.HasValues)
                {
                    _logger.LogWarning("No properties found in WoT Thing Description");
                    return opcNodes.Values.ToList();
                }

                foreach (JProperty property in properties.Children<JProperty>())
                {
                    string propertyName = property.Name;
                    JObject propertyValue = (JObject)property.Value;
                    JValue nodeId = (JValue)propertyValue["uav:mapToNodeId"];

                    string id;
                    if (nodeId != null)
                    {
                        id = "nsu=http://opcfoundation.org/UA/" + ((JValue)wotTD["name"]).Value + "/;" + nodeId.Value.ToString();
                    }
                    else
                    {
                        id = "nsu=http://opcfoundation.org/UA/" + ((JValue)wotTD["name"]).Value + "/;s=" + propertyName;
                    }

                    VariableModel variable = new()
                    {
                        Id = id,
                        OpcPublishingInterval = 1000,
                        OpcSamplingInterval = 1000,
                        HeartbeatInterval = 0,
                        SkipFirst = false
                    };

                    if (!opcNodes.ContainsKey(id))
                    {
                        opcNodes.Add(id, variable);
                        _logger.LogInformation($"Added property '{propertyName}' with Node ID '{nodeId}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse WoT Thing Description");
            }

            return opcNodes.Values.ToList();
        }
    }
}
