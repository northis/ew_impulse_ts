﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using cAlgo.API.Internals;
using Newtonsoft.Json;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Telegram;

namespace TradeKit.Signals
{
    public class ParseSetupFinder : BaseSetupFinder<SignalEventArgs>
    {
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly string m_SignalHistoryFilePath;
        private readonly bool m_UseUtc;
        private readonly bool m_UseOneTp;
        private const string SIGNAL_REGEX = @"(b[a|u]y|sel[l]?)[\D]*([0-9.,]{1,10})";
        private const string TP_REGEX = @"t(ake\s)?p(rofit)?\d?[\D]*([0-9.,]{1,10})";
        private const string SL_REGEX = @"s(top\s)?[l|t](oss)?[\D]*([0-9.,]{1,10})";

        private static readonly NumberStyles NUMBER_STYLES = NumberStyles.AllowCurrencySymbol
                                                             | NumberStyles.AllowDecimalPoint
                                                             | NumberStyles.AllowLeadingWhite
                                                             | NumberStyles.AllowTrailingWhite
                                                             | NumberStyles.AllowThousands;

        private static readonly Dictionary<string, string> SYMBOL_REGEX_MAP = new()
        {
            {"XAUUSD", @"(gold)|(xau[\s\\\/-]*usd)"},
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

        private readonly Dictionary<DateTime, ParsedSignal> m_Signals;
        public ParseSetupFinder(IBarsProvider mainBarsProvider, 
            SymbolState state, Symbol symbol, 
            string signalHistoryFilePath, bool useUtc, bool useOneTp) 
            : base(mainBarsProvider, state, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_SignalHistoryFilePath = signalHistoryFilePath;
            m_UseUtc = useUtc;
            m_UseOneTp = useOneTp;
            m_Signals = ParseSignals();
        }

        private int m_LastBar;
        private double m_LastPrice;

        /// <summary>
        /// Gets the last entry.
        /// </summary>
        public LevelItem LastEntry { get; private set; }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="!:index" />.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public override void CheckBar(int index)
        {
            m_LastBar = index;
            m_LastPrice = m_MainBarsProvider.GetClosePrice(index);
            ProcessSetup();
        }

        public override void CheckTick(double bid)
        {
            m_LastPrice = bid;
            if (m_LastBar == 0)
            {
                return;
            }

            ProcessSetup();
        }

        /// <summary>
        /// Finds or closes the trade setup.
        /// </summary>
        private void ProcessSetup()
        {
            DateTime prevBarDateTime = BarsProvider.GetOpenTime(m_LastBar - 1);
            DateTime barDateTime = BarsProvider.GetOpenTime(m_LastBar);

            List<KeyValuePair<DateTime, ParsedSignal>> matchedSignals = m_Signals
                .SkipWhile(a => a.Key < prevBarDateTime)
                .TakeWhile(a => a.Key <= barDateTime)
                .ToList();
            
            foreach (KeyValuePair<DateTime, ParsedSignal> matchedSignal in matchedSignals)
            {
                ParsedSignal signal = matchedSignal.Value;

                LastEntry = new LevelItem(m_LastPrice, m_LastBar);

                for (int i = 0; i < signal.TakeProfits.Length; i++)
                {
                    if (m_UseOneTp && i > 0)
                    {
                        break;
                    }

                    double tp = signal.TakeProfits[i];

                    State.IsInSetup = true;
                    OnEnterInvoke(new SignalEventArgs(
                        LastEntry,
                        new LevelItem(tp),
                        new LevelItem(signal.StopLoss)));
                }

                m_Signals.Remove(matchedSignal.Key);
            }
        }

        /// <summary>
        /// Parses the signals.
        /// </summary>
        /// <returns>Date-signal map</returns>
        public Dictionary<DateTime, ParsedSignal> ParseSignals()
        {
            var res = new Dictionary<DateTime, ParsedSignal>();
            if (!SYMBOL_REGEX_MAP.TryGetValue(State.Symbol, out string symbolRegex))
            {
                return res;
            }

            string text = File.ReadAllText(m_SignalHistoryFilePath);
            TelegramHistorySignal[]? history = null;
            try
            {
                history = JsonConvert.DeserializeObject<TelegramExportJson>(text)?.Messages;
            }
            catch (Exception)
            {
                ;
            }

            if (history == null)
            {
                try
                {
                    history = JsonConvert.DeserializeObject<TelegramHistorySignal[]>(text);
                }
                catch (Exception)
                {
                    ;
                }
            }

            if (history == null)
            {
                return res;
            }
            
            foreach (TelegramHistorySignal historyItem in history)
            {
                string textAll = string.Concat(historyItem.Text);

                if (!Regex.IsMatch(textAll, symbolRegex, RegexOptions.IgnoreCase))
                {
                    continue;
                }

                Match signal = Regex.Match(textAll, SIGNAL_REGEX, RegexOptions.IgnoreCase);
                DateTime utcDateTime = m_UseUtc ? historyItem.Date : historyItem.Date.ToUniversalTime();
                var signalOut = new ParsedSignal { DateTime = utcDateTime, SymbolName = State.Symbol };
                bool isTrueSignal = false;

                if (signal.Success)
                {
                    if (double.TryParse(signal.Groups[2].Value,
                            NUMBER_STYLES, CultureInfo.InvariantCulture, out double enterPrice))
                        signalOut.Price = enterPrice;

                    signalOut.IsLong = signal.Groups[1].Value?.ToLowerInvariant() == "buy";
                    isTrueSignal = true;
                }

                Match sl = Regex.Match(textAll, SL_REGEX, RegexOptions.IgnoreCase);
                if (!sl.Success)
                {
                    continue;
                }

                if (!double.TryParse(sl.Groups[3].Value,
                        NUMBER_STYLES, CultureInfo.InvariantCulture, out double slPrice))
                {
                    continue;
                }

                signalOut.StopLoss = slPrice;

                var tpsCollection = Regex.Matches(textAll, TP_REGEX, RegexOptions.IgnoreCase);
                if (tpsCollection.Count == 0)
                {
                    continue;
                }

                var tpList = new List<double>();
                foreach (Match? match in tpsCollection)
                {
                    if (match == null || !double.TryParse(match.Groups[3].Value,
                            NUMBER_STYLES, CultureInfo.InvariantCulture, out double tpPrice))
                        continue;

                    tpList.Add(tpPrice);
                }

                signalOut.TakeProfits = tpList.ToArray();

                if (!isTrueSignal)
                {
                    signalOut.IsLong = tpList[0] > slPrice;
                }

                res[signalOut.DateTime] = signalOut;
            }

            return res;
        }
    }
}