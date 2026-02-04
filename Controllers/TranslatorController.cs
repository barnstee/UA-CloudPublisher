
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Azure.AI.OpenAI;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using OpenAI.Chat;
    using System;
    using System.ClientModel;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class TranslatorController : Controller
    {
        private readonly IUAClient _client;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        public TranslatorController(IUAClient client, ILoggerFactory loggerFactory)
        {
            _client = client;
            _logger = loggerFactory.CreateLogger("TranslatorController");
            _loggerFactory = loggerFactory;
        }

        public IActionResult Index()
        {
            return View("Index", string.Empty);
        }

        [HttpPost]
        public IActionResult Generate(string chatprompt)
        {
            try
            {
                if (string.IsNullOrEmpty(chatprompt))
                {
                    throw new ArgumentException("The chat prompt is invalid!");
                }

                AzureOpenAIClient client = new(
                    new Uri(Settings.Instance.AzureOpenAIAPIEndpoint),
                    new ApiKeyCredential(Settings.Instance.AzureOpenAIAPIKey)
                );

                ChatClient chatClient = client.GetChatClient(Settings.Instance.AzureOpenAIDeploymentName);

                ChatCompletion completion = chatClient.CompleteChat(
                    new List<ChatMessage>() {
                        new SystemChatMessage("You are a Web of Things Thing Description generator."),
                        new UserChatMessage("Generate a Web of Things Thing Description for a Siemens Sentron PAC4200"),
                        new AssistantChatMessage(System.IO.File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "pac4200.jsonld"))),
                        new UserChatMessage("Generate a Web of Things Thing Description for a " + chatprompt)
                    },
                    new ChatCompletionOptions()
                    {
                        Temperature = (float)0.2,
                        MaxOutputTokenCount = 23420,
                        FrequencyPenalty = 0,
                        PresencePenalty = 0
                    }
                );

                return File(Encoding.UTF8.GetBytes(completion.Content[0].Text), "APPLICATION/octet-stream", "asset_description_wot_td.jsonld");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate asset description");
                return View("Index", ex.Message);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Load(IFormFile file, string endpointUrl, string username, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(endpointUrl))
                {
                    throw new ArgumentException("The endpoint URL specified is invalid!");
                }

                if (file == null)
                {
                    throw new ArgumentException("No file specified!");
                }

                if (file.Length == 0)
                {
                    throw new ArgumentException("Invalid file specified!");
                }

                string name = "asset";
                byte[] bytes = new byte[file.Length];
                using (Stream content = file.OpenReadStream())
                {
                    content.ReadExactly(bytes, 0, (int)file.Length);

                    if (file.FileName.EndsWith(".jsonld", StringComparison.OrdinalIgnoreCase))
                    {
                        JObject jsonObject = JObject.Parse(Encoding.UTF8.GetString(bytes).Trim('\uFEFF')); // strip BOM, if present
                        name = jsonObject["name"].ToString();
                    }
                }

                if (Settings.Instance.PushCertsBeforePublishing)
                {
                    try
                    {
                        await _client.GDSServerPush(endpointUrl, username, password).ConfigureAwait(false);

                        // after the cert push, give the server 5s time to become available again before trying to pudh the WoT file to it
                        Thread.Sleep(5000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Cannot push new certificates to server " + endpointUrl + "due to " + ex.Message);
                    }
                }

                if (file.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    await _client.UANodesetUpload(endpointUrl, username, password, bytes).ConfigureAwait(false);
                }
                else if (file.FileName.EndsWith(".jsonld", StringComparison.OrdinalIgnoreCase))
                {
                    // Step 1: Parse the WoT Thing Description and generate PublishedNodes configuration
                    WoTThingDescriptionParser parser = new WoTThingDescriptionParser(_loggerFactory);
                    List<PublishNodesInterfaceModel> publishNodesList = parser.ParseWoTThingDescription(bytes, endpointUrl);

                    // Step 2: Save the generated PublishedNodes.json file
                    if (publishNodesList.Count > 0)
                    {
                        string publishedNodesDir = Path.Combine(Directory.GetCurrentDirectory(), "PublishedNodes");
                        if (!Directory.Exists(publishedNodesDir))
                        {
                            Directory.CreateDirectory(publishedNodesDir);
                        }

                        string publishedNodesPath = Path.Combine(publishedNodesDir, $"publishednodes_{name}.json");
                        string publishedNodesJson = JsonConvert.SerializeObject(publishNodesList, Formatting.Indented);
                        System.IO.File.WriteAllText(publishedNodesPath, publishedNodesJson);
                        _logger.LogInformation($"Generated PublishedNodes file: {publishedNodesPath} with {publishNodesList[0].OpcNodes?.Count ?? 0} nodes");
                    }

                    // Step 3: Send the WoT file to Edge Translator
                    await _client.WoTConUpload(endpointUrl, username, password, bytes, name).ConfigureAwait(false);

                    // Step 4: Publish the nodes from the generated configuration
                    if (publishNodesList.Count > 0)
                    {
                        foreach (PublishNodesInterfaceModel publishEntry in publishNodesList)
                        {
                            // Set authentication credentials
                            publishEntry.UserName = username;
                            publishEntry.Password = password;
                            publishEntry.OpcAuthenticationMode = string.IsNullOrEmpty(username) ? UserAuthModeEnum.Anonymous : UserAuthModeEnum.UsernamePassword;

                            if (publishEntry.OpcNodes != null)
                            {
                                foreach (VariableModel opcNode in publishEntry.OpcNodes)
                                {
                                    NodePublishingModel publishingInfo = new NodePublishingModel()
                                    {
                                        ExpandedNodeId = ExpandedNodeId.Parse(opcNode.Id),
                                        EndpointUrl = new Uri(publishEntry.EndpointUrl).ToString(),
                                        OpcPublishingInterval = opcNode.OpcPublishingInterval,
                                        OpcSamplingInterval = opcNode.OpcSamplingInterval,
                                        HeartbeatInterval = opcNode.HeartbeatInterval,
                                        SkipFirst = opcNode.SkipFirst,
                                        OpcAuthenticationMode = publishEntry.OpcAuthenticationMode,
                                        Username = publishEntry.UserName,
                                        Password = publishEntry.Password
                                    };

                                    try
                                    {
                                        await _client.PublishNodeAsync(publishingInfo).ConfigureAwait(false);
                                        _logger.LogInformation($"Published node {publishingInfo.ExpandedNodeId} successfully");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError($"Cannot publish node {publishingInfo.ExpandedNodeId}: {ex.Message}");
                                    }
                                }
                            }
                        }

                        int totalNodes = publishNodesList[0].OpcNodes?.Count ?? 0;
                        return View("Index", $"UA Edge Translator configured successfully! Published {totalNodes} nodes from WoT Thing Description.");
                    }
                }
                else
                {
                    throw new ArgumentException("Invalid file type specified!");
                }

                return View("Index", "UA Edge Translator configured successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file");
                return View("Index", ex.Message);
            }
        }
    }
}
