using Telegram.Bot;
using TrainBot.Commands;
using TrainBot.Commands.Common;

namespace TrainBot.Root
{
    public class MainConfigurator
    {
        public MainConfigurator(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        
        private BotSettingHolder? m_BotSettings;

        public BotSettingHolder BotSettings
        {
            get
            {
                if (m_BotSettings != null) return m_BotSettings;

                m_BotSettings = new BotSettingHolder(Configuration);
                return m_BotSettings;
            }
        }

        public string CurrentDir => AppDomain.CurrentDomain.BaseDirectory;
        public uint MaxUploadFileSize => 8192;

        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; private set; }

        private CommandBase[] GetCommands()
        {
            return new CommandBase[]
            {
                ServiceProvider.GetService<AboutCommand>()!,
                ServiceProvider.GetService<HelpCommand>()!,
                ServiceProvider.GetService<StartCommand>()!
            };
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var botSettings = BotSettings;
            var tClient = new TelegramBotClient(botSettings.TelegramBotKey)
                { Timeout = botSettings.PollingTimeout };
            services.AddSingleton(bS => botSettings);
            services.AddSingleton(cl => tClient);

            var commandManager = new CommandManager(GetCommands);
            services.AddSingleton<ICommandManager>(commandManager);
            services.AddMvc(options => options.EnableEndpointRouting = false);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime applicationLifetime)
        {
            ServiceProvider = app.ApplicationServices;

            applicationLifetime.ApplicationStopping.Register(OnShutdown);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseMvcWithDefaultRoute();

            var botSettings = ServiceProvider.GetRequiredService<BotSettingHolder>();
            var botBotClient = ServiceProvider.GetRequiredService<TelegramBotClient>();
            botBotClient.SetWebhookAsync($"{botSettings.WebhookPublicUrl}/{botSettings.TelegramBotKey}/Webhook").Wait();
        }

        private void OnShutdown()
        {
            var botClient = ServiceProvider.GetRequiredService<TelegramBotClient>();
            botClient.DeleteWebhookAsync().Wait();
        }

        public static DateTime GetDateTime()
        {
            return DateTime.Now;
        }
    }
}
