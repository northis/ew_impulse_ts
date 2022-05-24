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
        }
        
        private string _releaseNotesInfo;
        private string _aboutInfo;
        private string _preInstalledFolder;
        private BotSettingHolder _botSettings;

        public BotSettingHolder BotSettings
        {
            get
            {
                if (_botSettings != null) return _botSettings;

                _botSettings = new BotSettingHolder(Configuration);
                return _botSettings;
            }
        }

        public string ReleaseNotesInfo
        {
            get
            {
                if (_releaseNotesInfo != null) return _releaseNotesInfo;

                var path = Path.Combine(CurrentDir, "ReleaseNotes.txt");
                _releaseNotesInfo = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

                return _releaseNotesInfo;
            }
        }
        public string PreInstalledFolder
        {
            get
            {
                if (_preInstalledFolder != null) return _preInstalledFolder;

                _preInstalledFolder = Path.Combine(CurrentDir, "Hsk");
                return _preInstalledFolder;
            }
        }

        public string AboutInfo
        {
            get
            {
                if (_aboutInfo != null)
                {
                    return _aboutInfo;
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

                    _aboutInfo = sb.ToString();
                }
                else
                {
                    _aboutInfo = string.Empty;
                }

                return _aboutInfo;
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
                ServiceProvider.GetService<AboutCommand>() ?? throw new InvalidOperationException(),
                ServiceProvider.GetService<HelpCommand>() ?? throw new InvalidOperationException()
            };
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var botSettings = BotSettings;
            var tClient = new TelegramBotClient(botSettings.TelegramBotKey)
                { Timeout = botSettings.PollingTimeout };

            //var commandManager = new CommandManager(GetCommands, GetHiddenCommands,
            //    new Dictionary<string, ECommands> {{botSettings.ServiceCommandPassword, ECommands.Admin}});
            
            services.AddSingleton(bS => botSettings);
            services.AddSingleton(cl => tClient);
            //services.AddSingleton<ICommandManager>(commandManager);

            services.AddTransient(a => new AboutCommand(ReleaseNotesInfo, AboutInfo));
            services.AddTransient(a => new HelpCommand(GetCommands));
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
