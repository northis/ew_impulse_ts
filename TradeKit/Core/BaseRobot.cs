﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Internals;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.EventArgs;
using TradeKit.Telegram;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;
using Shape = Plotly.NET.LayoutObjects.Shape;

namespace TradeKit.Core
{
    /// <summary>
    /// Base (ro)bot with common operations for trading
    /// </summary>
    /// <typeparam name="T">Type of <see cref="BaseSetupFinder{TK}"/></typeparam>
    /// <typeparam name="TK">The type of <see cref="SignalEventArgs"/> - what type of signals supports this bot.</typeparam>
    /// <seealso cref="Robot" />
    public abstract class BaseRobot<T,TK> : 
        Robot where T: BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        protected const double RISK_DEPOSIT_PERCENT = 1;
        protected const double RISK_DEPOSIT_PERCENT_MAX = 5;
        protected const double SPREAD_MARGIN_RATIO = 1.1;
        protected const int CHART_BARS_MARGIN_COUNT = 5;
        protected const double CHART_FONT_HEADER = 36;
        protected const int CHART_HEIGHT = 1000;
        protected const int CHART_WIDTH = 1000;
        private const int SETUP_MIN_WIDTH = 3;
        protected const string ZERO_CHART_FILE_POSTFIX = "img.00";
        protected const string FIRST_CHART_FILE_POSTFIX = "img.01";
        protected const string STATE_SAVE_KEY = "ReportStateMap";
        private double m_CurrentRisk = RISK_DEPOSIT_PERCENT;
        protected TelegramReporter TelegramReporter;
        private Dictionary<string, T> m_SetupFindersMap;
        private Dictionary<string, T[]> m_SymbolFindersMap;
        private Dictionary<string, Bars> m_SetupIdBarsMap;
        private HashSet<Bars> m_MinimalBars;
        private Dictionary<string, IBarsProvider> m_FinderIdChartBarProviderMap;
        private Dictionary<string, Symbol> m_SymbolsMap;
        private Dictionary<string, bool> m_BarsInitMap;
        private Dictionary<string, List<int>> m_PositionFinderMap;
        private Dictionary<string, TK> m_ChartFileFinderMap;
        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;

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
        /// Gets the name of the bot.
        /// </summary>
        public abstract string GetBotName();
        
        /// <summary>
        /// Gets the get current risk.
        /// </summary>
        protected double GetCurrentRisk
        {
            get
            {
                if (UseProgressiveVolume)
                {
                    return m_CurrentRisk;
                }

                return RiskPercentFromDeposit;
            }
        }

        /// <summary>
        /// Ups the risk.
        /// </summary>
        protected void UpRisk()
        {
            if (m_CurrentRisk >= RiskPercentFromDepositMax)
            {
                m_CurrentRisk = RiskPercentFromDepositMax;
                return;
            }
            
            m_CurrentRisk += RiskPercentFromDeposit;
        }

        /// <summary>
        /// Downs the risk.
        /// </summary>
        protected void DownRisk()
        {
            if (m_CurrentRisk <= RiskPercentFromDeposit)
            {
                m_CurrentRisk = RiskPercentFromDeposit;
                return;
            }

            m_CurrentRisk -= RiskPercentFromDeposit;
        }

        private string[] SplitString(string str)
        {
            return str.Split(new[] { '|', ',', ';', }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Called when cBot is being started. Override this method to initialize cBot, create nested indicators, etc.
        /// </summary>
        /// <example>
        ///   <code>
        /// protected override void OnStart()
        /// {
        /// //This method is invoked when the cBot is started.
        /// }
        /// </code>
        /// </example>
        /// <signature>
        ///   <code>public void OnStart()</code>
        /// </signature>
        protected override void OnStart()
        {
            Logger.SetWrite(a => Print(a));
            m_SetupFindersMap = new Dictionary<string, T>();
            m_SetupIdBarsMap = new Dictionary<string, Bars>();
            m_FinderIdChartBarProviderMap = new Dictionary<string, IBarsProvider>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, Symbol>();
            m_SymbolFindersMap = new Dictionary<string, T[]>();
            m_PositionFinderMap = new Dictionary<string, List<int>>();
            m_ChartFileFinderMap = new Dictionary<string, TK>();
            m_MinimalBars = new HashSet<Bars>();
            m_CurrentRisk = RiskPercentFromDeposit;

            string[] symbols = !UseSymbolsList || string.IsNullOrEmpty(SymbolsToProceed)
                ? new[] {SymbolName}
                : SplitString(SymbolsToProceed);
            string[] timeFrames = !UseTimeFramesList || string.IsNullOrEmpty(TimeFramesToProceed)
                ? new[] {TimeFrame.Name}
                : SplitString(TimeFramesToProceed);
            foreach (string symbolName in symbols)
            {
                if (!Symbols.Exists(symbolName))
                {
                    continue;
                }

                Symbol symbolEntity = Symbols.GetSymbol(symbolName);
                var finders = new List<T>();
                foreach (string timeFrameStr in timeFrames)
                {
                    if (!TimeFrame.TryParse(timeFrameStr, out TimeFrame timeFrame))
                        continue;

                    Bars bars = MarketData.GetBars(timeFrame, symbolName);
                    T sf = CreateSetupFinder(bars, symbolEntity);
                    string key = sf.Id;

                    m_SetupIdBarsMap[key] = bars;
                    m_SetupIdBarsMap[key].BarOpened += BarOpened;
                    m_SymbolsMap[key] = symbolEntity;
                    m_SetupFindersMap[key] = sf;
                    m_BarsInitMap[key] = false;
                    finders.Add(sf);
                    Logger.Write($"Symbol {symbolName}, time frame {timeFrame.Name} is added");
                }
                
                m_SymbolFindersMap[symbolName] = finders.ToArray();
            }


            Logger.Write("Creating chart dictionaries...");

            var symbolTfMap = new Dictionary<Symbol, TimeFrame>();
            foreach (KeyValuePair<string, T> finder in m_SetupFindersMap)
            {
                TimeFrame chartTimeFrame = TimeFrameHelper
                    .GetPreviousTimeFrameInfo(finder.Value.TimeFrame).TimeFrame;

                IBarsProvider barProvider = m_SetupFindersMap
                    .Where(a =>
                        a.Value.Symbol == finder.Value.Symbol &&
                        a.Value.TimeFrame == chartTimeFrame)
                    .Select(a => a.Value.BarsProvider)
                    .FirstOrDefault();

                if (barProvider == null && 
                    !(symbolTfMap.ContainsKey(finder.Value.Symbol) &&
                    symbolTfMap[finder.Value.Symbol].Equals(chartTimeFrame)))
                {
                    Bars bars = MarketData.GetBars(chartTimeFrame, finder.Value.Symbol.Name);
                    barProvider = GetBarsProvider(bars, finder.Value.Symbol);
                    bars.BarOpened += MinimalBarOpened;
                    m_MinimalBars.Add(bars);
                    symbolTfMap[finder.Value.Symbol] = chartTimeFrame;
                }

                m_FinderIdChartBarProviderMap[finder.Key] = barProvider;
            }

            if (SaveChartForManualAnalysis)
            {
                Logger.Write($"Your charts will be in this folder: {Helper.DirectoryToSaveResults}");
            }

            Positions.Closed += OnPositionsClosed;

            Dictionary<string, int> stateMap = LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
            TelegramReporter = new TelegramReporter(
                TelegramBotToken, ChatId, PostCloseMessages, stateMap, OnReportStateSave);
            Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }
        
        private void MinimalBarOpened(BarOpenedEventArgs obj)
        {
            try
            {
                if (!m_SymbolFindersMap.TryGetValue(obj.Bars.SymbolName, out T[] finders))
                {
                    return;
                }
                
                foreach (T sf in finders)
                {
                    if (!m_BarsInitMap[sf.Id])
                    {
                        continue;
                    }

                    sf.CheckTick(obj.Bars.LastBar.Close);
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(MinimalBarOpened)} error: {ex.Message} {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Called when on saving the report state.
        /// </summary>
        /// <param name="stateMap">The state map (signal id-post Id).</param>
        protected void OnReportStateSave(Dictionary<string, int> stateMap)
        {
            LocalStorage.SetObject(STATE_SAVE_KEY, stateMap, LocalStorageScope.Device);
        }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected abstract IBarsProvider GetBarsProvider(Bars bars, Symbol symbolEntity);

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected abstract T CreateSetupFinder(Bars bars, Symbol symbolEntity);

        private void OnPositionsClosed(PositionClosedEventArgs obj)
        {
            if (obj.Reason == PositionCloseReason.StopLoss)
            {
                UpRisk();
                return;
            }

            if (obj.Reason == PositionCloseReason.TakeProfit)
            {
                DownRisk();
            }
        }

        private void BarOpened(BarOpenedEventArgs obj)
        {
            try
            {
                Bars bars = obj.Bars;
                int prevCount = bars.Count - 1;
                int index = prevCount - 1;
                if (index < 0)
                {
                    return;
                }

                string finderId = BaseSetupFinder<TK>.GetId(bars.SymbolName, bars.TimeFrame.Name);
                if (!m_SetupFindersMap.TryGetValue(finderId, out T sf))
                {
                    return;
                }

                if (m_BarsInitMap[finderId])
                {
                    //Logger.Write($"{nameof(BarOpened)}: {obj.Bars.SymbolName} {obj.Bars.TimeFrame.ShortName}");
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
                sf.OnBreakEven += OnBreakeven;
                //sf.IsInSetup = false; //TODO
                m_BarsInitMap[finderId] = true;
                Logger.Write($"{nameof(BarOpened)}: Bars initialized - {obj.Bars.SymbolName} {obj.Bars.TimeFrame.ShortName}");
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(BarOpened)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes or sets the breakeven for the symbol positions.
        /// </summary>
        /// <param name="setupId">Id of the setup finder.</param>
        /// <param name="posId">Identity args to find the position</param>
        /// <param name="breakEvenPrice">If not null, sets this price as a breakeven</param>
        private void ModifySymbolPositions(string setupId, string posId, double? breakEvenPrice = null)
        {
            if (!m_PositionFinderMap.TryGetValue(setupId, out List<int> posIds)
                || posIds == null || posIds.Count == 0)
                return;

            Position[] positionsToModify = Positions
                .Where(a => posIds.Contains(a.Id) && a.Comment == posId)
                .ToArray();

            foreach (Position position in positionsToModify)
            {
                if (breakEvenPrice.HasValue)
                    position.ModifyStopLossPrice(breakEvenPrice.Value);
                else
                {
                    posIds.Remove(position.Id);
                    position.Close();
                }

                break;
            }
        }

        /// <summary>
        /// Handles the close.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        /// <param name="price">The price.</param>
        /// <param name="setupId">The setup identifier.</param>
        /// <param name="positionId">The position identifier.</param>
        /// <returns>True, if the positions exist</returns>
        protected void HandleClose(object sender, LevelEventArgs e,
            out string price, out string setupId, out string positionId)
        {
            T sf = (T)sender;
            setupId = sf.Id;
            GetEventStrings(sender, e.Level, out price);
            positionId = Helper.GetPositionId(setupId, e.FromLevel);
        }
        
        protected void OnStopLoss(object sender, LevelEventArgs e)
        {
            HandleClose(sender, e, out string price, out string setupId, out string positionId);
            m_StopCount++;
            Logger.Write($"SL hit! {price}");
            ModifySymbolPositions(setupId, positionId);
            ShowResultChart(sender, false);

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportStopLoss(positionId);
        }

        /// <summary>
        /// Generates the second result chart for the setup finder
        /// </summary>
        /// <param name="setupFinder">The setup finder we want to check. See <see cref="T"/></param>
        /// <param name="successTrade">True - TP hit, False - SL hit</param>
        private void ShowResultChart(object setupFinder, bool successTrade)
        {
            if (setupFinder is not T sf ||
                !m_ChartFileFinderMap.TryGetValue(sf.Id, out TK signalEventArgs))
            {
                return;
            }

            if (IsBacktesting)
                OnResultForManualAnalysis(signalEventArgs, sf, successTrade);

            if (!SaveChartForManualAnalysis)
                return;

            IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
            GeneratePlotImageFile(bp, signalEventArgs, true, successTrade);
            m_ChartFileFinderMap.Remove(sf.Id);
        }

        /// <summary>
        /// Called when take profit is hit
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnTakeProfit(object sender, LevelEventArgs e)
        {
            HandleClose(sender, e, out string price, out string setupId, out string positionId);
            m_TakeCount++;
            Logger.Write($"TP hit! {price}");
            ModifySymbolPositions(setupId, positionId);
            ShowResultChart(sender, true);

            if (!TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportTakeProfit(positionId);
        }

        /// <summary>
        /// Called when breakeven is hit
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnBreakeven(object sender, LevelEventArgs e)
        {
            HandleClose(sender, e, out string price, out string setupId, out string positionId);
            Logger.Write($"Breakeven is set! {price}");
            ModifySymbolPositions(setupId, positionId, e.Level.Value);

            if (!TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportBreakeven(positionId);
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="TK"/> instance containing the event data.</param>
        /// <returns>
        ///   <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected abstract bool HasSameSetupActive(T setupFinder, TK signal);

        /// <summary>
        /// Determines whether the interval has a trade break inside.
        /// </summary>
        /// <param name="dateStart">The interval start.</param>
        /// <param name="dateEnd">The interval end.</param>
        /// <param name="symbol">The symbol to check the interval against.</param>
        /// <returns>
        ///   <c>true</c> if the interval has a trade break inside; otherwise, <c>false</c>.
        /// </returns>
        protected static bool HasTradeBreakInside(
            DateTime dateStart, DateTime dateEnd, Symbol symbol)
        {
            IReadonlyList<TradingSession> sessions = symbol.MarketHours.Sessions;
            TimeSpan safeTimeDurationStart = TimeSpan.FromHours(1);

            DateTime setupDayStart = dateStart
                .Subtract(dateStart.TimeOfDay)
                .AddDays(-(int)dateStart.DayOfWeek);
            bool isSetupInDay = !sessions.Any();
            foreach (TradingSession session in sessions)
            {
                DateTime sessionDateTime = setupDayStart
                    .AddDays((int)session.StartDay)
                    .Add(session.StartTime)
                    .Add(safeTimeDurationStart);
                DateTime sessionEndTime = setupDayStart
                    .AddDays((int)session.EndDay)
                    .Add(session.EndTime)
                    .Add(-safeTimeDurationStart);

                if (dateStart > sessionDateTime && dateEnd < sessionEndTime)
                {
                    isSetupInDay = true;
                    break;
                }
            }

            return !isSetupInDay;
        }

        /// <summary>
        /// Determines whether <see cref="signal"/> and <see cref="setupFinder"/> can contain an overnight signal.
        /// </summary>
        /// <param name="signal">The signal.</param>
        /// <param name="setupFinder">The setup finder.</param>
        protected virtual bool IsOvernightTrade(TK signal, T setupFinder)
        {
            return false;
        }

        private void OnEnter(object sender, TK e)
        { 
            var sf = (T)sender;
            if (!m_SymbolFindersMap.TryGetValue(sf.Symbol.Name, out T[] finders))
            {
                return;
            }
            
            if (!m_SetupIdBarsMap.TryGetValue(sf.Id, out _))
            {
                return;
            }
            
            Symbol symbol = m_SymbolsMap[sf.Id];

            double tp = e.TakeProfit.Value;
            double sl = e.StopLoss.Value;
            bool isLong = sl < tp;
            double spread = symbol.Spread;

            if (isLong && symbol.Ask >= tp ||
                spread > 0 && Math.Abs(sl - tp) / spread < Helper.MAX_SPREAD_RATIO)
            {
                Logger.Write("Big spread, ignore the signal");

                if (!AllowEnterOnBigSpread)
                    return;
            }

            if (!AllowOvernightTrade && IsOvernightTrade(e, sf))
            {
                return;
            }

            foreach (T finder in finders)
            {
                if (finder.Id == sf.Id)
                {
                    continue;
                }

                if (HasSameSetupActive(finder, e))
                {
                    Logger.Write($"Already got this setup in on {finder.Symbol.Name} - {finder.TimeFrame}");
                    return;
                }
            }

            m_EnterCount++;

            if (!m_PositionFinderMap.ContainsKey(sf.Id))
                m_PositionFinderMap[sf.Id] = new List<int>();

            GetEventStrings(sender, e.Level, out string price);
            Logger.Write($"New setup found! {price}");
            Symbol s = m_SymbolsMap[sf.Id];
            double priceNow = isLong ? s.Ask : s.Bid;
            if (!isLong)
            {
                sl += s.Spread * SPREAD_MARGIN_RATIO;
                tp += s.Spread * SPREAD_MARGIN_RATIO;
            }

            double slLen = Math.Abs(priceNow - sl);
            double tpLen = Math.Abs(priceNow - tp);

            //isLong = !isLong;
            //(sl, tp) = (tp, sl);
            TradeType type = isLong ? TradeType.Buy : TradeType.Sell;

            double slP = Math.Round(slLen / sf.Symbol.PipSize);
            double tpP = Math.Round(tpLen / sf.Symbol.PipSize);

            if (slP > 0)
            {
                //System.Diagnostics.Debugger.Launch();
                double volume = GetVolume(symbol, Math.Max(tpP, slP));
                double volumeInLots = volume / symbol.LotSize;

                if (volumeInLots > MaxVolumeLots)
                {
                    Logger.Write(
                        $"The calculated volume is too big - {volumeInLots:F2}; max value is {MaxVolumeLots:F2} lots");
                    return;
                }

                if (IsBacktesting || AllowToTrade)
                {
                    TradeResult order = ExecuteMarketOrder(
                        type, sf.Symbol.Name, volume, GetBotName(), slP, tpP,
                        Helper.GetPositionId(sf.Id, e.Level));

                    if (order?.IsSuccessful == true)
                    {
                        order.Position.ModifyTakeProfitPrice(tp);
                        order.Position.ModifyStopLossPrice(sl);
                        m_PositionFinderMap[sf.Id].Add(order.Position.Id);
                    }
                }
            }
            
            //if (IsBacktesting && !SaveChartForManualAnalysis)
            //{
            //    return;
            //}
            
            Directory.CreateDirectory(Helper.DirectoryToSaveResults);
            //if (!SaveChartForManualAnalysis)
            //{
            //    foreach (string file in Directory.GetFiles(Helper.DirectoryToSaveResults))
            //    {
            //        File.Delete(file);
            //    }
            //}

            if (SaveChartForManualAnalysis || IsBacktesting) m_ChartFileFinderMap[sf.Id] = e;
            if (!TelegramReporter.IsReady)
                return;

            IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
            string plotImagePath = GeneratePlotImageFile(bp, e);
            TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = s.Ask,
                Bid = s.Bid,
                Digits = sf.Symbol.Digits,
                SignalEventArgs = e,
                SymbolName = sf.Symbol.Name,
                SenderId = sf.Id,
                PlotImagePath = plotImagePath
            });
        }

        /// <summary>
        /// Can be used for drawing something on the chart.
        /// </summary>
        /// <param name="candlestickChart">The main chart with candles.</param>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="chartDateTimes">Date times for bars got from the broker.</param>
        protected virtual void OnDrawChart(
            GenericChart.GenericChart candlestickChart, 
            TK signalEventArgs, 
            IBarsProvider barProvider, 
            List<DateTime> chartDateTimes)
        {
        }

        /// <summary>
        /// Can be used for additional logic for manual analysis.
        /// </summary>
        /// <param name="chartDataSource">The main chart data.</param>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="dirPath">Path to save extra chart data</param>
        /// <param name="tradeResult">True - TP hit, false - SL hit.</param>
        /// <param name="rangebreaks">Ranges of dates where we shouldn't draw the data</param>
        protected virtual void OnSaveRawChartDataForManualAnalysis(
            ChartDataSource chartDataSource,
            TK signalEventArgs,
            IBarsProvider barProvider,
            string dirPath,
            bool tradeResult,
            Rangebreak[] rangebreaks = null)
        {
        }

        /// <summary>
        /// Occurs on the <see cref="E:ResultForManualAnalysis" /> event.
        /// </summary>
        /// <param name="signalEventArgs">The <see cref="GartleySignalEventArgs"/> instance containing the event data.</param>
        /// <param name="setupFinder">The setup finder this trade relates to</param>
        /// <param name="tradeResult"><c>true</c> for TP hit, otherwise <c>false</c>.</param>
        protected virtual void OnResultForManualAnalysis(
            TK signalEventArgs,
            T setupFinder,
            bool tradeResult)
        {

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
        /// Gets the path to the generated chart image file.
        /// </summary>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="signalEventArgs">Signal info args</param>
        /// <param name="showTradeResult">True if we want to see the result of the first trade.</param>
        /// <param name="successTrade">Null - no result yet, true - TP hit, false - SL hit.</param>
        /// <returns>Path to image file</returns>
        protected string GeneratePlotImageFile(
            IBarsProvider barProvider, 
            TK signalEventArgs, 
            bool showTradeResult = false,
            bool? successTrade = null)
        {
            DateTime startView = signalEventArgs.StartViewBarTime;
            int firstIndex = barProvider.GetIndexByTime(signalEventArgs.StartViewBarTime);
            int earlyBar = Math.Max(0, firstIndex - CHART_BARS_MARGIN_COUNT);

            int lastIndex = barProvider.Count - 1;
            int barsCount = lastIndex - earlyBar + 1;
            if (barsCount <= 0)
            {
                return null;
            }
            
            bool useCommonTimeFrame = TimeFrameHelper.TimeFrames
                .TryGetValue(barProvider.TimeFrame, out TimeFrameInfo timeFrameInfo);

            if (!useCommonTimeFrame)
                throw new NotSupportedException($"We don't support {barProvider.TimeFrame.Name} time frame");

            var s = new ChartDataSource(earlyBar, barsCount);
            var rangeBreaks = new List<DateTime>();
            var validDateTimes = new List<DateTime>();

            for (int i = earlyBar; i <= lastIndex; i++)
            {
                int barIndex = i - earlyBar;
                DateTime currentDateTime = barProvider.GetOpenTime(i);
                s.O[barIndex] = barProvider.GetOpenPrice(i);
                s.H[barIndex] = barProvider.GetHighPrice(i);
                s.L[barIndex] = barProvider.GetLowPrice(i);
                s.C[barIndex] = barProvider.GetClosePrice(i);
                s.D[barIndex] = currentDateTime;

                if (i == earlyBar)
                {
                    continue;
                }

                DateTime prevDateTime = barProvider.GetOpenTime(i - 1);
                TimeSpan diffToPrevious = currentDateTime - prevDateTime;
                if (i != firstIndex && diffToPrevious > timeFrameInfo.TimeSpan)
                {
                    while (currentDateTime >= prevDateTime)
                    {
                        prevDateTime = prevDateTime.Add(timeFrameInfo.TimeSpan);
                        rangeBreaks.Add(prevDateTime);
                    }
                }
                else
                {
                    validDateTimes.Add(currentDateTime);
                }
            }

            DateTime lastOpenDateTime = s.D[^1];
            DateTime lastCloseDateTime = lastOpenDateTime;

            GenericChart.GenericChart candlestickChart = ChartGenerator.GetCandlestickChart(
                s.O, s.H, s.L, s.C, s.D, barProvider.Symbol.Name, rangeBreaks, timeFrameInfo.TimeSpan,
                out Rangebreak[] rbs);

            OnDrawChart(candlestickChart, signalEventArgs, barProvider, validDateTimes);
            GenericChart.GenericChart[] layers =
                GetAdditionalChartLayers(signalEventArgs, lastCloseDateTime)
                ?? Array.Empty<GenericChart.GenericChart>();

            GenericChart.GenericChart resultChart = Plotly.NET.Chart.Combine(
                    layers.Concat(new[] { candlestickChart }))
                .WithTitle(
                    $@"{barProvider.Symbol.Name} {barProvider.TimeFrame.ShortName} {lastCloseDateTime.ToUniversalTime():R} ",
                    Font.init(Size: CHART_FONT_HEADER));

            string fileName = startView.ToString("s").Replace(":", "-");
            string dirPath = Path.Combine(Helper.DirectoryToSaveResults,
                $"{fileName}.{barProvider.Symbol.Name}.{barProvider.TimeFrame.ShortName}");
            Directory.CreateDirectory(dirPath);

            string imageName;
            if (SaveChartForManualAnalysis)
            {
                if (showTradeResult)
                {
                    OnSaveRawChartDataForManualAnalysis(
                        s, signalEventArgs, barProvider, dirPath, successTrade.GetValueOrDefault(), rbs);
                    imageName = Helper.MAIN_IMG_FILE_NAME;
                }
                else
                {
                    imageName = FIRST_CHART_FILE_POSTFIX;
                }
            }
            else
            {
                imageName = ZERO_CHART_FILE_POSTFIX;
            }

            string filePath = Path.Combine(dirPath, imageName);
            resultChart.SavePNG(filePath, null, CHART_WIDTH, CHART_HEIGHT);
            return $"{filePath}{Helper.CHART_FILE_TYPE_EXTENSION}";
        }

        /// <summary>
        /// Gets the volume.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="slPoints">The sl points.</param>
        protected virtual double GetVolume(Symbol symbol, double slPoints)
        {
            double volume = symbol.GetVolume(GetCurrentRisk, Account.Balance, slPoints);
            return volume;
        }

        private void GetEventStrings(object sender, BarPoint level, out string price)
        {
            var sf = (T)sender;
            string priceFmt = level.Value.ToString($"F{sf.Symbol.Digits}", CultureInfo.InvariantCulture);
            price = $"Price:{priceFmt} ({level.OpenTime:s}) - {sf.Symbol.Name}";
        }

        protected Shape GetLine(BarPoint bp1, BarPoint bp2, Color color, double width = 1)
        {
            Shape line = Shape.init(StyleParam.ShapeType.Line.ToFSharp(),
                X0: bp1.OpenTime.ToFSharp(),
                Y0: bp1.Value.ToFSharp(),
                X1: bp2.OpenTime.ToFSharp(),
                Y1: bp2.Value.ToFSharp(),
                Fillcolor: color.ToFSharp(),
                Line: Line.init(Color: color, Width: width.ToFSharp()));
            return line;
        }

        protected DateTime GetMedianDate(DateTime start, DateTime end, List<DateTime> chartDateTimes)
        {
            if (start == end)
                return start;

            DateTime[] dates = chartDateTimes
                .SkipWhile(a => a < start)
                .TakeWhile(a => a <= end)
                .ToArray();

            if (dates.Length == 0)
                return start;

            return dates[^(dates.Length / 2)];
        }

        protected Annotation GetAnnotation(
            BarPoint bp1, BarPoint bp2, Color color, string text, List<DateTime> chartDateTimes)
        {
            DateTime x = GetMedianDate(bp1.OpenTime, bp2.OpenTime, chartDateTimes);
            double y = bp1.Value + (bp2.Value - bp1.Value) / 2;
            Annotation annotation = ChartGenerator.GetAnnotation(x, y, ChartGenerator.BLACK_COLOR, ChartGenerator.CHART_FONT_MAIN, color, text);
            return annotation;
        }

        protected Shape GetSetupRectangle(
            DateTime setupStart, DateTime setupEnd, Color color, double levelStart, double levelEnd)
        {
            Shape shape = Shape.init(StyleParam.ShapeType.Rectangle.ToFSharp(),
                X0: setupStart.ToFSharp(),
                Y0: levelStart.ToFSharp(),
                X1: setupEnd.ToFSharp(),
                Y1: levelEnd.ToFSharp(),
                Fillcolor: color,
                Line: Line.init(Color: color));

            return shape;
        }

        protected void GetSetupEndRender(
            DateTime openDateTime, TimeFrame tf, out  DateTime realStart, out DateTime realEnd)
        {
            TimeSpan timeFramePeriod = TimeFrameHelper.TimeFrames[tf].TimeSpan;
            realStart = openDateTime.Add(timeFramePeriod);
            realEnd = realStart.Add(timeFramePeriod * SETUP_MIN_WIDTH);
        }

        /// <summary>
        /// Called when cBot is stopped.
        /// </summary>
        /// <example>
        ///   <code>
        /// protected override void OnStop()
        /// {
        /// //This method is called when the cBot is stopped
        /// }
        /// </code>
        /// </example>
        /// <signature>
        ///   <code>public void OnStop()</code>
        /// </signature>
        protected override void OnStop()
        {
            base.OnStop();
            foreach (T sf in m_SetupFindersMap.Values)
            {
                sf.OnEnter -= OnEnter;
                sf.OnStopLoss -= OnStopLoss;
                sf.OnTakeProfit -= OnTakeProfit;
                sf.OnTakeProfit -= OnBreakeven;
                m_SetupIdBarsMap[sf.Id].BarOpened -= BarOpened;
            }

            foreach (Bars minimalBar in m_MinimalBars)
            {
                minimalBar.BarOpened -= MinimalBarOpened;
            }

            Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}
