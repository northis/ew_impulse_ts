using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Json;

namespace TradeKit.Core.Signals
{
    public class ParseSetupFinder : SingleSetupFinder<SignalEventArgs>
    {
        private readonly ITradeViewManager m_TradeViewManager;
        private readonly string m_SignalHistoryFilePath;
        private readonly bool m_UseLimitOrders;
        private readonly double m_MaxStopRatio = 0.20;
        private const string SIGNAL_REGEX = @"(buy|sell)([^\n]*?(?:\n[^\d\n@\-]*)?)[@\-]\s*(\d+(?:[.,]\d{0,5})?)";
        private const string BREAKEVEN_REGEX = @"(running in profit|entry point|break\s*-?\s*even|to the entry|b\.e|move sl to entry|sl\s+move\s+to\s+(?:the\s+)?entry)";
        private const string EXTRA_REGEX = @"\b(extra)\b";
        private const string TP_HIT_REGEX = @"(tp\s*\d*\s*hit|target\s+hit|🎯)";
        private const string SL_HIT_REGEX = @"(sl\s*(was\s*)?hit|stop\s*out)";
        private const string ACTIVATED_REGEX = @"(activated)";
        private const string CANCELED_REGEX = @"(delete|cancel|remove)";
        private const string LIMIT_REGEX = @"(limit)";
        private const string STRIKETHROUGH_REGEX = @"{(\n|\s)*""type"":(\n|\s)*""strikethrough"",(\n|\s)*""text"":(\n|\s)*""(.)*""(\n|\s)*},";
        private const string CLOSE_REGEX = @"(close[d\s]|close$)";
        private const string TP_REGEX = @"(tp|take profit)(.*)\s(\d+(?:[.,]\d{0,5})?)";
        private const string SL_REGEX = @"(SL|stop\s?loss)\D*(\d{1,9}(?:[.,]\d{0,5})?)";

        private static readonly NumberStyles NUMBER_STYLES = NumberStyles.AllowCurrencySymbol
                                                             | NumberStyles.AllowDecimalPoint
                                                             | NumberStyles.AllowLeadingWhite
                                                             | NumberStyles.AllowTrailingWhite
                                                             | NumberStyles.AllowThousands;

        private static readonly Dictionary<string, string> SYMBOL_REGEX_MAP = new()
        {
            {"XAUUSD", @"(gold|xauusd|one\s*more)"}
        };

        private static readonly Dictionary<string, double> DEFAULT_TP_PIPS = new()
        {
            {"XAUUSD", 1000}
        };

        private static readonly Dictionary<string, double> DEFAULT_SL_PIPS = new()
        {
            {"XAUUSD", 1000}
        };

        private readonly Dictionary<long, TelegramHistoryMessage> m_Messages;
        private readonly Dictionary<long, SignalEventArgs> m_MessageSignalArgsMap;
        private readonly SortedDictionary<DateTime, TelegramHistoryMessage> m_MessagesDate;
        public ParseSetupFinder(IBarsProvider mainBarsProvider, 
            ISymbol symbol, 
            ITradeViewManager tradeViewManager,
            string signalHistoryFilePath,
            bool useLimitOrders = true) 
            : base(mainBarsProvider, symbol)
        {
            m_TradeViewManager = tradeViewManager;
            m_UseLimitOrders = useLimitOrders;
            char[] trimChars = { '"', ' ' };
            m_SignalHistoryFilePath = signalHistoryFilePath.TrimStart(trimChars).TrimEnd(trimChars);
            m_MessageSignalArgsMap = new Dictionary<long, SignalEventArgs>();
            if (m_SignalHistoryFilePath != null)
            {
                m_Messages = new Dictionary<long, TelegramHistoryMessage>();
                m_MessagesDate = new SortedDictionary<DateTime, TelegramHistoryMessage>();
                ParseSignals();
            }
        }
        
        /// <summary>
        /// Gets the last entry.
        /// </summary>
        public BarPoint LastEntry { get; private set; }

        protected override void CheckSetup(DateTime openDateTime)
        {
            ProcessSetup();
        }

        private void HandleSignalAction(
            KeyValuePair<DateTime, TelegramHistoryMessage> matchedSignal, string symbolRegex)
        {
            SignalAction res = ProcessMessage(matchedSignal.Value, symbolRegex, out ParsedSignal signal);

            bool hasTp = signal.TakeProfits != null && signal.TakeProfits.Any();
            bool hasSl = signal.HasExplicitSl;
            bool isEnter = (res & SignalAction.ENTER_BUY) == SignalAction.ENTER_BUY ||
                           (res & SignalAction.ENTER_SELL) == SignalAction.ENTER_SELL;
            if (isEnter && hasSl)
            {
                bool isKeywordLimit = (res & SignalAction.LIMIT) == SignalAction.LIMIT;
                if (isKeywordLimit)
                {
                    return;// We skip keyword-based limit orders.
                }

                double tp;
                if (hasTp)
                {
                    // Use the TP closest to the entry price
                    double entry = signal.Price.GetValueOrDefault();
                    tp = signal.TakeProfits.OrderBy(t => Math.Abs(t - entry)).First();
                }
                else if (DEFAULT_TP_PIPS.TryGetValue(Symbol.Name, out double defaultTpPips))
                {
                    double entry = signal.Price.GetValueOrDefault();
                    tp = entry + Symbol.PipSize * defaultTpPips * (signal.IsLong ? 1 : -1);
                }
                else
                {
                    return; // Cannot open without a TP reference
                }

                // If the current price is less favorable than the signal entry, use a limit order
                // so we wait for the market to come back to the intended entry level.
                // BUY: less favorable when ask > entry (price already moved up past entry)
                // SELL: less favorable when bid < entry (price already moved down past entry)
                double currentPrice = signal.IsLong
                    ? m_TradeViewManager.GetAsk(Symbol)
                    : m_TradeViewManager.GetBid(Symbol);
                bool useLimitOrder = m_UseLimitOrders && signal.Price.HasValue &&
                    (signal.IsLong
                        ? currentPrice > signal.Price.Value
                        : currentPrice < signal.Price.Value);

                var args = new SignalEventArgs(
                    new BarPoint(signal.Price.GetValueOrDefault(), LastBarOpenDateTime, BarsProvider),
                    new BarPoint(tp, LastBarOpenDateTime, BarsProvider),
                    new BarPoint(signal.StopLoss, LastBarOpenDateTime, BarsProvider),
                    isLimit: useLimitOrder);
                m_MessageSignalArgsMap[matchedSignal.Value.Id] = args;
                OnEnterInvoke(args);
                return;
            }

            long? replyId = matchedSignal.Value.ReplyId ?? m_MessageSignalArgsMap.LastOrDefault().Key;
            if (replyId is null or <= 0)
            {
                return;
            }

            if (!m_MessageSignalArgsMap.TryGetValue(replyId.Value, out SignalEventArgs refSignal))
            {
                return;
            }
            if ((res & SignalAction.EXTRA) == SignalAction.EXTRA)
            {
                var args = new SignalEventArgs(
                    new BarPoint(refSignal.Level.Value, LastBarOpenDateTime, BarsProvider),
                    refSignal.TakeProfit,
                    refSignal.StopLoss, comment: refSignal.Comment);
                m_MessageSignalArgsMap[matchedSignal.Value.Id] = args;
                OnEnterInvoke(args);
                return;
            }

            if ((res & SignalAction.SET_BREAKEVEN) == SignalAction.SET_BREAKEVEN)
            {
                if (refSignal.IsLimit && !refSignal.IsActive)
                {
                    // Cancel the pending limit order when a breakeven signal arrives
                    OnCanceledInvoke(new LevelEventArgs(refSignal.Level, refSignal.Level));
                    m_MessageSignalArgsMap.Remove(replyId.Value);
                    return;
                }

                if (!refSignal.HasBreakeven)
                {
                    var newSl = new BarPoint(refSignal.Level.Value, LastBarOpenDateTime, BarsProvider);
                    refSignal.HasBreakeven = true;
                    OnBreakEvenInvoke(new LevelEventArgs(newSl, refSignal.Level, hasBreakeven: true, closeHalf: true));
                    refSignal.StopLoss = newSl;
                    // Keep in map so price-movement TP/SL checks and further reply messages still work
                }
            }
            else if ((res & SignalAction.HIT_TP) == SignalAction.HIT_TP)
            {
                OnTakeProfitInvoke(new LevelEventArgs(refSignal.TakeProfit, refSignal.Level, true));
                m_MessageSignalArgsMap.Remove(replyId.Value);
            }
            else if ((res & SignalAction.HIT_SL) == SignalAction.HIT_SL)
            {
                OnStopLossInvoke(new LevelEventArgs(refSignal.StopLoss, refSignal.Level, true));
                m_MessageSignalArgsMap.Remove(replyId.Value);
            }
            else if ((res & SignalAction.SET_SL) == SignalAction.SET_SL ||
                (res & SignalAction.SET_TP) == SignalAction.SET_TP)
            {
                if ((res & SignalAction.SET_SL) == SignalAction.SET_SL && signal.StopLoss > 0)
                {
                    foreach (SignalEventArgs refSignalToChange in
                             m_MessageSignalArgsMap.Values.Where(a => a.Comment == refSignal.Comment))
                    {
                        if (refSignalToChange.HasBreakeven)
                            continue;

                        refSignalToChange.StopLoss =
                            new BarPoint(signal.StopLoss, LastBarOpenDateTime, BarsProvider);
                    }
                }

                if (hasTp)
                {
                    // Use the TP closest to the entry price
                    double entry = refSignal.Level.Value;
                    double closestTp = signal.TakeProfits.OrderBy(t => Math.Abs(t - entry)).First();
                    refSignal.TakeProfit =
                        new BarPoint(closestTp, LastBarOpenDateTime, BarsProvider);
                }

                OnEditInvoke(refSignal);
            }
            else if ((res & SignalAction.CLOSE) == SignalAction.CLOSE)
            {
                OnManualCloseInvoke(new LevelEventArgs(refSignal.Level, refSignal.Level, true));
                m_MessageSignalArgsMap.Remove(replyId.Value);
            }
            else if ((res & SignalAction.ACTIVATED) == SignalAction.ACTIVATED && refSignal.IsLimit && !refSignal.IsActive)
            {
                refSignal.IsActive = true;
                OnActivatedInvoke(new LevelEventArgs(refSignal.Level, refSignal.Level, true));
            }
            else if ((res & SignalAction.CANCELLED) == SignalAction.CANCELLED && refSignal.IsLimit &&
                     !refSignal.IsActive)
            {
                OnCanceledInvoke(new LevelEventArgs(refSignal.Level, refSignal.Level, true));
                m_MessageSignalArgsMap.Remove(replyId.Value);
            }
        }

        /// <summary>
        /// Finds or closes the trade setup.
        /// </summary>
        private void ProcessSetup()
        {
            // Use LastBarOpenDateTime (set by CheckBar) instead of Count-1.
            // In an indicator all bars are already loaded, so Count is always the total bars
            // count. Using Count-1 would always process the last bar instead of walking
            // through history bar by bar. GetIndexByTime gives the correct sequential index.
            DateTime barDateTime = LastBarOpenDateTime;
            if (barDateTime == DateTime.MinValue)
                return;

            int barIndex = BarsProvider.GetIndexByTime(barDateTime);
            if (barIndex < 1)
                return;

            DateTime prevBarDateTime = BarsProvider.GetOpenTime(barIndex - 1);
            if (prevBarDateTime == DateTime.MinValue)
            {
                return;
            }

            double low = BarsProvider.GetLowPrice(barDateTime);
            double high = BarsProvider.GetHighPrice(barDateTime);

            if (!SYMBOL_REGEX_MAP.TryGetValue(Symbol.Name, out string symbolRegex))
            {
                return;
            }
            
            var idsToRemove = new List<long>();
            foreach (long messageId in m_MessageSignalArgsMap.Keys)
            {
                SignalEventArgs value = m_MessageSignalArgsMap[messageId];
                bool isUp = value.StopLoss < value.TakeProfit; // true for BUY, false for SELL

                if (value.IsLimit && !value.IsActive)
                {
                    // Pending limit order: check if entry price was touched this bar.
                    // BUY limit executes when price falls to entry (low <= entry).
                    // SELL limit executes when price rises to entry (high >= entry).
                    bool entryHit = isUp
                        ? low <= value.Level.Value
                        : high >= value.Level.Value;

                    if (!entryHit)
                    {
                        // Entry not yet triggered. If TP was already reached, cancel the order.
                        bool tpReached = (value.TakeProfit.Value < high && isUp) ||
                                         (value.TakeProfit.Value > low && !isUp);
                        if (tpReached)
                        {
                            OnCanceledInvoke(new LevelEventArgs(value.TakeProfit, value.Level));
                            idsToRemove.Add(messageId);
                        }
                        continue; // Skip normal TP/SL check until entry is hit
                    }

                    // Entry was hit — mark position as active and fall through to TP/SL check
                    value.IsActive = true;
                }

                if (value.TakeProfit.Value < high && isUp ||
                    value.TakeProfit.Value > low && !isUp)
                {
                    OnTakeProfitInvoke(new LevelEventArgs(value.TakeProfit, value.Level, true));
                    idsToRemove.Add(messageId);
                    continue;
                }

                if (value.StopLoss.Value > low && isUp ||
                    value.StopLoss.Value < high && !isUp)
                {
                    OnStopLossInvoke(new LevelEventArgs(value.StopLoss, value.Level, true));
                    idsToRemove.Add(messageId);
                }
            }

            foreach (long idToRemove in idsToRemove)
            {
                m_MessageSignalArgsMap.Remove(idToRemove);
            }

            List<KeyValuePair<DateTime, TelegramHistoryMessage>> matchedSignals = m_MessagesDate
                .SkipWhile(a => a.Key <= prevBarDateTime)
                .TakeWhile(a => a.Key <= barDateTime)
                .ToList();
            
            foreach (KeyValuePair<DateTime, TelegramHistoryMessage> matchedSignal in matchedSignals)
            {
                HandleSignalAction(matchedSignal, symbolRegex);
            }
        }

        private SignalAction ProcessMessage(
            TelegramHistoryMessage historyItem, string symbolRegex, out ParsedSignal signalOut)
        {
            string textAll = string.Concat(historyItem.Text).ToLowerInvariant();
            textAll = Regex.Replace(textAll, STRIKETHROUGH_REGEX, "");
            SignalAction resultAction = SignalAction.DEFAULT;

            Match signal = Regex.Match(textAll, SIGNAL_REGEX, RegexOptions.IgnoreCase);
            DateTime utcDateTime = historyItem.Date;
            signalOut = new ParsedSignal
            {
                DateTime = utcDateTime,
                SymbolName = Symbol.Name
            };
            double ask = m_TradeViewManager.GetAsk(Symbol);
            double bid = m_TradeViewManager.GetBid(Symbol);
            
            if (signal.Success)
            {
                if (Regex.IsMatch(textAll, symbolRegex))
                {
                    signalOut.IsLong = signal.Groups[1].Value.ToLowerInvariant() == "buy";
                    if (double.TryParse(signal.Groups[3].Value,
                            NUMBER_STYLES, CultureInfo.InvariantCulture, out double enterPrice))
                    {
                        signalOut.Price = enterPrice;
                        bool isLimit = Regex.IsMatch(textAll, LIMIT_REGEX);
                        if (isLimit)
                            resultAction |= SignalAction.LIMIT;
                    }
                    else
                        signalOut.Price = signalOut.IsLong ? ask : bid;

                    resultAction |= signalOut.IsLong ? SignalAction.ENTER_BUY : SignalAction.ENTER_SELL;
                }
                else
                {
                    return resultAction;
                }
            }

            bool isActivated = Regex.IsMatch(textAll, ACTIVATED_REGEX);
            if (isActivated)
                resultAction |= SignalAction.ACTIVATED;

            double? GetDefaultStopLoss(ParsedSignal signalOut)
            {
                if (DEFAULT_SL_PIPS.TryGetValue(Symbol.Name, out double defaultStopPips))
                {
                    double priceNow = signalOut.Price ?? (signalOut.IsLong ? ask: bid );
                    signalOut.StopLoss = priceNow +
                                         Symbol.PipSize * defaultStopPips * (signalOut.IsLong ? -1 : 1);
                    return signalOut.StopLoss;
                }

                return null;
            }

            Match sl = Regex.Match(textAll, SL_REGEX, RegexOptions.IgnoreCase);
            if (sl.Success && double.TryParse(sl.Groups[2].Value,
                    NUMBER_STYLES, CultureInfo.InvariantCulture, out double slPrice))
            {
                double? defSl = GetDefaultStopLoss(signalOut);
                if (defSl is > 0 && Math.Abs(slPrice - defSl.Value) / defSl < m_MaxStopRatio)
                {
                    signalOut.StopLoss = slPrice;
                    signalOut.HasExplicitSl = true;
                }

                resultAction |= SignalAction.SET_SL;
            }
            else
            {
                if (signal.Success)
                {
                    double? newSl = GetDefaultStopLoss(signalOut);
                    if (newSl.HasValue && Math.Abs(bid - newSl.Value) / bid < m_MaxStopRatio)
                    {
                        resultAction |= SignalAction.SET_SL;
                    }
                }
            }

            MatchCollection tpsCollection = Regex.Matches(textAll, TP_REGEX, RegexOptions.IgnoreCase);
            if (tpsCollection.Count > 0)
            {
                var tpList = new List<double>();
                foreach (Match match in tpsCollection)
                {
                    if (match == null || !double.TryParse(match.Groups[3].Value,
                            NUMBER_STYLES, CultureInfo.InvariantCulture, out double tpPrice))
                        continue;

                    tpList.Add(tpPrice);
                }

                if (tpList.Any())
                {
                    resultAction |= SignalAction.SET_TP;
                }
                else
                {
                    if (DEFAULT_TP_PIPS.TryGetValue(Symbol.Name, out double defaultTake))
                    {
                        tpList.Add(signalOut.Price.GetValueOrDefault() +
                                   Symbol.PipSize * defaultTake * (signalOut.IsLong ? 1 : -1));
                        resultAction |= SignalAction.SET_TP;
                    }
                }

                signalOut.TakeProfits = tpList.ToArray();
            }

            if ((resultAction & SignalAction.SET_TP) != 0 && 
                (resultAction & SignalAction.SET_SL) != 0 &&
                (resultAction & SignalAction.ENTER_BUY) != 0 && 
                (resultAction & SignalAction.ENTER_SELL) != 0 &&
                signalOut.TakeProfits[0] > signalOut.StopLoss != signalOut.IsLong)
            {
                Logger.Write("Wrong signal, ignore it");
                resultAction = SignalAction.DEFAULT;
            }

            Match be = Regex.Match(textAll, BREAKEVEN_REGEX, RegexOptions.IgnoreCase);
            if (be.Success)
            {
                resultAction |= SignalAction.SET_BREAKEVEN;
            }

            Match close = Regex.Match(textAll, CLOSE_REGEX, RegexOptions.IgnoreCase);
            if (close.Success)
            {
                resultAction |= SignalAction.CLOSE;
            } 
            
            Match extra = Regex.Match(textAll, EXTRA_REGEX, RegexOptions.IgnoreCase);
            if (extra.Success)
            {
                resultAction |= SignalAction.EXTRA;
            }

            Match tpHit = Regex.Match(textAll, TP_HIT_REGEX, RegexOptions.IgnoreCase);
            if (tpHit.Success)
            {
                resultAction |= SignalAction.HIT_TP;
            }

            Match slHit = Regex.Match(textAll, SL_HIT_REGEX, RegexOptions.IgnoreCase);
            if (slHit.Success)
            {
                resultAction |= SignalAction.HIT_SL;
            }

            Match cancelled = Regex.Match(textAll, CANCELED_REGEX, RegexOptions.IgnoreCase);
            if (cancelled.Success)
            {
                resultAction |= SignalAction.CANCELLED;
            }

            return resultAction;
        }

        /// <summary>
        /// Parses the signals.
        /// </summary>
        /// <returns>Date-signal map</returns>
        public void ParseSignals()
        {
            var res = new Dictionary<DateTime, ParsedSignal>();
            if (!SYMBOL_REGEX_MAP.TryGetValue(Symbol.Name, out string symbolRegex))
            {
                return;
            }

            string text = File.ReadAllText(m_SignalHistoryFilePath);
            TelegramHistoryMessage[] history = null;
            try
            {
                history = JsonConvert.DeserializeObject<SymbolDataExportJson>(text)?.Messages;
            }
            catch (Exception)
            {
                ;
            }

            if (history == null)
            {
                try
                {
                    history = JsonConvert.DeserializeObject<TelegramHistoryMessage[]>(text);
                }
                catch (Exception)
                {
                    ;
                }
            }

            if (history == null)
            {
                return;
            }

            foreach (TelegramHistoryMessage historyItem in history)
            {
                // Normalize to Unspecified kind with UTC ticks.
                // cTrader bar open times are also Unspecified-UTC, so comparisons
                // will work correctly on any machine timezone.
                historyItem.Date = DateTime.SpecifyKind(
                    historyItem.Date.ToUniversalTime(), DateTimeKind.Unspecified);
                m_Messages[historyItem.Id] = historyItem;
                m_MessagesDate[historyItem.Date] = historyItem;
                //ProcessMessage(historyItem);
            }
        }
    }
}
