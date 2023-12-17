namespace TrainBot.Root
{
    public class BotSettingHolder
    {
        public BotSettingHolder(IConfiguration configuration)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            TelegramBotKey = configuration["TelegramBotKey"]!;
            UseWebHook = bool.Parse(configuration["UseWebHook"]!);
            InputFolder = Path.Join(baseDir, configuration["InputFolder"]);
            MlFolder = Path.Join(baseDir, configuration["MLFolder"]);
            PositiveFolder = Path.Join(baseDir, configuration["PositiveFolder"]);
            PositiveDiagonalFolder = Path.Join(baseDir, configuration["PositiveDiagonalFolder"]);
            NegativeFolder = Path.Join(baseDir, configuration["NegativeFolder"]);
            BrokenFolder = Path.Join(baseDir, configuration["BrokenFolder"]);
            PollingTimeout = TimeSpan.Parse(configuration["PollingTimeout"]!);
            WebhookUrl = configuration["WebhookUrl"]!;
            WebhookPublicUrl = configuration["WebhookPublicUrl"]!;
            ApiPublicUrl = configuration["ApiPublicUrl"]!;
            LocalPort = int.Parse(configuration["LocalPort"]!);
            UserId = long.Parse(configuration["UserId"]!);
            AdminUserId = long.Parse(configuration["AdminUserId"]!);
            ServerUserId = long.Parse(configuration["ServerUserId"]!);
            Singleton = this;

            Directory.CreateDirectory(InputFolder);
            Directory.CreateDirectory(PositiveFolder);
            Directory.CreateDirectory(NegativeFolder);
            Directory.CreateDirectory(PositiveDiagonalFolder);
            Directory.CreateDirectory(BrokenFolder);
            Directory.CreateDirectory(MlFolder);

            MlModelPath = Path.Join(MlFolder, "model.zip");
        }

        #region Properties
        public string InputFolder { get; }
        public string MlFolder { get; }
        public string MlModelPath { get; }
        public string PositiveFolder { get; }
        public string PositiveDiagonalFolder { get; }
        public string NegativeFolder { get; }
        public string BrokenFolder { get; }
        public string TelegramBotKey { get; }
        public TimeSpan PollingTimeout { get; }
        public string WebhookUrl{ get; }
        public string WebhookPublicUrl { get; }
        public int LocalPort { get; }
        public string ApiPublicUrl { get; }
        public long UserId { get; }
        public long AdminUserId { get; }
        public long ServerUserId { get; }
        public bool UseWebHook { get; }

        public static BotSettingHolder Singleton { get; private set; }
        #endregion
    }
}