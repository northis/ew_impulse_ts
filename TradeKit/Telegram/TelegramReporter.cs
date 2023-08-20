using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using TradeKit.Core;
using TradeKit.EventArgs;
using File = System.IO.File;

namespace TradeKit.Telegram
{
    /// <summary>
    /// Class report the signals via telegram
    /// </summary>
    public class TelegramReporter
    {
        private readonly bool m_ReportClose;
        private readonly Action<Dictionary<string, int>> m_OnSaveState;
        private readonly TelegramBotClient m_TelegramBotClient;
        private readonly ChatId m_TelegramChatId;
        private readonly Dictionary<string, int> m_SignalPostIds;

        private const string TOKEN_NAME = "IMPULSE_FINDER_BOT_TOKEN_NAME";
        private const string CHAT_ID = "IMPULSE_FINDER_BOT_CHAT_ID";

        private readonly Dictionary<string, string> m_SymbolsMap = new()
        {
            {"XAUUSD", "GOLD"},
            {"XAGUSD", "SILVER"},
            {"US 30", "US30"},
            {"US TECH 100", "NAS100"},
            {"BTCUSD", "BTC"},
            {"ETHUSD", "ETH"}
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="TelegramReporter"/> class.
        /// </summary>
        /// <param name="botToken">The bot token.</param>
        /// <param name="chatId">The chat identifier.</param>
        /// <param name="reportClose">If true - the close messages will be posted (tp hit)</param>
        /// <param name="signalPostIds">Optional signal-post id map to keep the state between runs.</param>
        /// <param name="onSaveState">Delegate for saving the state (signal-post id map).</param>
        public TelegramReporter(string botToken, string chatId, bool reportClose = true, Dictionary<string, int> signalPostIds = null, Action<Dictionary<string, int>> onSaveState = null)
        {
            m_ReportClose = reportClose;
            m_OnSaveState = onSaveState;
            m_SignalPostIds = signalPostIds ?? new Dictionary<string, int>();
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
        /// <param name="posId">The position identifier.</param>
        public void ReportStopLoss(string posId)
        {
            if (m_ReportClose)
            {
                ReportClose(posId, "SL hit");
                m_SignalPostIds.Remove(posId);
                m_OnSaveState?.Invoke(m_SignalPostIds);
            }
        }

        /// <summary>
        /// Reports the take profit.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        public void ReportTakeProfit(string posId)
        {
            if (m_ReportClose)
            {
                ReportClose(posId, "TP hit");
                m_SignalPostIds.Remove(posId);
                m_OnSaveState?.Invoke(m_SignalPostIds);
            }
        }

        /// <summary>
        /// Reports the breakeven.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        public void ReportBreakeven(string posId)
        {
            if (m_ReportClose)
                ReportClose(posId, "Move the stop to the entry point");
        }

        /// <summary>
        /// Reports the close of the position.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="text">The text.</param>
        private void ReportClose(string posId, string text)
        {
            if (!m_SignalPostIds.TryGetValue(posId, out int postId))
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
            bool isLong = signalEventArgs.StopLoss.Value < signalEventArgs.TakeProfit.Value;
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

            double sl = signalEventArgs.StopLoss.Value;
            double tp = signalEventArgs.TakeProfit.Value;

            double nom = Math.Abs(price - sl);
            double den = Math.Abs(price - tp);

            var sb = new StringBuilder();
            string symbolViewName = 
                m_SymbolsMap.TryGetValue(signalArgs.SymbolName, out string preDefValue)
                ? preDefValue 
                : signalArgs.SymbolName.Replace(" ", "");

            sb.AppendLine($"#{symbolViewName} {tradeType} {PriceFormat(price, signalArgs.Digits)}");
            sb.AppendLine($"TP {PriceFormat(signalEventArgs.TakeProfit.Value, signalArgs.Digits)}");
            sb.AppendLine($"SL {PriceFormat(signalEventArgs.StopLoss.Value, signalArgs.Digits)}");

            if (den > 0)
            {
                sb.AppendLine($"Risk:Reward = {PriceFormat(nom / den, 2)}");
                sb.AppendLine($"Spread:Reward = {PriceFormat(spread / den, 2)}");
            }

            string comment = signalArgs.SignalEventArgs.Comment;
            if (!string.IsNullOrEmpty(comment))
            {
                sb.AppendLine(comment);
            }

            string alert = sb.ToString();
            Message msgRes;

            if (string.IsNullOrEmpty(signalArgs.PlotImagePath))
            {
                msgRes = m_TelegramBotClient
                    .SendTextMessageAsync(m_TelegramChatId, alert)
                    .Result;
            }
            else
            {
                using FileStream fileStream = File.Open(signalArgs.PlotImagePath, FileMode.Open);
                string fileName = Path.GetFileName(signalArgs.PlotImagePath)
                                  ?? Guid.NewGuid().ToString();

                msgRes = m_TelegramBotClient
                    .SendPhotoAsync(m_TelegramChatId, new InputMedia(fileStream, fileName), alert)
                    .Result;
            }

            string positionId = Helper.GetPositionId(signalArgs.SenderId, signalArgs.SignalEventArgs.Level);
            m_SignalPostIds[positionId] = msgRes.MessageId;
            m_OnSaveState?.Invoke(m_SignalPostIds);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is ready.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is ready; otherwise, <c>false</c>.
        /// </value>
        public bool IsReady { get; set; }

        /// <summary>
        /// <see cref="EventArgs"/> for signal
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

            /// <summary>
            /// Gets or sets the image of signal (a .png file). Can be null
            /// </summary>
            public string PlotImagePath { get; set; }
        }
    }
}
