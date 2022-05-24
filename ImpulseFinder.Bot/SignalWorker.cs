using ImpulseFinder.Bot.OpenApi;
using OpenAPI.Net.Auth;

namespace ImpulseFinder.Bot
{
    internal class SignalWorker: IHostedService, IDisposable
    {
        private readonly ILogger<SignalWorker> m_Logger;
        private readonly IOpenApiService m_OpenApiService;

        public SignalWorker(
            ILogger<SignalWorker> logger, IOpenApiService openApiService)
        {
            m_Logger = logger;
            m_OpenApiService = openApiService;
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
