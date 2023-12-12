using System.Net;
using Microsoft.AspNetCore;
using TradeKit.Core;
using TrainBot.Root;

namespace TrainBot
{
    public class Program
    {
        private static void Write(string message)
        {
            Console.WriteLine(message);
        }

        public static void Main(string[] args)
        {
            Logger.SetWrite(Write);
            var webHostBuilder = WebHost.CreateDefaultBuilder(args)
                .UseStartup<MainConfigurator>();

            webHostBuilder.UseKestrel(options =>
            {
                var botSettings = options.ApplicationServices.GetRequiredService<BotSettingHolder>();
                options.Listen(IPAddress.Any, botSettings.LocalPort);
            });

            var webHost = webHostBuilder.Build();
            
            WebHostSingleton = webHost;
            Logger.Write($"{nameof(Main)} started");
            webHost.Run();
        }

        public static IWebHost WebHostSingleton { get; private set; } = null!;
    }
}