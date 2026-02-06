
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    public class WoTTDParser
    {
        private readonly ILogger _logger;
        private static readonly Regex TemplateRegex = new Regex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

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

        public bool IsThingModel(string wotContent)
        {
            try
            {
                JObject wotObject = JObject.Parse(wotContent);
                
                // Check if @type contains "tm:ThingModel"
                JToken typeToken = wotObject["@type"];
                if (typeToken != null)
                {
                    if (typeToken is JArray typeArray)
                    {
                        foreach (var type in typeArray)
                        {
                            if (type.ToString().Contains("ThingModel", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    else if (typeToken.ToString().Contains("ThingModel", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Check if content contains template placeholders {{}}
                return wotContent.Contains("{{") && wotContent.Contains("}}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to determine if WoT file is a Thing Model");
                return false;
            }
        }

        public List<string> ExtractTemplates(string wotContent)
        {
            var templates = new HashSet<string>();
            
            try
            {
                // Regular expression to find {{template}} patterns
                MatchCollection matches = TemplateRegex.Matches(wotContent);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        templates.Add(match.Groups[1].Value.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract templates from WoT content");
            }

            return templates.ToList();
        }

        public string ReplaceTemplates(string wotContent, Dictionary<string, string> templateValues)
        {
            string result = wotContent;

            try
            {
                foreach (var kvp in templateValues)
                {
                    string placeholder = "{{" + kvp.Key + "}}";
                    result = result.Replace(placeholder, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replace templates in WoT content");
            }

            return result;
        }
    }
}
