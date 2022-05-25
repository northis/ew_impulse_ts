using System.Text;
using ImpulseFinder.Bot.Commands;
using Newtonsoft.Json.Linq;
using Telegram.Bot;

namespace ImpulseFinder.Bot.Root
{
    public class MainConfigurator
    {
        public MainConfigurator(IConfiguration configuration)
        {
            Configuration = configuration;
            BotSettings = new BotSettingHolder(configuration);
        }
        
        private string m_ReleaseNotesInfo;
        private string m_AboutInfo;

        public BotSettingHolder BotSettings { get; init;}

        public string ReleaseNotesInfo
        {
            get
            {
                if (m_ReleaseNotesInfo != null) return m_ReleaseNotesInfo;

                var path = Path.Combine(CurrentDir, "ReleaseNotes.txt");
                m_ReleaseNotesInfo = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

                return m_ReleaseNotesInfo;
            }
        }

        public string AboutInfo
        {
            get
            {
                if (m_AboutInfo != null)
                {
                    return m_AboutInfo;
                }

                var path = Path.Combine(CurrentDir, "package.json");
                if (File.Exists(path))
                {
                    dynamic json = JObject.Parse(File.ReadAllText(path));

                    var sb = new StringBuilder();
                    sb.AppendLine($"{json.description} ver. {json.version}");
                    sb.AppendLine($"Author: {json.author}");
                    sb.AppendLine("Contact me: @soft_udder");
                    sb.AppendLine($"Github: {json.homepage}");

                    m_AboutInfo = sb.ToString();
                }
                else
                {
                    m_AboutInfo = string.Empty;
                }

                return m_AboutInfo;
            }
        }

        public string CurrentDir => AppDomain.CurrentDomain.BaseDirectory;

        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; private set; }

        private CommandBase[] GetCommands()
        {
            return new CommandBase[]
            {
                ServiceProvider.GetService<AboutCommand>() ?? throw new InvalidOperationException(),
                ServiceProvider.GetService<HelpCommand>() ?? throw new InvalidOperationException()
            };
        }

        public void ConfigureServices(IServiceCollection services)
        {
            BotSettingHolder botSettings = BotSettings;
            var tClient = new TelegramBotClient(botSettings.TelegramBotKey)
                { Timeout = botSettings.PollingTimeout };

            var commandManager = new CommandManager(
                GetCommands,
                new Dictionary<string, ECommands>());

            services.AddSingleton(_ => botSettings);
            services.AddSingleton(_ => tClient);
            services.AddSingleton<ICommandManager>(commandManager);

            services.AddTransient(_ => new AboutCommand(ReleaseNotesInfo, AboutInfo));
            services.AddTransient(_ => new HelpCommand(GetCommands));
        }

        public void Configure(IHostBuilder app, IHostingEnvironment env, IApplicationLifetime applicationLifetime)
        {
           // ServiceProvider = app.UseServiceProviderFactory();

            applicationLifetime.ApplicationStopping.Register(OnShutdown);
            //if (env.IsDevelopment())
            //{
            //    app.UseDeveloperExceptionPage();
            //}

            //app.UseDefaultFiles();
            //app.UseStaticFiles();
            //app.UseMvcWithDefaultRoute();

            //var botSettings = ServiceProvider.GetRequiredService<BotSettingHolder>();
            //var botBotClient = ServiceProvider.GetRequiredService<TelegramBotClient>();
            //botBotClient.SetWebhookAsync($"{botSettings.WebhookPublicUrl}/{botSettings.TelegramBotKey}/Webhook").Wait();
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
