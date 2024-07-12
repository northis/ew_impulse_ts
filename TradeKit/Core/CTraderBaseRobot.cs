using System;
using cAlgo.API;
using Plotly.NET;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core
{
    /// <summary>
    /// Base (ro)bot with common operations for trading
    /// </summary>
    /// <typeparam name="TR">Type of <see cref="BaseRobot{TF,TK}"/></typeparam>
    /// <typeparam name="TF">Type of <see cref="BaseSetupFinder{TK}"/></typeparam>
    /// <typeparam name="TK">The type of <see cref="SignalEventArgs"/> - what type of signals supports this bot.</typeparam>
    /// <seealso cref="Robot" />
    public abstract class CTraderBaseRobot<TR, TF, TK> : 
        Robot where TR : BaseRobot<TF, TK> where TF: BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        protected const double RISK_DEPOSIT_PERCENT = 1;
        protected const double RISK_DEPOSIT_PERCENT_MAX = 5;

        private TR m_BaseRobot;

        /// <summary>
        /// Gets the logic class for robot.
        /// </summary>
        protected abstract TR GetBaseRobot();

        #region User properties

        /// <summary>
        /// Gets or sets the risk percent from deposit (regular).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDeposit), DefaultValue = RISK_DEPOSIT_PERCENT, Group = Helper.TRADE_SETTINGS_NAME)]
        public double RiskPercentFromDeposit { get; set; }

        /// <summary>
        /// Gets or sets the risk percent from deposit (maximum).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDepositMax), DefaultValue = RISK_DEPOSIT_PERCENT_MAX, Group = Helper.TRADE_SETTINGS_NAME)]
        public double RiskPercentFromDepositMax { get; set; }

        /// <summary>
        /// Gets or sets the max allowed volume in lots.
        /// </summary>
        [Parameter(nameof(MaxVolumeLots), DefaultValue = Helper.ALLOWED_VOLUME_LOTS, MaxValue = Helper.MAX_ALLOWED_VOLUME_LOTS, MinValue = Helper.MIN_ALLOWED_VOLUME_LOTS, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxVolumeLots { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can trade.
        /// </summary>
        [Parameter(nameof(AllowToTrade), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowToTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can pass positions overnight (to the next trade day).
        /// </summary>
        [Parameter(nameof(AllowOvernightTrade), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowOvernightTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can open positions while big spread (spread/(tp-sl) ratio more than <see cref="Helper.MAX_SPREAD_RATIO"/>.
        /// </summary>
        [Parameter(nameof(AllowEnterOnBigSpread), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowEnterOnBigSpread { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should increase the volume every SL hit.
        /// </summary>
        [Parameter(nameof(UseProgressiveVolume), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseProgressiveVolume { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use the symbols list.
        /// </summary>
        [Parameter(nameof(UseSymbolsList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseSymbolsList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should use the TF list.
        /// </summary>
        [Parameter(nameof(UseTimeFramesList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseTimeFramesList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should save .png files of the charts for manual analysis.
        /// </summary>
        [Parameter(nameof(SaveChartForManualAnalysis), DefaultValue = false, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public bool SaveChartForManualAnalysis { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should post the close messages like "tp/sl hit".
        /// </summary>
        [Parameter(nameof(PostCloseMessages), DefaultValue = true, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public bool PostCloseMessages { get; set; }

        /// <summary>
        /// Gets or sets the time frames we should use.
        /// </summary>
        [Parameter(nameof(TimeFramesToProceed), DefaultValue = "Minute30,Hour", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string TimeFramesToProceed { get; set; }

        /// <summary>
        /// Gets the symbol names.
        /// </summary>
        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,XAGUSD,XAUEUR,XAGEUR,EURUSD,GBPUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD,AUDCAD,AUDCHF,AUDJPY,CADJPY,CADCHF,CHFJPY,EURCAD,EURCHF,EURGBP,EURAUD,EURJPY,EURNZD,GBPCAD,GBPAUD,GBPJPY,GBPNZD,GBPCHF,NZDCAD,NZDJPY,US 30,US TECH 100,USDNOK,USDSEK,USDDDK", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string SymbolsToProceed { get; set; }

        /// <summary>
        /// Gets or sets the telegram bot token.
        /// </summary>
        [Parameter("Telegram bot token", DefaultValue = null, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public string TelegramBotToken { get; set; }

        /// <summary>
        /// Gets or sets the chat identifier where to send signals.
        /// </summary>
        [Parameter("Chat ID", DefaultValue = null, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public string ChatId { get; set; }

        #endregion
        
        /// <summary>
        /// Called when cBot is being started. Override this method to initialize cBot, create nested indicators, etc.
        /// </summary>
        protected override void OnStart()
        {
            Logger.SetWrite(a => Print(a));
            m_BaseRobot = GetBaseRobot();

            //m_SetupFindersMap = new Dictionary<string, T>();
            //m_SetupIdBarsMap = new Dictionary<string, Bars>();
            //m_FinderIdChartBarProviderMap = new Dictionary<string, IBarsProvider>();
            //m_BarsInitMap = new Dictionary<string, bool>();
            //m_SymbolsMap = new Dictionary<string, Symbol>();
            //m_SymbolFindersMap = new Dictionary<string, T[]>();
            //m_PositionFinderMap = new Dictionary<string, List<int>>();
            //m_ChartFileFinderMap = new Dictionary<string, TK>();
            //m_MinimalBars = new HashSet<Bars>();
            //m_CurrentRisk = RiskPercentFromDeposit;

            //string[] symbols = !UseSymbolsList || string.IsNullOrEmpty(SymbolsToProceed)
            //    ? new[] {SymbolName}
            //    : SplitString(SymbolsToProceed);
            //string[] timeFrames = !UseTimeFramesList || string.IsNullOrEmpty(TimeFramesToProceed)
            //    ? new[] {TimeFrame.Name}
            //    : SplitString(TimeFramesToProceed);
            //foreach (string symbolName in symbols)
            //{
            //    if (!Symbols.Exists(symbolName))
            //    {
            //        continue;
            //    }

            //    Symbol symbolEntity = Symbols.GetSymbol(symbolName);
            //    var finders = new List<T>();
            //    foreach (string timeFrameStr in timeFrames)
            //    {
            //        if (!TimeFrame.TryParse(timeFrameStr, out TimeFrame timeFrame))
            //            continue;

            //        Bars bars = MarketData.GetBars(timeFrame, symbolName);
            //        T sf = CreateSetupFinder(bars, symbolEntity);
            //        string key = sf.Id;

            //        m_SetupIdBarsMap[key] = bars;
            //        m_SetupIdBarsMap[key].BarOpened += BarOpened;
            //        m_SymbolsMap[key] = symbolEntity;
            //        m_SetupFindersMap[key] = sf;
            //        m_BarsInitMap[key] = false;
            //        finders.Add(sf);
            //        Logger.Write($"Symbol {symbolName}, time frame {timeFrame.Name} is added");
            //    }

            //    m_SymbolFindersMap[symbolName] = finders.ToArray();
            //}


            //Logger.Write("Creating chart dictionaries...");

            //var symbolTfMap = new Dictionary<string, ITimeFrame>();
            //foreach (KeyValuePair<string, T> finder in m_SetupFindersMap)
            //{
            //    ITimeFrame chartTimeFrame = TimeFrameHelper
            //        .GetPreviousTimeFrameInfo(finder.Value.TimeFrame).TimeFrame;

            //    IBarsProvider barProvider = m_SetupFindersMap
            //        .Where(a =>
            //            a.Value.Symbol == finder.Value.Symbol &&
            //            a.Value.TimeFrame == chartTimeFrame)
            //        .Select(a => a.Value.BarsProvider)
            //        .FirstOrDefault();

            //    if (barProvider == null && 
            //        !(symbolTfMap.ContainsKey(finder.Value.Symbol) &&
            //        symbolTfMap[finder.Value.Symbol].Equals(chartTimeFrame)))
            //    {
            //        Bars bars = MarketData.GetBars(chartTimeFrame.ToTimeFrame(), finder.Value.Symbol.Name);
            //        barProvider = GetBarsProvider(bars, finder.Value.Symbol);
            //        bars.BarOpened += MinimalBarOpened;
            //        m_MinimalBars.Add(bars);
            //        symbolTfMap[finder.Value.Symbol] = chartTimeFrame;
            //    }

            //    m_FinderIdChartBarProviderMap[finder.Key] = barProvider;
            //}

            //if (SaveChartForManualAnalysis)
            //{
            //    Logger.Write($"Your charts will be in this folder: {Helper.DirectoryToSaveResults}");
            //}

            //Positions.Closed += OnPositionsClosed;

            //Dictionary<string, int> stateMap = LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
            //TelegramReporter = new TelegramReporter(
            //    TelegramBotToken, ChatId, PostCloseMessages, stateMap, OnReportStateSave);
            //Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }

        /// <summary>
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="lastOpenDateTime">The last open date time.</param>
        protected virtual GenericChart.GenericChart[] GetAdditionalChartLayers(
            TK signalEventArgs, DateTime lastOpenDateTime)
        {
            return null;
        }

        /// <summary>
        /// Called when cBot is stopped.
        /// </summary>
        protected override void OnStop()
        {
            base.OnStop();
            //foreach (T sf in m_SetupFindersMap.Values)
            //{
            //    sf.OnEnter -= OnEnter;
            //    sf.OnStopLoss -= OnStopLoss;
            //    sf.OnTakeProfit -= OnTakeProfit;
            //    sf.OnTakeProfit -= OnBreakeven;
            //    m_SetupIdBarsMap[sf.Id].BarOpened -= BarOpened;
            //    IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
            //    bp.Dispose();
            //}

            //foreach (Bars minimalBar in m_MinimalBars)
            //{
            //    minimalBar.BarOpened -= MinimalBarOpened;
            //}

            //Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}
