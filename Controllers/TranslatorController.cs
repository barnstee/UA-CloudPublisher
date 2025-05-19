
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Azure.AI.OpenAI;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Interfaces;
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

        public TranslatorController(IUAClient client, ILoggerFactory loggerFactory)
        {
            _client = client;
            _logger = loggerFactory.CreateLogger("TranslatorController");
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
                _logger.LogError(ex.Message);
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
                    string payload = Encoding.UTF8.GetString(bytes);
                    JObject jsonObject = JObject.Parse(payload);
                    name = jsonObject["name"].ToString();
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
                    await _client.WoTConUpload(endpointUrl, username, password, bytes, name).ConfigureAwait(false);
                }
                else
                {
                    throw new ArgumentException("Invalid file type specified!");
                }

                return View("Index", "UA Edge Translator configured successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return View("Index", ex.Message);
            }
        }
    }
}
