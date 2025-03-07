
using TradeKit.Core.Telegram;

var tl = new TelegramEventListener(Environment.GetEnvironmentVariable("SignalBotUserId"),
    Environment.GetEnvironmentVariable("SignalBotHash"),
    Environment.GetEnvironmentVariable("SignalBotPhone"));
await tl.Init();
