﻿
namespace Opc.Ua.Cloud.Publisher.Controllers
{
    using Azure;
    using Azure.AI.OpenAI;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.IO;
    using System.Text;
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

                OpenAIClient client = new OpenAIClient(
                    new Uri(Settings.Instance.AzureOpenAIAPIEndpoint),
                    new AzureKeyCredential(Settings.Instance.AzureOpenAIAPIKey)
                );

                string chatResponseSample = System.IO.File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "pac4200.jsonld"));

                Response<ChatCompletions> responseWithoutStream = client.GetChatCompletions(
                    Settings.Instance.AzureOpenAIDeploymentName,
                    new ChatCompletionsOptions()
                    {
                        Messages =
                        {
                            new ChatMessage(ChatRole.System, "You are a Web of Things Thing Description generator."),
                            new ChatMessage(ChatRole.User, @"Generate a Web of Things Thing Description for a Siemens Sentron PAC4200"),
                            new ChatMessage(ChatRole.Assistant, chatResponseSample),
                            new ChatMessage(ChatRole.User, "Generate a Web of Things Thing Description for a " + chatprompt)
                        },
                        Temperature = (float)0.2,
                        MaxTokens = 23420,
                        NucleusSamplingFactor = (float)0.95,
                        FrequencyPenalty = 0,
                        PresencePenalty = 0,
                    });

                ChatCompletions completions = responseWithoutStream.Value;
                string autogeneratedConfig = completions.Choices[0].Message.Content;

                return File(Encoding.UTF8.GetBytes(autogeneratedConfig), "APPLICATION/octet-stream", "asset_description_wot_td.jsonld");
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
                    content.Read(bytes, 0, (int)file.Length);
                    string payload = Encoding.UTF8.GetString(bytes);
                    JObject jsonObject = JObject.Parse(payload);
                    name = jsonObject["name"].ToString();
                }

                await _client.WoTConUpload(endpointUrl, username, password, bytes, name).ConfigureAwait(false);

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
