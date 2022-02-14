
namespace UA.MQTT.Publisher
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Configuration;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

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

            string logFilePath = Configuration["LOG_FILE_PATH"];
            if (string.IsNullOrEmpty(logFilePath))
            {
                logFilePath = "./Logs/UA-MQTT-Publisher.log";
            }
            services.AddLogging(logging =>
            {
                logging.AddFile(logFilePath);
            });

            // add our singletons
            services.AddSingleton<IUAApplication, UAApplication>();
            services.AddSingleton<IUAClient, UAClient>();
            services.AddSingleton<IMQTTSubscriber, MQTTSubscriber>();
            services.AddSingleton<IPublishedNodesFileHandler, PublishedNodesFileHandler>();
            services.AddSingleton<Settings>();
            services.AddSingleton<IPeriodicDiagnosticsInfo, PeriodicDiagnosticsInfo>();
            services.AddSingleton<OpcSessionHelper>();
            services.AddSingleton<StatusHub>();

            // add our message processing engine
            services.AddSingleton<IMessageProcessingEngine, MessageProcessingEngine>();
            services.AddSingleton<IMessageSource, MonitoredItemNotification>();
            services.AddSingleton<IMessageEncoder, PubSubTelemetryEncoder>();
            services.AddSingleton<IMessagePublisher, MQTTPublisher>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env,
                              ILoggerFactory loggerFactory,
                              IUAApplication uaApp,
                              IMessageProcessingEngine engine,
                              IPeriodicDiagnosticsInfo diag,
                              Settings settings)
        {
            ILogger logger = loggerFactory.CreateLogger("Statup");

            settings.LoadRequiredSettingsFromEnvironment();
            settings.LoadOptionalSettingsFromEnvironment();

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
                endpoints.MapHub<StatusHub>("/statushub");
            });

            // create our app
            uaApp.CreateAsync().GetAwaiter().GetResult();

            // kick off the task to show periodic diagnostic info
            _ = Task.Run(() => diag.RunAsync());

            // run the telemetry engine
            _ = Task.Run(() => engine.Run());
        }
    }
}
