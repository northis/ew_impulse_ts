using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Config;

namespace TradeKit
{
    public class ImpulseSignalerBaseRobot : Robot
    {
        /// <summary>
        /// Gets or sets a value indicating whether we should use the symbols list.
        /// </summary>
        [Parameter(nameof(UseSymbolsList), DefaultValue = false)]
        public bool UseSymbolsList { get; set; }

        /// <summary>
        /// Gets the symbol names.
        /// </summary>
        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,XAGUSD,XAUEUR,XAGEUR,,EURUSD,GBPUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD,AUDCAD,AUDCHF,AUDJPY,CADJPY,CADCHF,CHFJPY,EURCAD,EURCHF,EURGBP,EURAUD,EURJPY,EURNZD,GBPCAD,GBPAUD,GBPJPY,GBPNZD,GBPCHF,NZDCAD,NZDJPY")]
        public string SymbolsToProceed { get; set; }

        /// <summary>
        /// Gets or sets the telegram bot token.
        /// </summary>
        [Parameter("TelegramBotToken", DefaultValue = null)]
        public string TelegramBotToken { get; set; }

        /// <summary>
        /// Gets or sets the chat identifier where to send signals.
        /// </summary>
        [Parameter("ChatId", DefaultValue = null)]
        public string ChatId { get; set; }

        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;

        private Dictionary<string, SetupFinder> m_SetupFinders;
        private TelegramReporter m_TelegramReporter;
        private StateKeeper m_StateKeeper;
        private Dictionary<string, Bars> m_BarsMap;
        private Dictionary<string, Symbol> m_SymbolsMap;
        private Dictionary<string, bool> m_BarsInitMap;

        protected override void OnStart()
        {
            string[] symbols = !UseSymbolsList || string.IsNullOrEmpty(SymbolsToProceed)
                ? new[] {SymbolName}
                : SymbolsToProceed.Split(new[] {'|', ',', ';', ' '}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(a => Symbols.Exists(a))
                    .ToArray();

            m_StateKeeper = new StateKeeper();
            if (IsBacktesting)
            {
                m_StateKeeper.ResetState();
            }

            m_StateKeeper.Init(symbols);
            m_SetupFinders = new Dictionary<string, SetupFinder>();
            m_BarsMap = new Dictionary<string, Bars>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, Symbol>();

            foreach (string sb in symbols)
            {
                SymbolState state = m_StateKeeper.MainState.States[sb];
                state.Symbol = sb;
                state.TimeFrame = TimeFrame.Name;
                m_BarsMap[sb] = MarketData.GetBars(TimeFrame, sb);
                var barsProvider = new CTraderBarsProvider(m_BarsMap[sb]);
                var sf = new SetupFinder(Helper.PERCENT_CORRECTION_DEF, barsProvider, state);
                m_BarsMap[sb].BarOpened += BarOpened;
                m_SymbolsMap[sb] = Symbols.GetSymbol(sb);
                m_SetupFinders[sb] = sf;
                m_BarsInitMap[sb] = false;
            }
            
            m_TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId, m_StateKeeper.MainState);
        }

        private void BarOpened(BarOpenedEventArgs obj)
        {
            Bars bars = obj.Bars;
            int prevCount = bars.Count - 1;
            int index = prevCount - 1;
            if (index < 0)
            {
                return;
            }

            SetupFinder sf = m_SetupFinders[bars.SymbolName];
            if (m_BarsInitMap[bars.SymbolName])
            {
                sf.CheckBar(index);
                return;
            }

            for (int i = 0; i < prevCount; i++)
            {
                sf.CheckBar(i);
            }
            
            sf.OnEnter += OnEnter;
            sf.OnStopLoss += OnStopLoss;
            sf.OnTakeProfit += OnTakeProfit;
            sf.State.IsInSetup = false;
            m_BarsInitMap[bars.SymbolName] = true;
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            m_StopCount++;
            GetEventStrings(sender, e.Level, out string price, out SymbolInfo symbolInfo);
            Print($"SL hit! {price}");
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }

            m_TelegramReporter.ReportStopLoss(symbolInfo.Name);
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            m_TakeCount++;
            GetEventStrings(sender, e.Level, out string price, out SymbolInfo symbolInfo);
            Print($"TP hit! {price}");
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }

            m_TelegramReporter.ReportTakeProfit(symbolInfo.Name);
        }

        private void OnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            m_EnterCount++;
            GetEventStrings(sender, e.Level, out string price, out SymbolInfo symbolInfo);
            Print($"New setup found! {price}");
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }

            Symbol s = m_SymbolsMap[symbolInfo.Name];
            m_TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = s.Ask,
                Bid = s.Bid,
                Digits = symbolInfo.Digits,
                SignalEventArgs = e,
                SymbolName = symbolInfo.Name
            });
        }

        private void GetEventStrings(object sender, LevelItem level, out string price, out SymbolInfo symbolInfo)
        {
            SetupFinder sf = (SetupFinder)sender;
            symbolInfo = Symbols.GetSymbolInfo(sf.State.Symbol);
            string priceFmt = level.Price.ToString($"F{symbolInfo.Digits}", CultureInfo.InvariantCulture);
            price = $"Price:{priceFmt} ({sf.BarsProvider.GetOpenTime(level.Index):s}) - {sf.State.Symbol}";
        }

        protected override void OnStop()
        {
            foreach (SetupFinder sf in m_SetupFinders.Values)
            {
                sf.OnEnter -= OnEnter;
                sf.OnStopLoss -= OnStopLoss;
                sf.OnTakeProfit -= OnTakeProfit;
                m_BarsMap[sf.State.Symbol].BarOpened -= BarOpened;
            }

            Print($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
            if (!IsBacktesting)
            {
                m_StateKeeper.Save();
            }
        }
    }
}