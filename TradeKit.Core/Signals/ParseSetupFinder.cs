﻿using System.Diagnostics;
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
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly ITradeViewManager m_TradeViewManager;
        private readonly string m_SignalHistoryFilePath;
        private readonly bool m_UseUtc;
        private readonly bool m_UseOneTp;
        private const string SIGNAL_REGEX = @"(buy|sell)(.*)\s(\d+(?:[.,]\d{0,5})?)";
        private const string BREAKEVEN_REGEX = @"(book|entry point|breakeven|BE|to the entry)";
        private const string TP_HIT_REGEX = @"(TP hit|🎯)";
        private const string SL_HIT_REGEX = @"(SL hit)";
        private const string ACTIVATED_REGEX = @"(activated)";
        private const string LIMIT_REGEX = @"(limit)";
        private const string CLOSE_REGEX = @"(\sclose\s)";
        private const string TP_REGEX = @"(tp|take profit)(.*)\s(\d+(?:[.,]\d{0,5})?)";
        private const string SL_REGEX = @"(?:SL|stop\s?loss)\b[\s\-–—]*(\d+(?:[.,]\d{0,5})?)";

        private static readonly NumberStyles NUMBER_STYLES = NumberStyles.AllowCurrencySymbol
                                                             | NumberStyles.AllowDecimalPoint
                                                             | NumberStyles.AllowLeadingWhite
                                                             | NumberStyles.AllowTrailingWhite
                                                             | NumberStyles.AllowThousands;

        private static readonly Dictionary<string, string> SYMBOL_REGEX_MAP = new()
        {
            {"XAUUSD", @"(gold)|(xau[\s\\\/-]*usd)|(one\smore)"},
            {"XAUEUR", @"(xau[\s\\\/-]*eur)"},
            {"XAGUSD", @"(silver)|(xag[\s\\\/-]*usd)"},
            {"XAGEUR", @"(xag[\s\\\/-]*eur)"},
            {"EURUSD", @"(eur[\s\\\/-]*usd)"},
            {"GBPUSD", @"(gbp[\s\\\/-]*usd)"},
            {"USDJPY", @"(usd[\s\\\/-]*jpy)"},
            {"USDCAD", @"(usd[\s\\\/-]*cad)"},
            {"AUDUSD", @"(aud[\s\\\/-]*usd)"},
            {"NZDUSD", @"(nzd[\s\\\/-]*usd)"},
            {"USDCHF", @"(usd[\s\\\/-]*chf)"},
            {"GBPAUD", @"(gbp[\s\\\/-]*aud)"},
            {"EURAUD", @"(eur[\s\\\/-]*aud)"},
            {"GBPJPY", @"(gbp[\s\\\/-]*jpy)"},
            {"EURJPY", @"(eur[\s\\\/-]*jpy)"},
            {"CHFJPY", @"(chf[\s\\\/-]*jpy)"},
            {"AUDCHF", @"(aud[\s\\\/-]*chf)"},
            {"NZDCHF", @"(nzd[\s\\\/-]*chf)"},
        };

        private static readonly Dictionary<string, double> DEFAULT_TP_PIPS = new()
        {
            {"XAUUSD", 100}
        };

        private static readonly Dictionary<string, double> DEFAULT_SL_PIPS = new()
        {
            {"XAUUSD", 100}
        };

        private readonly Dictionary<long, TelegramHistoryMessage> m_Messages;
        private readonly Dictionary<long, SignalEventArgs> m_MessageSignalArgsMap;
        private readonly SortedDictionary<DateTime, TelegramHistoryMessage> m_MessagesDate;
        public ParseSetupFinder(IBarsProvider mainBarsProvider, 
            ISymbol symbol, 
            ITradeViewManager tradeViewManager,
            string signalHistoryFilePath, bool useUtc, bool useOneTp) 
            : base(mainBarsProvider, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_TradeViewManager = tradeViewManager;
            char[] trimChars = { '"', ' ' };
            m_SignalHistoryFilePath = signalHistoryFilePath.TrimStart(trimChars).TrimEnd(trimChars);
            m_UseUtc = useUtc;
            m_UseOneTp = useOneTp;
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

        protected override void CheckSetup(int index)
        {
            ProcessSetup();
        }

        private void HandleSignalAction(
            KeyValuePair<DateTime, TelegramHistoryMessage> matchedSignal, string symbolRegex)
        {
            SignalAction res = ProcessMessage(matchedSignal.Value, symbolRegex, out ParsedSignal signal);

            bool hasTp = signal.TakeProfits != null && signal.TakeProfits.Any();
            if (((res & SignalAction.ENTER_BUY) == SignalAction.ENTER_BUY ||
                 (res & SignalAction.ENTER_SELL) == SignalAction.ENTER_SELL) && hasTp)
            {
                bool isLimit = (res & SignalAction.LIMIT) == SignalAction.LIMIT;
                var args = new SignalEventArgs(
                    new BarPoint(signal.Price.GetValueOrDefault(), signal.DateTime, BarsProvider),
                    new BarPoint(signal.TakeProfits[0], LastBar, BarsProvider),
                    new BarPoint(signal.StopLoss, LastBar, BarsProvider), isLimit);
                m_MessageSignalArgsMap[matchedSignal.Value.Id] = args;
                OnEnterInvoke(args);
                return;
            }
            
            long? replyId = matchedSignal.Value.ReplyId ?? m_MessageSignalArgsMap.LastOrDefault().Key;
            if (replyId is null or <= 0)
            {
                return;
            }

            Debugger.Launch();
            SignalEventArgs refSignal = m_MessageSignalArgsMap[replyId.Value];
            if ((res & SignalAction.SET_SL) == SignalAction.SET_SL)
            {
                if (signal.Price.HasValue)
                {
                    refSignal.StopLoss =
                        new BarPoint(signal.Price.Value, LastBar, BarsProvider);
                    OnEditInvoke(refSignal);
                }
            }
            else if ((res & SignalAction.SET_TP) == SignalAction.SET_TP && hasTp)
            {
                refSignal.TakeProfit =
                    new BarPoint(signal.TakeProfits[0], LastBar, BarsProvider);
                OnEditInvoke(refSignal);
            }
            else if ((res & SignalAction.SET_BREAKEVEN) == SignalAction.SET_BREAKEVEN)
            {
                var newSl = new BarPoint(refSignal.Level.Value, LastBar, BarsProvider);
                OnBreakEvenInvoke(new LevelEventArgs(newSl, refSignal.StopLoss, true));
                refSignal.StopLoss = newSl;
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
            else if ((res & SignalAction.CANCELLED) == SignalAction.CANCELLED && refSignal.IsLimit && !refSignal.IsActive)
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
            DateTime prevBarDateTime = BarsProvider.GetOpenTime(LastBar - 1);
            DateTime barDateTime = BarsProvider.GetOpenTime(LastBar);

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
                bool isUp = value.StopLoss < value.TakeProfit;

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
                    OnStopLossInvoke(new LevelEventArgs(value.TakeProfit, value.Level, true));
                    idsToRemove.Add(messageId);
                }
            }

            foreach (long idToRemove in idsToRemove)
            {
                m_MessageSignalArgsMap.Remove(idToRemove);
            }

            List<KeyValuePair<DateTime, TelegramHistoryMessage>> matchedSignals = m_MessagesDate
                .SkipWhile(a => a.Key < prevBarDateTime)
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
            SignalAction resultAction = SignalAction.DEFAULT;

            Match signal = Regex.Match(textAll, SIGNAL_REGEX, RegexOptions.IgnoreCase);
            DateTime utcDateTime = m_UseUtc ? historyItem.Date : historyItem.Date.ToUniversalTime();
            signalOut = new ParsedSignal
            {
                DateTime = utcDateTime,
                SymbolName = Symbol.Name
            };
            double ask = m_TradeViewManager.GetAsk(Symbol);
            double bid = m_TradeViewManager.GetBid(Symbol);

            if (signal.Success && Regex.IsMatch(textAll, symbolRegex))
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

            bool isActivated = Regex.IsMatch(textAll, ACTIVATED_REGEX);
            if (isActivated)
                resultAction |= SignalAction.ACTIVATED;

            Match sl = Regex.Match(textAll, SL_REGEX, RegexOptions.IgnoreCase);
            if (sl.Success && double.TryParse(sl.Groups[1].Value,
                    NUMBER_STYLES, CultureInfo.InvariantCulture, out double slPrice))
            {
                signalOut.StopLoss = slPrice;
                resultAction |= SignalAction.SET_SL;
            }
            else
            {
                if (signal.Success)
                {
                    if (DEFAULT_SL_PIPS.TryGetValue(Symbol.Name, out double defaultStopPips))
                    {
                        signalOut.StopLoss = signalOut.Price.GetValueOrDefault() +
                                             Symbol.PipSize * defaultStopPips * (signalOut.IsLong ? -1 : 1);
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

            //if (isTrueSignal)
            //{
            //    OnEnterInvoke(new SignalEventArgs(
            //        new BarPoint(signalOut.Price ?? m_LastPrice, historyItem.Date,
            //            BarsProvider),
            //        new BarPoint(signalOut.TakeProfits[0], LastBar, BarsProvider),
            //        new BarPoint(signal.StopLoss, LastBar, BarsProvider)));
            //}

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
                m_Messages[historyItem.Id] = historyItem;
                m_MessagesDate[historyItem.Date] = historyItem;
                //ProcessMessage(historyItem);
            }
        }
    }
}
