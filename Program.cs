
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Hosting;

    public sealed class Program
    {
        public static IHost AppHost { get; private set; }

        public static void Main(string[] args)
        {
            AppHost = CreateHostBuilder(args).Build();
            AppHost.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
           Host.CreateDefaultBuilder(args)
               .ConfigureWebHostDefaults(webBuilder =>
               {
                   webBuilder.UseStartup<Startup>();
               });
    }
}

