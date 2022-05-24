namespace ImpulseFinder.Bot.Root
{
    public class BotSettingHolder
    {
        public BotSettingHolder(IConfiguration configuration)
        {
            TelegramBotKey = configuration["TelegramBotKey"];
            PollingTimeout = TimeSpan.Parse(configuration["PollingTimeout"]);
            Singleton = this;
        }

        #region Properties
        public string TelegramBotKey { get; }
        public TimeSpan PollingTimeout { get; }

        public static BotSettingHolder? Singleton { get; private set; }
        #endregion
    }
}