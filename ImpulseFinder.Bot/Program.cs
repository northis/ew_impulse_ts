namespace ImpulseFinder.Bot
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddHostedService<SignalWorker>();
                })
                .Build();
            await host.RunAsync();
        }
    }
}