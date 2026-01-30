namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Hosting;
    using Serilog;
    using Serilog.Events;
    using System;
    using System.IO;

    public sealed class Program
    {
        public static IHost AppHost { get; private set; }

        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            Directory.CreateDirectory("logs");

            try
            {
                var host = Host.CreateDefaultBuilder(args)
                    .UseSerilog((ctx, services, lc) => lc
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.AspNetCore.Mvc", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
                        .WriteTo.Console()
                        .WriteTo.File("logs/uacloudpublisher-.log", rollingInterval: RollingInterval.Day))
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseStartup<Startup>();
                    })
                    .Build();

                AppHost = host;

                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
