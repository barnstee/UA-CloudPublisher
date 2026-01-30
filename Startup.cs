
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Cloud.Publisher.Configuration;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Radzen;
    using System;
    using System.IO;
    using System.Threading.Tasks;

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
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(5);
            });

            services.AddControllersWithViews();

            services.AddRazorPages();

            services.AddServerSideBlazor();

            services.AddRadzenComponents();

            services.AddHttpClient();

            // add our singletons
            services.AddSingleton<IUAApplication, UAApplication>();
            services.AddSingleton<IUAClient, UAClient>();

            services.AddSingleton<KafkaClient>();
            services.AddSingleton<MQTTClient>();

            services.AddSingleton<Settings.BrokerResolver>(serviceProvider => key =>
            {
                switch (key)
                {
                    case "MQTT":
                        return serviceProvider.GetService<MQTTClient>();
                    case "Kafka":
                        return serviceProvider.GetService<KafkaClient>();
                    default:
                        return null;
                }
            });

            services.AddSingleton<IPublishedNodesFileHandler, PublishedNodesFileHandler>();
            services.AddSingleton<ICommandProcessor, CommandProcessor>();

            // add our message processing engine
            services.AddSingleton<IMessageProcessor, MessageProcessor>();
            services.AddSingleton<IMessageSource, MonitoredItemNotification>();
            services.AddSingleton<IMessageEncoder, PubSubTelemetryEncoder>();
            services.AddSingleton<IMessagePublisher, StoreForwardPublisher>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              ILogger<Startup> logger,
                              ILoggerFactory loggerFactory,
                              IUAApplication uaApp,
                              IMessageProcessor engine,
                              Settings.BrokerResolver brokerResolver,
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
                    pattern: "{controller=Diag}/{action=Index}/{id?}");
                endpoints.MapBlazorHub();
            });

            // do all further initialization on a background thread to load the webserver independently
            _ = Task.Run(() =>
            {
                // kick off the task to show periodic diagnostic info
                _ = Task.Run(() => Diagnostics.Singleton.RunAsync());

                // create our app
                uaApp.CreateAsync().GetAwaiter().GetResult();

                IBrokerClient broker;
                IBrokerClient altBroker;
                if (Settings.Instance.UseKafka)
                {
                    broker = brokerResolver("Kafka");
                }
                else
                {
                    broker = brokerResolver("MQTT");
                }

                // connect to broker
                broker.Connect();

                // check if we need a second broker
                if (Settings.Instance.UseAltBrokerForReceivingUAOverMQTT)
                {
                    altBroker = brokerResolver("MQTT");
                    altBroker.Connect(true);
                }

                // run the telemetry engine
                _ = Task.Run(() => engine.Run());

                // load our persistency file
                if (Settings.Instance.AutoLoadPersistedNodes)
                {
                    try
                    {
                        byte[] persistencyFile = File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "settings", "persistency.json"));
                        if (persistencyFile == null)
                        {
                            // no file persisted yet
                            throw new Exception("Persistency file not found.");
                        }
                        else
                        {
                            _ = Task.Run(() => publishedNodesFileHandler.ParseFile(persistencyFile));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to auto-load persisted nodes.");
                    }
                }
            });
        }
    }
}
