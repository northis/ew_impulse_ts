using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using TradeKit.EventArgs;

namespace TradeKit
{
    /// <summary>
    /// Class report the signals via telegram
    /// </summary>
    public class TelegramReporter
    {
        private readonly TelegramBotClient m_TelegramBotClient;
        private readonly ChatId m_TelegramChatId;
        private readonly Dictionary<string, int> m_SignalPostIds;

        private const string TOKEN_NAME = "IMPULSE_FINDER_BOT_TOKEN_NAME";
        private const string CHAT_ID = "IMPULSE_FINDER_BOT_CHAT_ID";

        /// <summary>
        /// Initializes a new instance of the <see cref="TelegramReporter"/> class.
        /// </summary>
        /// <param name="botToken">The bot token.</param>
        /// <param name="chatId">The chat identifier.</param>
        public TelegramReporter(string botToken, string chatId)
        {
            m_SignalPostIds = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(botToken))
            {
                botToken = Environment.GetEnvironmentVariable(TOKEN_NAME);
            }

            if (string.IsNullOrEmpty(chatId))
            {
                chatId = Environment.GetEnvironmentVariable(CHAT_ID);
            }

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
            {
                return;
            }

            m_TelegramBotClient = new TelegramBotClient(botToken)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            
            m_TelegramChatId = new ChatId(chatId);
            IsReady = true;
        }

        /// <summary>
        /// Prices the format.
        /// </summary>
        /// <param name="price">The price.</param>
        /// <param name="digits">The digits.</param>
        private string PriceFormat(double price, int digits)
        {
            return price.ToString($"F{digits}", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reports the stop loss.
        /// </summary>
        /// <param name="finderId">The finder identifier.</param>
        public void ReportStopLoss(string finderId)
        {
            ReportClose(finderId, "SL hit");
        }

        /// <summary>
        /// Reports the take profit.
        /// </summary>
        /// <param name="finderId">The finder identifier.</param>
        public void ReportTakeProfit(string finderId)
        {
            ReportClose(finderId, "TP hit");
        }

        /// <summary>
        /// Reports the close of the position.
        /// </summary>
        /// <param name="finderId">The finder identifier.</param>
        /// <param name="text">The text.</param>
        private void ReportClose(string finderId, string text)
        {
            if (!m_SignalPostIds.TryGetValue(finderId, out int postId))
            {
                return;
            }
            
            m_TelegramBotClient.SendTextMessageAsync(
                m_TelegramChatId, text, null, null, null, null, postId);
        }

        /// <summary>
        /// Reports the signal.
        /// </summary>
        /// <param name="signalArgs">The signal arguments.</param>
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

            double sl = signalEventArgs.StopLoss.Price;
            double tp = signalEventArgs.TakeProfit.Price;

            double nom = Math.Abs(price - sl);
            double den = Math.Abs(price - tp);

            var sb = new StringBuilder();
            sb.AppendLine($"#{signalArgs.SymbolName.Replace(" ","")} {tradeType} {PriceFormat(price, signalArgs.Digits)}");
            sb.AppendLine($"TP {PriceFormat(signalEventArgs.TakeProfit.Price, signalArgs.Digits)}");
            sb.AppendLine($"SL {PriceFormat(signalEventArgs.StopLoss.Price, signalArgs.Digits)}");

            if (den > 0)
            {
                sb.AppendLine($"Risk/Reward {PriceFormat(100 * nom / den, 0)}%");
                sb.AppendLine($"Spread/Reward {PriceFormat(100 * spread / den, 0)}%");
            }

            string alert = sb.ToString();
            Message msgRes = m_TelegramBotClient
                .SendTextMessageAsync(m_TelegramChatId, alert)
                .Result;

            m_SignalPostIds[signalArgs.SenderId] = msgRes.MessageId;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is ready.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is ready; otherwise, <c>false</c>.
        /// </value>
        public bool IsReady { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public class SignalArgs
        {
            /// <summary>
            /// Gets or sets the signal event arguments.
            /// </summary>
            public SignalEventArgs SignalEventArgs { get; set; }

            /// <summary>
            /// Gets or sets the sender identifier.
            /// </summary>
            public string SenderId { get; set; }

            /// <summary>
            /// Gets or sets the name of the symbol.
            /// </summary>
            public string SymbolName { get; set; }

            /// <summary>
            /// Gets or sets the current bid.
            /// </summary>
            public double Bid { get; set; }

            /// <summary>
            /// Gets or sets the current ask.
            /// </summary>
            public double Ask { get; set; }

            /// <summary>
            /// Gets or sets the amount of digits for the <see cref="SymbolName"/>.
            /// </summary>
            public int Digits { get; set; }
        }
    }
}
