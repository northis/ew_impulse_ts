namespace ImpulseFinder.Bot
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(hostConfig =>
                {
                    hostConfig.SetBasePath(Directory.GetCurrentDirectory());
                    hostConfig.AddJsonFile("hostsettings.json", optional: true);
                    hostConfig.AddEnvironmentVariables(prefix: "PREFIX_");
                    hostConfig.AddCommandLine(args);
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<SignalWorker>();
                });

            hostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<MyThingy>();
            });

            var host = hostBuilder.Build();
            await host.RunAsync();
        }
    }
}