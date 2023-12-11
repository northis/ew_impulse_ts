using Telegram.Bot;
using Telegram.Bot.Types;
using TrainBot.Commands;
using TrainBot.Commands.Common;
using TrainBot.FoldersLogic;

namespace TrainBot.Root
{
    public class MainConfigurator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MainConfigurator"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
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
        
        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; private set; }

        private CommandBase[] GetCommands()
        {
            return new CommandBase[]
            {
                ServiceProvider.GetService<HelpCommand>()!,
                ServiceProvider.GetService<LearnCommand>()!,
                ServiceProvider.GetService<StartCommand>()!
            };
        }

        public void ConfigureServices(IServiceCollection services)
        {
            BotSettingHolder botSettings = BotSettings;

            var botKey = botSettings.TelegramBotKey;
            if (string.IsNullOrEmpty(botKey))
                botKey = Environment.GetEnvironmentVariable("TG_TOKEN");

            if (botKey == null)
                Environment.Exit(-1);

            var tClient = new TelegramBotClient(botKey)
                { Timeout = botSettings.PollingTimeout };

            var fm = new FolderManager(botSettings);

            services.AddSingleton(_ => botSettings);
            services.AddSingleton(_ => tClient);
            services.AddSingleton(_ => fm);

            var commandManager = new CommandManager(GetCommands);
            services.AddSingleton<ICommandManager>(commandManager);
            services.AddTransient(_ => new HelpCommand(GetCommands));
            services.AddTransient(_ => new StartCommand(GetCommands));
            services.AddTransient(_ => new LearnCommand(fm));
            services.AddSingleton(_ => new QueryHandler(tClient, commandManager));

            if (botSettings.UseWebHook) services.AddMvc(options => options.EnableEndpointRouting = false);
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

            var botSettings = ServiceProvider.GetRequiredService<BotSettingHolder>();
            var botBotClient = ServiceProvider.GetRequiredService<TelegramBotClient>();

            if (botSettings.UseWebHook)
            {
                app.UseMvcWithDefaultRoute();
                botBotClient.SetWebhookAsync($"{botSettings.WebhookPublicUrl}/{botSettings.TelegramBotKey}/Webhook").Wait();
            }
            else
            {
                botBotClient.StartReceiving(UpdateHandler, PollingErrorHandler);
            }
        }

        private async Task PollingErrorHandler(
            ITelegramBotClient client, Exception ex, CancellationToken token)
        {
            QueryHandler queryHandler = ServiceProvider.GetRequiredService<QueryHandler>();
            await Task.Run(()=> queryHandler.OnReceiveGeneralError(ex), token);
        }

        private async Task UpdateHandler(
            ITelegramBotClient client, Update update, CancellationToken token)
        {
            QueryHandler queryHandler = ServiceProvider.GetRequiredService<QueryHandler>();
            if (update.Message != null)
                await queryHandler.OnMessage(update.Message);

            if (update.InlineQuery != null)
                await queryHandler.InlineQuery(update.InlineQuery);

            if (update.CallbackQuery != null)
                await queryHandler.CallbackQuery(update.CallbackQuery);
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
