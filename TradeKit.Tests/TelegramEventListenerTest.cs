using NUnit.Framework;
using TradeKit.Core.Telegram;

namespace TradeKit.Tests
{
    internal class TelegramEventListenerTest
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task MainTelegramEventListenerTest()
        {
            var tl = new TelegramEventListener(Environment.GetEnvironmentVariable("SignalBotUserId"),
                Environment.GetEnvironmentVariable("SignalBotHash"),
                Environment.GetEnvironmentVariable("SignalBotPhone"));
            await tl.Init();
        }
    }
}
