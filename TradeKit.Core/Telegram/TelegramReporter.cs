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

        private readonly Dictionary<string, string> m_SymbolsMap = new()
        {
            //{"XAUUSD", "GOLD"},
            //{"XAGUSD", "SILVER"},
            {"US 30", "US30"},
            {"US TECH 100", "NAS100"},
            //{"BTCUSD", "BTC"},
            //{"ETHUSD", "ETH"}
        };

        private const string DEFAULT_PROVIDER = "ICMARKETS:";
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
        public TelegramReporter(string botToken, 
            string chatId,
            IStorageManager storageManager, 
            bool reportClose = true)
        {
            m_StorageManager = storageManager;
            m_ReportClose = reportClose;
            m_SignalPostIds = storageManager.GetSavedState() ?? new Dictionary<string, int>();

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
            {
                return;
            }

            m_TelegramBotClient = new TelegramBotClient(botToken)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            
            m_TelegramChatId = new ChatId(chatId);
            IsReady = true;
            m_SignalEventArgsMap = new Dictionary<string, SignalArgs>();//TODO get it saved to the local storage
        }

        /// <summary>
        /// Reports the stop loss.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="closePrice">
        /// The price the position was really closed at. A stop is filled with slippage on a thin
        /// market, and the result is counted in risks, so the loss is reported as −1.5R rather
        /// than −1R (DIAGONAL.md §6.7). <c>0</c> — unknown, the stop level is used.
        /// </param>
        /// <param name="closeImagePath">The close image path.</param>
        public void ReportStopLoss(string posId, double closePrice = 0, string closeImagePath = null)
        {
            if (!m_ReportClose) return;

            string stat = string.Empty;
            if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
            {
                stat = GetStatistic(args, ExitPrice(closePrice, args.SignalEventArgs.StopLoss.Value));
            }

            ReportPositionInfo(posId, $"SL hit{stat}", closeImagePath);//TODO for breakeven this is not right
            RemoveSignal(posId);
        }

        /// <summary>
        /// The exit price to count the result by: the real fill when it is known, the planned
        /// level otherwise.
        /// </summary>
        private static double ExitPrice(double closePrice, double plannedLevel)
        {
            return closePrice > 0 ? closePrice : plannedLevel;
        }

        /// <summary>
        /// The result of a setup in R — multiples of the risk planned at the entry
        /// (<c>|entry − SL|</c>), counted by the price the position was <b>really</b> closed at.
        /// A stop swept with slippage gives less than −1, a part closed early (see
        /// <see cref="ReportPartialClose"/>) is already realized and only the rest of the
        /// position rides to this exit.
        /// </summary>
        /// <param name="args">The signal the position was opened for.</param>
        /// <param name="exitPrice">The price the position was closed at.</param>
        public static double GetSetupResult(SignalArgs args, double exitPrice)
        {
            double entry = args.SignalEventArgs.Level.Value;
            double wholeRisk = Math.Abs(entry - args.SignalEventArgs.StopLoss.Value);
            if (wholeRisk <= 0)
                return 0;

            double direction = args.SignalEventArgs.StopLoss.Value <
                               args.SignalEventArgs.TakeProfit.Value
                ? 1
                : -1;

            return args.ClosedResult + (1 - args.ClosedPart) * direction * (exitPrice - entry) / wholeRisk;
        }

        private string GetStatistic(SignalArgs args, double exitPrice)
        {
            double entry = args.SignalEventArgs.Level.Value;
            double wholeRisk = Math.Abs(entry - args.SignalEventArgs.StopLoss.Value);
            if (wholeRisk <= 0) 
                return string.Empty;

            double direction = args.SignalEventArgs.StopLoss.Value <
                               args.SignalEventArgs.TakeProfit.Value
                ? 1
                : -1;

            double toVal = GetSetupResult(args, exitPrice);
            double diffP = direction * (exitPrice - entry) / args.PipSize;
            StatisticItem res = m_StorageManager.AddSetupResult(toVal);

            // The two signs are independent: with a part of the position closed early the
            // realized result and this exit can be on different sides of the entry.
            string resultSign = toVal > 0 ? "+" : string.Empty;
            string pipsSign = diffP > 0 ? "+" : string.Empty;
            string resStr =
                $": {resultSign}{Helper.PriceFormat(toVal, 2)}, {pipsSign}{diffP:N0} pips; Σ={Helper.PriceFormat(res.ResultValue, 2)} ({res.SetupsCount:N0}).";
            return resStr;
        }

        /// <summary>
        /// Reports an early partial close: <paramref name="ratio"/> of the position is closed at
        /// <paramref name="price"/>, the rest keeps running to TP or SL.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="price">The price the part was closed at.</param>
        /// <param name="ratio">The share of the position closed.</param>
        public void ReportPartialClose(string posId, double price, double ratio)
        {
            if (!m_ReportClose) return;

            string stat = string.Empty;
            if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
            {
                double entry = args.SignalEventArgs.Level.Value;
                double wholeRisk = Math.Abs(entry - args.SignalEventArgs.StopLoss.Value);
                double part = Math.Min(ratio, 1 - args.ClosedPart);
                if (wholeRisk > 0 && part > 0)
                {
                    int dir = args.SignalEventArgs.StopLoss.Value <
                              args.SignalEventArgs.TakeProfit.Value
                        ? 1
                        : -1;
                    double resultValue = dir * (price - entry) / wholeRisk;
                    args.ClosedResult += part * resultValue;
                    args.ClosedPart += part;
                    stat = $": {Helper.PriceFormat(resultValue, 2)}R";
                }
            }

            ReportPositionInfo(posId, $"Close {ratio:P0} of the position{stat}");
        }

        /// <summary>
        /// Reports the take profit.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="closePrice">
        /// The price the position was really closed at; <c>0</c> — unknown, the target level is
        /// used. See <see cref="ReportStopLoss"/>.
        /// </param>
        /// <param name="closeImagePath">The close image path.</param>
        public void ReportTakeProfit(string posId, double closePrice = 0, string closeImagePath = null)
        {
            if (!m_ReportClose) return;

            string stat = string.Empty;
            if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
            {
                stat = GetStatistic(args, ExitPrice(closePrice, args.SignalEventArgs.TakeProfit.Value));
            }

            ReportPositionInfo(posId, $"TP hit{stat}", closeImagePath);
            RemoveSignal(posId);
        }

        /// <summary>
        /// Reports the manual closing.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="price">The current price.</param>
        /// <param name="closeImagePath">The close image path.</param>
        public void ReportManualClose(string posId, double price, string closeImagePath = null)
        {
            if (!m_ReportClose) return;

            string stat = string.Empty;
            if (m_SignalEventArgsMap.TryGetValue(posId, out SignalArgs args))
            {
                stat = GetStatistic(args, price);
            }

            ReportPositionInfo(posId, $"Manually closed{stat}", closeImagePath);
            RemoveSignal(posId);
        }

        private void RemoveSignal(string posId)
        {
            m_SignalEventArgsMap.Remove(posId);
            m_SignalPostIds.Remove(posId);
            m_StorageManager.SaveState(m_SignalPostIds);
        }

        /// <summary>
        /// Reports the breakeven.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        public void ReportBreakeven(string posId)
        {
            if (m_ReportClose)
                ReportPositionInfo(posId, "Move the stop to the entry point");
        }

        /// <summary>
        /// Reports the order cancellation.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        public void ReportCanceled(string posId)
        {
            if (m_ReportClose)
                ReportPositionInfo(posId, "Cancel the order.");

            RemoveSignal(posId);
        }

        /// <summary>
        /// Reports the order activation.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        public void ReportActivated(string posId)
        {
            if (m_ReportClose)
                ReportPositionInfo(posId, "The order is active.");
        }

        /// <summary>
        /// Reports the info about the position.
        /// </summary>
        /// <param name="posId">The position identifier.</param>
        /// <param name="text">The text.</param>
        /// <param name="closeImagePath">The close image path.</param>
        private void ReportPositionInfo(string posId, string text, string closeImagePath = null)
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
            
            double sl = signalEventArgs.StopLoss.Value;
            double tp = signalEventArgs.TakeProfit.Value;
            if (isLong)
            {
                price = signalEventArgs.IsLimit ? signalEventArgs.Level.Value : signalArgs.Ask;
                tradeType = "BUY";
            }
            else
            {
                price = signalEventArgs.IsLimit ? signalEventArgs.Level.Value : signalArgs.Bid;
                tradeType = "SELL";

                tp -= spread;
                sl += spread;
            }


            double nom = Math.Abs(price - tp);
            double den = Math.Abs(sl - price);

            var sb = new StringBuilder();
            string symbolViewName = 
                m_SymbolsMap.TryGetValue(signalArgs.SymbolName, out string preDefValue)
                ? preDefValue 
                : signalArgs.SymbolName.Replace(" ", "");

            if (signalArgs.SignalEventArgs.IsLimit)
                tradeType += " limit";

            sb.AppendLine($"#setup #{symbolViewName} {tradeType} {Helper.PriceFormat(price, signalArgs.Digits)}");
            sb.AppendLine($"TP {Helper.PriceFormat(signalEventArgs.TakeProfit.Value, signalArgs.Digits)}");
            sb.AppendLine($"SL {Helper.PriceFormat(signalEventArgs.StopLoss.Value, signalArgs.Digits)}");

            if (den > 0)
            {
                double profit = nom / den;

                sb.AppendLine(
                    $"RR = {Helper.PriceFormat(profit, 2)}; {Helper.PriceFormat(nom / signalArgs.PipSize, 0)} pips; Δ={Helper.PriceFormat(100 * nom / price, 2)}%");
            }

            //sb.AppendLine($"Spread = {Helper.PriceFormat(spread * Math.Pow(10, signalArgs.Digits), 0)}");

            string comment = signalArgs.SignalEventArgs.Comment;
            if (!string.IsNullOrEmpty(comment))
            {
                sb.AppendLine(comment);
            }

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

            /// <summary>
            /// Gets or sets the share of the position already closed early (see
            /// <see cref="ReportPartialClose"/>).
            /// </summary>
            public double ClosedPart { get; set; }

            /// <summary>
            /// Gets or sets the result (in R) already realized on <see cref="ClosedPart"/>.
            /// </summary>
            public double ClosedResult { get; set; }
        }
    }
}
