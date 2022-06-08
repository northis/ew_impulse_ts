using System;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using TradeKit.Config;
using TradeKit.EventArgs;

namespace TradeKit
{
    public class TelegramReporter
    {
        private readonly MainState m_State;
        private readonly TelegramBotClient m_TelegramBotClient;
        private readonly ChatId m_TelegramChatId;

        private const string TOKEN_NAME = "IMPULSE_FINDER_BOT_TOKEN_NAME";
        private const string CHAT_ID = "IMPULSE_FINDER_BOT_CHAT_ID";

        public TelegramReporter(string botToken, string chartId, MainState state)
        {
            m_State = state;
            if (string.IsNullOrEmpty(botToken))
            {
                botToken = Environment.GetEnvironmentVariable(TOKEN_NAME);
            }

            if (string.IsNullOrEmpty(chartId))
            {
                chartId = Environment.GetEnvironmentVariable(CHAT_ID);
            }

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chartId))
            {
                return;
            }

            m_TelegramBotClient = new TelegramBotClient(botToken)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            m_TelegramChatId = new ChatId(Convert.ToInt64(chartId));
            IsReady = true;
        }

        private string PriceFormat(double price, int digits)
        {
            return price.ToString($"F{digits}", CultureInfo.InvariantCulture);
        }

        public void ReportStopLoss(string symbol)
        {
            ReportClose(symbol, "SL hit");
        }

        public void ReportTakeProfit(string symbol)
        {
            ReportClose(symbol, "TP hit");
        }

        private void ReportClose(string symbol, string text)
        {
            if (!m_State.States.TryGetValue(symbol, out SymbolState symbolState))
            {
                return;
            }
            
            m_TelegramBotClient.SendTextMessageAsync(
                m_TelegramChatId, text, null, null, null, null, symbolState.LastSignalMessageId);
        }

        public void ReportSignal(SignalArgs signalArgs)
        {
            if (!IsReady)
            {
                return;
            }

            SignalEventArgs signalEventArgs = signalArgs.SignalEventArgs;
            double price;
            double spread = signalArgs.Ask - signalArgs.Bid;
            bool isLong = signalEventArgs.StopLoss.Price < signalEventArgs.TakeProfit.Price;
            string tradeType;
            if (isLong)
            {
                price = signalArgs.Ask;
                tradeType = "BUY";
            }
            else
            {
                price = signalArgs.Bid;
                tradeType = "SELL";
            }

            double sl = signalEventArgs.StopLoss.Price + (isLong ? 0 : spread);
            double tp = signalEventArgs.TakeProfit.Price + (isLong ? 0 : spread);

            double den = Math.Abs(price - sl);
            string ratio = den > 0
                ? $" (R:R {Math.Abs(price - tp) / den:F1})"
                : string.Empty;
            
            var sb = new StringBuilder();
            sb.Append($"#{signalArgs.SymbolName} {tradeType}");
            sb.AppendLine($"TP: {PriceFormat(signalEventArgs.TakeProfit.Price, signalArgs.Digits)}");
            sb.AppendLine($"SL: {PriceFormat(signalEventArgs.StopLoss.Price, signalArgs.Digits)}");
            sb.AppendLine($"Price: {PriceFormat(price, signalArgs.Digits)}{ratio}");

            string alert = sb.ToString();
            Message msgRes = m_TelegramBotClient
                .SendTextMessageAsync(m_TelegramChatId, alert)
                .Result;

            if (m_State.States.TryGetValue(signalArgs.SymbolName, out SymbolState symbolState))
            {
                symbolState.LastSignalMessageId = msgRes.MessageId;
            }
        }

        public bool IsReady { get; set; }

        public class SignalArgs
        {
            public SignalEventArgs SignalEventArgs { get; set; }
            public string SymbolName { get; set; }
            public double Bid { get; set; }
            public double Ask { get; set; }
            public int Digits { get; set; }
        }
    }
}
