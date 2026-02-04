
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class WoTThingDescriptionParser
    {
        private readonly ILogger _logger;

        public WoTThingDescriptionParser(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("WoTThingDescriptionParser");
        }

        public List<PublishNodesInterfaceModel> ParseWoTThingDescription(byte[] wotBytes, string edgeTranslatorEndpointUrl)
        {
            try
            {
                string wotContent = Encoding.UTF8.GetString(wotBytes).Trim('\uFEFF'); // strip BOM, if present
                JObject wotTD = JObject.Parse(wotContent);

                // Extract properties from the WoT Thing Description
                JToken properties = wotTD["properties"];
                if (properties == null || !properties.HasValues)
                {
                    _logger.LogWarning("No properties found in WoT Thing Description");
                    return new List<PublishNodesInterfaceModel>();
                }

                // Create a list to hold the OPC nodes
                List<VariableModel> opcNodes = new List<VariableModel>();

                // Parse each property
                foreach (JProperty property in properties.Children<JProperty>())
                {
                    string propertyName = property.Name;
                    JObject propertyValue = (JObject)property.Value;

                    // Extract forms which contain the OPC UA type information
                    JArray forms = (JArray)propertyValue["forms"];
                    if (forms != null && forms.Count > 0)
                    {
                        foreach (JObject form in forms)
                        {
                            // Extract the OPC UA type mapping
                            string opcuaType = form["opcua:type"]?.ToString();
                            if (!string.IsNullOrEmpty(opcuaType))
                            {
                                // Extract polling time if available (use as publishing interval)
                                int pollingTime = form["modbus:pollingTime"]?.Value<int>() ?? 2000;

                                // Create the OPC UA node ID from the opcua:type
                                // The format is: nsu=namespace;i=identifier
                                string nodeId = opcuaType;

                                // Create a variable model
                                VariableModel variable = new VariableModel
                                {
                                    Id = nodeId,
                                    OpcPublishingInterval = pollingTime,
                                    OpcSamplingInterval = pollingTime,
                                    HeartbeatInterval = 0,
                                    SkipFirst = false
                                };

                                opcNodes.Add(variable);
                                _logger.LogInformation($"Added property '{propertyName}' with Node ID '{nodeId}' and polling interval {pollingTime}ms");
                            }
                            else
                            {
                                _logger.LogWarning($"Property '{propertyName}' does not have opcua:type mapping");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Property '{propertyName}' does not have forms");
                    }
                }

                // Create the PublishNodesInterfaceModel entry
                List<PublishNodesInterfaceModel> publishNodesList = new List<PublishNodesInterfaceModel>();

                if (opcNodes.Count > 0)
                {
                    PublishNodesInterfaceModel publishNodesEntry = new PublishNodesInterfaceModel
                    {
                        EndpointUrl = edgeTranslatorEndpointUrl,
                        OpcNodes = opcNodes,
                        OpcAuthenticationMode = UserAuthModeEnum.Anonymous
                    };

                    publishNodesList.Add(publishNodesEntry);
                    _logger.LogInformation($"Created PublishedNodes configuration with {opcNodes.Count} nodes for endpoint '{edgeTranslatorEndpointUrl}'");
                }
                else
                {
                    _logger.LogWarning("No valid OPC UA nodes found in WoT Thing Description");
                }

                return publishNodesList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse WoT Thing Description");
                throw;
            }
        }
    }
}
