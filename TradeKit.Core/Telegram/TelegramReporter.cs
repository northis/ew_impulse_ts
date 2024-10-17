using System.Diagnostics;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using File = System.IO.File;

namespace TradeKit.Core.Telegram
{
    /// <summary>
    /// Class report the signals via telegram
    /// </summary>
    public class TelegramReporter
    {
        private readonly IStorageManager m_StorageManager;
        private readonly bool m_ReportClose;
        private readonly TelegramBotClient m_TelegramBotClient;
        private readonly ChatId m_TelegramChatId;
        private readonly Dictionary<string, int> m_SignalPostIds;
        private readonly Dictionary<string, SignalArgs> m_SignalEventArgsMap;

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

        private const string DEFAULT_PROVIDER = "OANDA:";
        private readonly Dictionary<string, string> m_ProviderMap = new()
        {
            {"BTCUSD", "BINANCE:BTCUSDT"},
            {"ETHUSD", "BINANCE:ETHUSDT"}
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="TelegramReporter"/> class.
        /// </summary>
        /// <param name="botToken">The bot token.</param>
        /// <param name="chatId">The chat identifier.</param>
        /// <param name="storageManager">Manager to save the state between runs.</param>
        /// <param name="reportClose">If true - the close messages will be posted (tp hit)</param>
        public TelegramReporter(string botToken, string chatId, IStorageManager storageManager, bool reportClose = true)
        {
            m_StorageManager = storageManager;
            m_ReportClose = reportClose;
            m_SignalPostIds = storageManager.GetSavedState() ?? new Dictionary<string, int>();
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
            m_SignalEventArgsMap = new Dictionary<string, SignalArgs>();//TODO get it saved to the local storage
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
        /// <param name="closeImagePath">The close image path.</param>
        public void ReportStopLoss(string posId, string closeImagePath = null)
        {
            if (m_ReportClose)
            {
                string stat = string.Empty;
                if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
                {
                    stat = GetStatistic(args, args.SignalEventArgs.StopLoss.Value, -1);
                }

                ReportClose(posId, $"SL hit{stat}", closeImagePath);//TODO for breakeven this is not right
                m_SignalEventArgsMap.Remove(posId);
                m_SignalPostIds.Remove(posId);
                m_StorageManager.SaveState(m_SignalPostIds);
            }
        }

        private string GetStatistic(SignalArgs args, double targetLevel, int k = 1)
        {
            double wholeRisk = args.SignalEventArgs.WholeRange;
            if (wholeRisk <= 0) 
                return string.Empty;

            double diff = k * Math.Abs(args.SignalEventArgs.Level.Value - targetLevel);
            double diffP = diff / args.PipSize;
            double toVal = diff / wholeRisk;
            StatisticItem res = m_StorageManager.AddSetupResult(new StatisticItem
            {
                CloseDateTime = args.SignalEventArgs.Level.OpenTime,
                ResultValue = toVal,
                ResultPips = diffP
            });

            StatisticItem all = m_StorageManager.GetLatest(TimeSpan.Zero);
            string sign = k > 0 ? "+" : string.Empty;
            string resStr =
                $": {sign}{toVal:N2}, {sign}{diffP:N0} pips{Environment.NewLine}Day {res.ResultValue:N2}, {res.ResultPips:N0} pips{Environment.NewLine}Σ {all.ResultValue:N2}, {all.ResultPips:N0} pips";
            return resStr;
        }

        /// <summary>
        /// Reports the take profit.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="closeImagePath">The close image path.</param>
        public void ReportTakeProfit(string posId, string closeImagePath = null)
        {
            if (m_ReportClose)
            {
                string stat = string.Empty;
                if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
                {
                    stat = GetStatistic(args, args.SignalEventArgs.TakeProfit.Value);
                }

                m_SignalEventArgsMap.Remove(posId);
                ReportClose(posId, $"TP hit{stat}", closeImagePath);
                m_SignalPostIds.Remove(posId);
                m_StorageManager.SaveState(m_SignalPostIds);
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
        /// <param name="closeImagePath">The close image path.</param>
        private void ReportClose(string posId, string text, string closeImagePath = null)
        {
            if (!m_SignalPostIds.TryGetValue(posId, out int postId))
            {
                return;
            }

            SendMessage(text, postId, null, closeImagePath);
        }

        Message SendMessage(string textToShow, int? postId = null,
            InlineKeyboardMarkup chartLink = null, string imagePath = null)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return m_TelegramBotClient
                    .SendTextMessageAsync(m_TelegramChatId, textToShow, replyMarkup: chartLink,
                        replyToMessageId: postId)
                    .Result;
            }

            using FileStream fileStream = File.Open(imagePath, FileMode.Open);
            string fileName = Path.GetFileName(imagePath);

            return m_TelegramBotClient
                .SendPhotoAsync(m_TelegramChatId, new InputMedia(fileStream, fileName), textToShow,
                    replyMarkup: chartLink, replyToMessageId: postId)
                .Result;
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
                sb.AppendLine($"Profit = {PriceFormat(100 * nom / den, 2)}%");
                sb.AppendLine($"Spread = {PriceFormat(100 * spread / den, 2)}%");
            }

            //string comment = signalArgs.SignalEventArgs.Comment;
            //if (!string.IsNullOrEmpty(comment))
            //{
            //    sb.AppendLine(comment);
            //}

            TimeSpan tfTs = TimeFrameHelper.GetTimeFrameInfo(signalArgs.SignalEventArgs.Level.BarTimeFrame).TimeSpan;
            if (!m_ProviderMap.TryGetValue(signalArgs.SymbolName, out string provPart))
            {
                provPart = $"{DEFAULT_PROVIDER}{signalArgs.SymbolName}";
            }

            string alert = sb.ToString();
            InlineKeyboardMarkup chartLink = new(new[]
            {
                InlineKeyboardButton.WithUrl(
                    text: $"Chart {tfTs.TotalMinutes}m",
                    url: $"{Helper.PrivateChartUrl}?symbol={provPart}&interval={tfTs.TotalMinutes}")
            });

            Message msgRes = SendMessage(alert, null, chartLink, signalArgs.PlotImagePath);
            string positionId = Helper.GetPositionId(signalArgs.SenderId,
                signalArgs.SignalEventArgs.Level,
                signalArgs.SignalEventArgs.Comment);
            m_SignalPostIds[positionId] = msgRes.MessageId;
            m_SignalEventArgsMap[positionId] = signalArgs;
            m_StorageManager.SaveState(m_SignalPostIds);
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
            /// Gets or sets size of pip for the symbol.
            /// </summary>
            public double PipSize { get; set; }

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
