using OpenAPI.Net.Auth;

namespace ImpulseFinder.Bot
{
    internal class SignalWorker: IHostedService, IDisposable
    {
        private readonly ILogger<SignalWorker> m_Logger;

        public SignalWorker(ILogger<SignalWorker> logger)
        {
            m_Logger = logger;
            
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            m_Logger.LogInformation("StartAsync");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            m_Logger.LogInformation("StopAsync");
        }

        public void Dispose()
        {
            m_Logger.LogInformation("Disposed");
        }
    }
}
