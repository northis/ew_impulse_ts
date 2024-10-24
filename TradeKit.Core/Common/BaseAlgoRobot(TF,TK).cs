﻿using System.Globalization;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Telegram;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;
using Shape = Plotly.NET.LayoutObjects.Shape;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Base (ro)bot algorithms with common operations for trading
    /// </summary>
    /// <typeparam name="TF">Type of <see cref="BaseSetupFinder{TK}"/></typeparam>
    /// <typeparam name="TK">The type of <see cref="SignalEventArgs"/> - what type of signals supports this bot.</typeparam>
    public abstract class BaseAlgoRobot<TF,TK> : IDisposable 
        where TF : BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        protected readonly ITradeManager TradeManager;
        private readonly IStorageManager m_StorageManager;
        private readonly RobotParams m_RobotParams;
        private readonly bool m_IsBackTesting;
        private readonly bool m_GenerateChart;
        protected const double SPREAD_MARGIN_RATIO = 1.1;
        protected const int CHART_BARS_MARGIN_COUNT = 5;
        protected const double CHART_FONT_HEADER = 36;
        protected const int CHART_HEIGHT = 1000;
        protected const int CHART_WIDTH = 1000;
        private const int SETUP_MIN_WIDTH = 3;
        protected const string ZERO_CHART_FILE_POSTFIX = "img.00";
        protected const string FIRST_CHART_FILE_POSTFIX = "img.01";
        private double m_CurrentRisk;
        protected TelegramReporter TelegramReporter;
        private readonly Dictionary<string, TF> m_SetupFindersMap;
        private readonly Dictionary<string, TF[]> m_SymbolFindersMap;
        private readonly Dictionary<string, IBarsProvider> m_FinderIdChartBarProviderMap;
        private readonly Dictionary<string, ISymbol> m_SymbolsMap;
        private readonly Dictionary<string, bool> m_BarsInitMap;
        private readonly Dictionary<string, List<int>> m_PositionFinderMap;
        private readonly Dictionary<string, TK> m_ChartFileFinderMap;
        private readonly string[] m_Symbols;
        private readonly string[] m_TimeFrames;
        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;
        private bool m_IsInit;
        private readonly string[] m_RestrictedChars = Path.GetInvalidPathChars().Select(a=>a.ToString()).ToArray();

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseAlgoRobot{T,TK}"/> class.
        /// </summary>
        /// <param name="tradeManager">Trade-related manager.</param>
        /// <param name="storageManager">The storage manager.</param>
        /// <param name="robotParams">The robot parameters.</param>
        /// <param name="isBackTesting">if set to <c>true</c> if this a history trading.</param>
        /// <param name="symbolName">Name of the symbol.</param>
        /// <param name="timeFrameName">Name of the time frame.</param>
        /// <param name="generateChart">True (default) to generate chart image for each setup</param>
        protected BaseAlgoRobot(
            ITradeManager tradeManager,
            IStorageManager storageManager,
            RobotParams robotParams,
            bool isBackTesting,
            string symbolName,
            string timeFrameName,
            bool generateChart = true)
        {
            TradeManager = tradeManager;
            m_StorageManager = storageManager;
            m_RobotParams = robotParams;
            m_IsBackTesting = isBackTesting;
            m_GenerateChart = generateChart;

            m_SetupFindersMap = new Dictionary<string, TF>();
            m_FinderIdChartBarProviderMap = new Dictionary<string, IBarsProvider>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, ISymbol>();
            m_SymbolFindersMap = new Dictionary<string, TF[]>();
            m_PositionFinderMap = new Dictionary<string, List<int>>();
            m_ChartFileFinderMap = new Dictionary<string, TK>();
            m_CurrentRisk = robotParams.RiskPercentFromDeposit;

            m_Symbols = !robotParams.UseSymbolsList || string.IsNullOrEmpty(robotParams.SymbolsToProceed)
                ? new[] { symbolName }
                : SplitString(robotParams.SymbolsToProceed);
            m_TimeFrames = !robotParams.UseTimeFramesList || string.IsNullOrEmpty(robotParams.TimeFramesToProceed)
                ? new[] { timeFrameName }
                : SplitString(robotParams.TimeFramesToProceed);
        }

        /// <summary>
        /// The method <see cref="Init"/> should be called here after the initialization of all descendant classes is completed.
        /// </summary>
        public void Init()
        {
            if (m_IsInit)
                return;

            m_IsInit = true;
            HashSet<string> symbolsAvailable = TradeManager.GetSymbolNamesAvailable();

            foreach (string symbol in m_Symbols)
            {
                if (!symbolsAvailable.Contains(symbol))
                {
                    continue;
                }

                ISymbol symbolEntity = TradeManager.GetSymbol(symbol);
                var finders = new List<TF>();
                foreach (string timeFrameStr in m_TimeFrames)
                {
                    ITimeFrame timeFrame = TradeManager.GetTimeFrame(timeFrameStr);
                    if (timeFrame == null)
                        continue;

                    TF sf = CreateSetupFinder(timeFrame, symbolEntity);
                    string key = sf.Id;

                    sf.BarsProvider.BarOpened += BarOpened;
                    m_SymbolsMap[key] = symbolEntity;
                    m_SetupFindersMap[key] = sf;
                    m_BarsInitMap[key] = false;
                    finders.Add(sf);
                    Logger.Write($"Symbol {symbol}, time frame {timeFrame.Name} is added");
                }

                m_SymbolFindersMap[symbol] = finders.ToArray();
            }
            
            Logger.Write("Creating chart dictionaries...");
            foreach (KeyValuePair<string, TF> finder in m_SetupFindersMap)
            {
                ITimeFrame chartTimeFrame = finder.Value.TimeFrame;
                //Use this to show a smaller TF on the chart
                //chartTimeFrame =  TimeFrameHelper.GetPreviousTimeFrameInfo(finder.Value.TimeFrame).TimeFrame;

                IBarsProvider barProvider = m_SetupFindersMap
                    .Where(a =>
                        a.Value.Symbol == finder.Value.Symbol &&
                        a.Value.TimeFrame == chartTimeFrame)
                    .Select(a => a.Value.BarsProvider)
                    .FirstOrDefault() ?? CreateBarsProvider(chartTimeFrame, finder.Value.Symbol);

                m_FinderIdChartBarProviderMap[finder.Key] = barProvider;
            }

            if (m_RobotParams.SaveChartForManualAnalysis)
            {
                Logger.Write($"Your charts will be in this folder: {Helper.DirectoryToSaveResults}");
            }

            TradeManager.PositionClosed += OnPositionsClosed;

            TelegramReporter = new TelegramReporter(
                m_RobotParams.TelegramBotToken, m_RobotParams.ChatId, m_StorageManager,
                m_RobotParams.PostCloseMessages);

            Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }
        
        protected abstract IBarsProvider CreateBarsProvider(ITimeFrame timeFrame, ISymbol symbolEntity);

        /// <summary>
        /// Gets the get current risk.
        /// </summary>
        protected double GetCurrentRisk
        {
            get
            {
                if (m_RobotParams.UseProgressiveVolume)
                {
                    return m_CurrentRisk;
                }

                return m_RobotParams.RiskPercentFromDeposit;
            }
        }

        /// <summary>
        /// Ups the risk.
        /// </summary>
        protected void UpRisk()
        {
            if (m_CurrentRisk >= m_RobotParams.RiskPercentFromDepositMax)
            {
                m_CurrentRisk = m_RobotParams.RiskPercentFromDepositMax;
                return;
            }
            
            m_CurrentRisk += m_RobotParams.RiskPercentFromDeposit;
        }

        /// <summary>
        /// Downs the risk.
        /// </summary>
        protected void DownRisk()
        {
            if (m_CurrentRisk <= m_RobotParams.RiskPercentFromDeposit)
            {
                m_CurrentRisk = m_RobotParams.RiskPercentFromDeposit;
                return;
            }

            m_CurrentRisk -= m_RobotParams.RiskPercentFromDeposit;
        }

        private string[] SplitString(string str)
        {
            return str.Split(new[] { '|', ',', ';', }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public abstract string GetBotName();

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="timeFrame">The TF.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected abstract TF CreateSetupFinder(ITimeFrame timeFrame, ISymbol symbolEntity);

        private void OnPositionsClosed(object sender, ClosedPositionEventArgs args)
        {
            if (args.State == PositionClosedState.STOP_LOSS)
            {
                UpRisk();
                return;
            }

            if (args.State == PositionClosedState.TAKE_PROFIT)
            {
                DownRisk();
            }
        }

        private void BarOpened(object obj, System.EventArgs args)
        {
            if (!(obj is IBarsProvider barsProvider))
                return;

            try
            {
                int prevCount = barsProvider.Count - 1;
                int index = prevCount - 1;
                if (index < 0)
                {
                    return;
                }

                string finderId = BaseSetupFinder<TK>.GetId(barsProvider.BarSymbol.Name, barsProvider.TimeFrame.Name);
                if (!m_SetupFindersMap.TryGetValue(finderId, out TF sf))
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
                Logger.Write($"{nameof(BarOpened)}: Bars initialized - {barsProvider.BarSymbol.Name} {barsProvider.TimeFrame.ShortName}");
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(BarOpened)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes or sets the breakeven for the symbol positions.
        /// </summary>
        /// <param name="setupId">ID of the setup finder.</param>
        /// <param name="posId">Identity args to find the position</param>
        /// <param name="breakEvenPrice">If not null, sets this price as a breakeven</param>
        private void ModifySymbolPositions(
            string setupId, string posId, double? breakEvenPrice = null)
        {
            if (!m_PositionFinderMap.TryGetValue(setupId, out List<int> posIds)
                || posIds == null || posIds.Count == 0)
                return;

            IPosition[] positionsToModify = TradeManager.GetPositions()
                .Where(a => posIds.Contains(a.Id) && a.Comment == posId)
                .ToArray();

            foreach (IPosition position in positionsToModify)
            {
                if (breakEvenPrice.HasValue)
                    TradeManager.SetStopLossPrice(position, breakEvenPrice.Value);
                else
                {
                    posIds.Remove(position.Id);
                    TradeManager.Close(position);
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
            TF sf = (TF)sender;
            setupId = sf.Id;
            GetEventStrings(sender, e.Level, out price);
            positionId = Helper.GetPositionId(setupId, e.FromLevel, e.Comment);
        }
        
        protected void OnStopLoss(object sender, LevelEventArgs e)
        {
            HandleClose(sender, e, out string price, out string setupId, out string positionId);
            m_StopCount++;
            Logger.Write($"SL hit! {price}");
            ModifySymbolPositions(setupId, positionId);
            string resultChartPath = ShowResultChart(sender, false);

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportStopLoss(positionId, resultChartPath);
        }

        /// <summary>
        /// Generates the second result chart for the setup finder
        /// </summary>
        /// <param name="setupFinder">The setup finder we want to check. See <see cref="TF"/></param>
        /// <param name="successTrade">True - TP hit, False - SL hit</param>
        private string ShowResultChart(object setupFinder, bool successTrade)
        {
            if (setupFinder is not TF sf ||
                !m_ChartFileFinderMap.TryGetValue(sf.Id, out TK signalEventArgs))
            {
                return null;
            }

            if (m_IsBackTesting)
                OnResultForManualAnalysis(signalEventArgs, sf, successTrade);

            if (!m_RobotParams.SaveChartForManualAnalysis)
                return null;

            IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
            string res = GeneratePlotImageFile(bp, signalEventArgs, true, successTrade);
            m_ChartFileFinderMap.Remove(sf.Id);
            return res;
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
            string resultChartPath = ShowResultChart(sender, true);

            if (!TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportTakeProfit(positionId, resultChartPath);
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
        protected abstract bool HasSameSetupActive(TF setupFinder, TK signal);

        /// <summary>
        /// Determines whether the interval has a trade break inside.
        /// </summary>
        /// <param name="dateStart">The interval start.</param>
        /// <param name="dateEnd">The interval end.</param>
        /// <param name="symbol">The symbol to check the interval against.</param>
        /// <returns>
        ///   <c>true</c> if the interval has a trade break inside; otherwise, <c>false</c>.
        /// </returns>
        protected virtual bool HasTradeBreakInside(
            DateTime dateStart, DateTime dateEnd, ISymbol symbol)
        {
            ITradingHours[] sessions = TradeManager.GetTradingHours(symbol);
            if (sessions.Length == 0)
                return false;

            TimeSpan safeTimeDurationStart = TimeSpan.FromHours(1);

            DateTime setupDayStart = dateStart
                .Subtract(dateStart.TimeOfDay)
                .AddDays(-(int)dateStart.DayOfWeek);
            bool isSetupInDay = !sessions.Any();
            foreach (ITradingHours session in sessions)
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
        protected virtual bool IsOvernightTrade(TK signal, TF setupFinder)
        {
            return false;
        }

        private void OnEnter(object sender, TK e)
        {
            var sf = (TF)sender;
            if (!m_SymbolFindersMap.TryGetValue(sf.Symbol.Name, out TF[] finders))
            {
                return;
            }
            
            ISymbol symbol = m_SymbolsMap[sf.Id];

            double tp = e.TakeProfit.Value;
            double sl = e.StopLoss.Value;
            bool isLong = sl < tp;
            double spread = TradeManager.GetSpread(symbol);
            double rangeLen = Math.Abs(tp - sl);

            if (spread > 0 && rangeLen / spread < Helper.MAX_SPREAD_RATIO)
            {
                if (!m_RobotParams.AllowEnterOnBigSpread)
                {
                    Logger.Write("Big spread, ignore the signal");
                    return;
                }
            }

            if (!m_RobotParams.AllowOvernightTrade && IsOvernightTrade(e, sf))
            {
                return;
            }

            foreach (TF finder in finders)
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
            double priceNow = isLong ? TradeManager.GetAsk(symbol) : TradeManager.GetBid(symbol);

            if (!isLong)
            {
                sl += spread * SPREAD_MARGIN_RATIO;
                tp += spread * SPREAD_MARGIN_RATIO;
            }

            if (m_IsBackTesting || m_RobotParams.AllowToTrade)
            {
                double slLen = Math.Abs(priceNow - sl);
                double slP = Math.Round(slLen / sf.Symbol.PipSize);
                if (slP < 0)
                    return;

                double tpLen = Math.Abs(priceNow - tp);
                double tpP = Math.Round(tpLen / sf.Symbol.PipSize);
                double rangeP = Math.Round(rangeLen / sf.Symbol.PipSize);

                // System.Diagnostics.Debugger.Launch();
                double volume = GetVolume(symbol, rangeP);
                if (m_RobotParams.MaxMoneyPerSetup > 0)
                {
                    double moneyPerSetup =
                        m_RobotParams.RiskPercentFromDeposit * TradeManager.GetAccountBalance() / 100;

                    // We want to limit money for one setup if calculated amount greater than MaxMoneyPerSetup
                    double moneyLimitRatio = moneyPerSetup / m_RobotParams.MaxMoneyPerSetup;
                    if (moneyLimitRatio > 1)
                        volume /= moneyLimitRatio;
                }

                double volumeInLots = volume / symbol.LotSize;
                if (volumeInLots > m_RobotParams.MaxVolumeLots)
                {
                    Logger.Write(
                        $"The calculated volume is too big - {volumeInLots:F2}; max value is {m_RobotParams.MaxVolumeLots:F2} lots");
                    return;
                }

                OrderResult order = TradeManager.OpenOrder(
                    isLong, sf.Symbol, volume, GetBotName(), slP, tpP,
                    Helper.GetPositionId(sf.Id, e.Level));

                if (order?.IsSuccessful == true)
                {
                    TradeManager.SetTakeProfitPrice(order.Position, tp);
                    TradeManager.SetStopLossPrice(order.Position, sl);
                    m_PositionFinderMap[sf.Id].Add(order.Position.Id);
                }
            }

            //if (IsBacktesting && !SaveChartForManualAnalysis)
            //{
            //    return;
            //}

            Directory.CreateDirectory(Helper.DirectoryToSaveResults);
            if (!m_RobotParams.SaveChartForManualAnalysis)
            {
                foreach (string file in Directory.GetFiles(Helper.DirectoryToSaveResults))
                {
                    File.Delete(file);
                }
            }

            if (m_RobotParams.SaveChartForManualAnalysis || m_IsBackTesting)
                m_ChartFileFinderMap[sf.Id] = e;
            if (!TelegramReporter.IsReady)
                return;

            IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
            string plotImagePath = GeneratePlotImageFile(bp, e);
            TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = TradeManager.GetAsk(symbol),
                Bid = TradeManager.GetBid(symbol),
                Digits = sf.Symbol.Digits,
                PipSize = sf.Symbol.PipSize,
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
            TF setupFinder,
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
        /// Defines the start date we want to show the chart from.
        /// </summary>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        protected virtual DateTime GetStartViewDate(TK signalEventArgs)
        {
            DateTime startView = signalEventArgs.StartViewBarTime;
            return startView;
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
            if (!m_GenerateChart)
                return null;

            DateTime startView = GetStartViewDate(signalEventArgs);
            int firstIndex = barProvider.GetIndexByTime(startView);
            int earlyBar = Math.Max(0, firstIndex - CHART_BARS_MARGIN_COUNT);

            int lastIndex = barProvider.Count - 1;
            int barsCount = lastIndex - earlyBar + 1;
            if (barsCount <= 0)
            {
                return null;
            }
            
            bool useCommonTimeFrame = TimeFrameHelper.TimeFrames
                .TryGetValue(barProvider.TimeFrame.Name, out TimeFrameInfo timeFrameInfo);

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
                s.O, s.H, s.L, s.C, s.D, barProvider.BarSymbol.Name, rangeBreaks, timeFrameInfo.TimeSpan,
                out Rangebreak[] rbs);

            string comment = signalEventArgs.Comment;
            if (comment == null)
                comment = string.Empty;
            else
            {
                comment = $".{comment}";
                foreach (string rStr in m_RestrictedChars) comment = comment.Replace(rStr, string.Empty);
                comment = comment.Replace(" ", "_");
            }

            OnDrawChart(candlestickChart, signalEventArgs, barProvider, validDateTimes);
            GenericChart.GenericChart[] layers =
                GetAdditionalChartLayers(signalEventArgs, lastCloseDateTime)
                ?? Array.Empty<GenericChart.GenericChart>();
            GenericChart.GenericChart resultChart = Chart.Combine(
                    layers.Concat(new[] { candlestickChart }))
                .WithTitle(
                    $@"{barProvider.BarSymbol.Name} {barProvider.TimeFrame.ShortName} {lastCloseDateTime.ToUniversalTime():R} ",
                    Font.init(Size: CHART_FONT_HEADER));

            string fileName = startView.ToString("s").Replace(":", "-");
            string dirPath = Path.Combine(Helper.DirectoryToSaveResults,
                $"{fileName}.{barProvider.BarSymbol.Name}.{barProvider.TimeFrame.ShortName}{comment}");
            Directory.CreateDirectory(dirPath);

            string imageName;
            if (m_RobotParams.SaveChartForManualAnalysis)
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
        /// Gets the volume for the symbol given and allowed points for stop loss.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="rangePoints">The length in points.</param>
        /// <returns>Size of a position calculated.</returns>
        protected virtual double GetVolume(ISymbol symbol, double rangePoints)
        {
            double volume = symbol.GetVolume(
                GetCurrentRisk, TradeManager.GetAccountBalance(), rangePoints);
            return volume;
        }

        private void GetEventStrings(object sender, BarPoint level, out string price)
        {
            var sf = (TF)sender;
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

        private DateTime GetMedianDate(DateTime start, DateTime end, List<DateTime> chartDateTimes)
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

        /// <summary>
        /// Gets the annotation instance for the chart element.
        /// </summary>
        /// <param name="bp1">The BP1.</param>
        /// <param name="bp2">The BP2.</param>
        /// <param name="color">The color.</param>
        /// <param name="text">The text.</param>
        /// <param name="chartDateTimes">The chart date times.</param>
        protected Annotation GetAnnotation(
            BarPoint bp1, BarPoint bp2, Color color, string text, List<DateTime> chartDateTimes)
        {
            DateTime x = GetMedianDate(bp1.OpenTime, bp2.OpenTime, chartDateTimes);
            double y = bp1.Value + (bp2.Value - bp1.Value) / 2;
            Annotation annotation = ChartGenerator.GetAnnotation(x, y, ChartGenerator.BLACK_COLOR, ChartGenerator.CHART_FONT_MAIN, color, text);
            return annotation;
        }

        /// <summary>
        /// Gets the "setup rectangle" (entry + SL +TP) for the chart.
        /// </summary>
        /// <param name="setupStart">The setup start.</param>
        /// <param name="setupEnd">The setup end.</param>
        /// <param name="color">The color.</param>
        /// <param name="levelStart">The level start.</param>
        /// <param name="levelEnd">The level end.</param>
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

        /// <summary>
        /// Gets the range of the chart view to display the position and candles next to entry point.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        /// <param name="tf">The tf.</param>
        /// <param name="realStart">The real start.</param>
        /// <param name="realEnd">The real end.</param>
        protected void GetSetupEndRender(
            DateTime openDateTime, ITimeFrame tf, out DateTime realStart, out DateTime realEnd)
        {
            TimeSpan timeFramePeriod = TimeFrameHelper.TimeFrames[tf.Name].TimeSpan;
            realStart = openDateTime.Add(timeFramePeriod);
            realEnd = realStart.Add(timeFramePeriod * SETUP_MIN_WIDTH);
        }

        /// <summary>
        /// Called when cBot is stopped.
        /// </summary>
        public virtual void Dispose()
        {
            TradeManager.PositionClosed -= OnPositionsClosed;
            foreach (TF sf in m_SetupFindersMap.Values)
            {
                sf.OnEnter -= OnEnter;
                sf.OnStopLoss -= OnStopLoss;
                sf.OnTakeProfit -= OnTakeProfit;
                sf.OnTakeProfit -= OnBreakeven;
                sf.BarsProvider.BarOpened -= BarOpened;
                IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
                bp.Dispose();
            }

            Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}
