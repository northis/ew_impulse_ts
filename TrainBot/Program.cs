using System.Net;
using Microsoft.AspNetCore;
using TrainBot.Root;

namespace TrainBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var webHostBuilder = WebHost.CreateDefaultBuilder(args)
                .UseStartup<MainConfigurator>();

            webHostBuilder.UseKestrel(options =>
            {
                var botSettings = options.ApplicationServices.GetRequiredService<BotSettingHolder>();
                options.Listen(IPAddress.Any, botSettings.LocalPort);
            });

            var webHost = webHostBuilder.Build();
            webHost.Run();
            WebHostSingleton = webHost;
        }

        public static IWebHost WebHostSingleton { get; private set; } = null!;
    }
}