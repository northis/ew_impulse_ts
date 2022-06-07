using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.Config;

namespace cAlgo
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
        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,EURUSD,GBPUSD,XAGUSD")]
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


        private Dictionary<string, SetupFinder> m_SetupFinders;
        private TelegramReporter m_TelegramReporter;
        private StateKeeper m_StateKeeper;
        private Dictionary<string, Bars> m_BarsMap;
        private Dictionary<string, bool> m_BarsInitMap;

        protected override void OnStart()
        {
            string[] symbols = !UseSymbolsList || string.IsNullOrEmpty(SymbolsToProceed)
                ? new[] {SymbolName}
                : SymbolsToProceed.Split(new[] {'|', ',', ';', ' '}, StringSplitOptions.RemoveEmptyEntries)
                    .Where(a => Symbols.Exists(a))
                    .ToArray();
            
            m_StateKeeper = new StateKeeper();
            m_StateKeeper.Init(symbols);
            m_SetupFinders = new Dictionary<string, SetupFinder>();
            m_BarsMap = new Dictionary<string, Bars>();
            m_BarsInitMap = new Dictionary<string, bool>();

            foreach (string sb in symbols)
            {
                SymbolState state = m_StateKeeper.MainState.States[sb];
                state.Symbol = sb;
                state.TimeFrame = TimeFrame.Name;
                var barsProvider = new CTraderBarsProvider(Bars, MarketData);
                var sf = new SetupFinder(
                    Helper.PERCENT_CORRECTION_DEF, barsProvider, state);
                m_BarsMap[sb] = MarketData.GetBars(TimeFrame, sb);
                m_BarsMap[sb].BarOpened += BarOpened;
                m_SetupFinders[sb] = sf;
                m_BarsInitMap[sb] = false;
            }
            
            m_TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId, m_StateKeeper.MainState);
        }

        private void BarOpened(BarOpenedEventArgs obj)
        {
            int index = obj.Bars.Count - 1;
            if (index < 0)
            {
                return;
            }

            SetupFinder sf = m_SetupFinders[obj.Bars.SymbolName];
            if (m_BarsInitMap[obj.Bars.SymbolName])
            {
                m_SetupFinders[obj.Bars.SymbolName].CheckSetup(index);
                return;
            }

            for (int i = 0; i < obj.Bars.Count; i++)
            {
                sf.CheckSetup(i);
            }

            sf.OnEnter += OnEnter;
            sf.OnStopLoss += OnStopLoss;
            sf.OnTakeProfit += OnTakeProfit;
            sf.State.IsInSetup = false;
            m_BarsInitMap[obj.Bars.SymbolName] = true;
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
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
            GetEventStrings(sender, e.Level, out string price, out SymbolInfo symbolInfo);
            Print($"New setup found! {price}");
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }

            m_TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = Ask,
                Bid = Bid,
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
            price = $"Price:{priceFmt} ({sf.BarsProvider.GetOpenTime(level.Index):s})";
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

            m_StateKeeper.Save();
        }
    }
}