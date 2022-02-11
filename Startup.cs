
namespace UA.MQTT.Publisher
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Configuration;
    using UA.MQTT.Publisher.Controllers;
    using UA.MQTT.Publisher.Interfaces;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSession(options => {
                options.IdleTimeout = TimeSpan.FromMinutes(5);
            });

            services.AddControllersWithViews();

            services.AddSignalR();

            services.AddLogging(logging =>
            {
                logging.AddFile("Logs/UA-MQTT-Publisher.log");
            });

            // add our singletons
            services.AddSingleton<IUAApplication, UAApplication>();
            services.AddSingleton<IUAClient, UAClient>();
            services.AddSingleton<IMQTTSubscriber, MQTTSubscriber>();
            services.AddSingleton<IPublishedNodesFileHandler, PublishedNodesFileHandler>();
            services.AddSingleton<ISettingsConfiguration, SettingsConfiguration>();
            services.AddSingleton<IPeriodicDiagnosticsInfo, PeriodicDiagnosticsInfo>();
            services.AddSingleton<OpcSessionHelper>();

            // add our message processing engine
            services.AddSingleton<IMessageProcessingEngine, MessageProcessingEngine>();
            services.AddSingleton<IMessageTrigger, MonitoredItemNotificationTrigger>();
            services.AddSingleton<IMessageEncoder, PubSubTelemetryEncoder>();
            services.AddSingleton<IMessageSink, MQTTMessageSink>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
                                    IWebHostEnvironment env,
                                    ILoggerFactory loggerFactory,
                                    IUAApplication uaApp,
                                    IMessageProcessingEngine engine,
                                    IPeriodicDiagnosticsInfo diag,
                                    ISettingsConfiguration settingsConfiguration,
                                    IPublishedNodesFileHandler publishedNodesFileHandler)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Browser/Error");

                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseSession();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Browser}/{action=Index}/{id?}");
            });

            // create our app
            uaApp.CreateAsync().GetAwaiter().GetResult();

            // kick off the task to show periodic diagnostic info
            //_ = Task.Run(() => diag.RunAsync());

            // run the telemetry engine
            _ = Task.Run(() => engine.Run());

            // load publishednodes.json file, if available
            // publishednodes.json wins over pn.json
            string publishedNodesJSONFilePath1 = settingsConfiguration.AppDataPath + "publishednodes.json";
            string publishedNodesJSONFilePath2 = settingsConfiguration.AppDataPath + "pn.json";
            string pathToUse;
            if (File.Exists(publishedNodesJSONFilePath1))
            {
                pathToUse = publishedNodesJSONFilePath1;
            }
            else
            {
                pathToUse = publishedNodesJSONFilePath2;
            }

            ILogger logger = loggerFactory.CreateLogger("Statup");
            if (File.Exists(pathToUse))
            {
                logger.LogInformation($"Loading published nodes JSON file from {pathToUse}...");
                X509Certificate2 certWithPrivateKey = uaApp.GetAppConfig().SecurityConfiguration.ApplicationCertificate.LoadPrivateKey(null).GetAwaiter().GetResult();
                if (!publishedNodesFileHandler.ParseFile(pathToUse, certWithPrivateKey))
                {
                    logger.LogInformation("Could not load and parse published nodes JSON file!");
                }
                else
                {
                    logger.LogInformation("Published nodes JSON file parsed successfully.");
                }
            }
            else
            {
                logger.LogInformation($"Published nodes JSON file not found in {pathToUse}.");
            }
        }
    }
}
