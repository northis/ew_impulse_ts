using System.Diagnostics;
using System.Globalization;
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
        private readonly bool m_GenerateReport;
        protected const double SPREAD_MARGIN_RATIO = 1;
        protected const int CHART_BARS_MARGIN_COUNT = 5;
        protected const double CHART_FONT_HEADER = 36;
        protected const int CHART_HEIGHT = 1000;
        protected const int CHART_WIDTH = 1000;
        private const int SETUP_MIN_WIDTH = 3;
        protected const string ZERO_CHART_FILE_POSTFIX = "img.00";
        protected const string FIRST_CHART_FILE_POSTFIX = "img.01";
        protected const string BREAKEVEN_SUFFIX = "_BE";
        protected const string TAKE_PROFIT_SUFFIX = "_TP";
        private double m_CurrentRisk;
        protected TelegramReporter TelegramReporter;
        private readonly Dictionary<string, TF> m_SetupFindersMap;
        private readonly Dictionary<string, TF[]> m_SymbolFindersMap;
        private readonly Dictionary<string, IBarsProvider> m_FinderIdChartBarProviderMap;
        private readonly Dictionary<string, ISymbol> m_SymbolsMap;
        private readonly Dictionary<string, bool> m_BarsInitMap;
        private readonly Dictionary<string, List<int>> m_FinderPositionMap;
        private readonly Dictionary<string, TK> m_PositionSignalArgsMap;
        private readonly Dictionary<int, TF> m_PositionFinderMap;
        private readonly Dictionary<string, ClosedPositionEventArgs> m_ClosedPositionMap;
        private readonly Dictionary<string, TK> m_ChartFileFinderMap;
        private readonly string[] m_Symbols;
        private readonly string[] m_TimeFrames;
        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;
        private bool m_IsInit;
        private readonly string[] m_RestrictedChars = Path.GetInvalidPathChars().Select(a => a.ToString()).ToArray();
        private readonly bool m_IsTradable;

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
        /// <param name="generateReport">True to generate report image for each setup. False is default.</param>
        protected BaseAlgoRobot(
            ITradeManager tradeManager,
            IStorageManager storageManager,
            RobotParams robotParams,
            bool isBackTesting,
            string symbolName,
            string timeFrameName,
            bool generateChart = true,
            bool generateReport = false)
        {
            TradeManager = tradeManager;
            m_StorageManager = storageManager;
            m_RobotParams = robotParams;
            m_IsBackTesting = isBackTesting;
            m_GenerateChart = generateChart;
            m_GenerateReport = generateReport;

            m_SetupFindersMap = new Dictionary<string, TF>();
            m_PositionSignalArgsMap = new Dictionary<string, TK>();
            m_FinderIdChartBarProviderMap = new Dictionary<string, IBarsProvider>();
            m_PositionFinderMap = new Dictionary<int, TF>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, ISymbol>();
            m_SymbolFindersMap = new Dictionary<string, TF[]>();
            m_FinderPositionMap = new Dictionary<string, List<int>>();
            m_ChartFileFinderMap = new Dictionary<string, TK>();
            m_ClosedPositionMap = new Dictionary<string, ClosedPositionEventArgs>();
            m_CurrentRisk = robotParams.RiskPercentFromDeposit;

            m_Symbols = !robotParams.UseSymbolsList || string.IsNullOrEmpty(robotParams.SymbolsToProceed)
                ? new[] { symbolName }
                : SplitString(robotParams.SymbolsToProceed);
            m_TimeFrames = !robotParams.UseTimeFramesList || string.IsNullOrEmpty(robotParams.TimeFramesToProceed)
                ? new[] { timeFrameName }
                : SplitString(robotParams.TimeFramesToProceed);

            m_IsTradable = m_RobotParams.AllowToTrade || m_IsBackTesting;
        }

        /// <summary>
        /// The method <see cref="Init"/> should be called here after the initialization of all descendant classes is completed.
        /// </summary>
        public void Init()
        {
            if (m_IsInit)
                return;

            m_IsInit = true;
            TradeManager.SetBotName(GetBotName());
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

                    sf.BarsProvider.LoadBars(DateTime.Now.AddDays(-2));
                    sf.BarsProvider.BarClosed += BarClosed;
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
                //chartTimeFrame = TimeFrameHelper.GetPreviousTimeFrameInfo(finder.Value.TimeFrame).TimeFrame;

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
            TradeManager.OnTick += TradeManagerOnTick;
            TelegramReporter = new TelegramReporter(
                m_RobotParams.TelegramBotToken, m_RobotParams.ChatId, m_StorageManager,
                m_RobotParams.PostCloseMessages);

            Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }

        private void TradeManagerOnTick(object sender, SymbolTickEventArgs e)
        {
            if (!m_SymbolFindersMap.TryGetValue(e.Symbol.Name, out TF[] setupFinders))
                return;

            foreach (TF sf in setupFinders)
            {
                sf.CheckTick(e);
            }
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

        private string ToTakeProfit(string positionId)
        {
            if (string.IsNullOrEmpty(positionId))
                return null;

            if (positionId.EndsWith(BREAKEVEN_SUFFIX))
                return positionId.Replace(BREAKEVEN_SUFFIX, TAKE_PROFIT_SUFFIX);

            return null;
        }
        
        private void OnPositionsClosed(object sender, ClosedPositionEventArgs args)
        {
            if (args.Position.Label != GetBotName())
                return;

            string positionId = args.Position.Comment;
            if(string.IsNullOrEmpty(positionId))
                return;

            bool isSl = args.State == PositionClosedState.STOP_LOSS;
            bool isTp = args.State == PositionClosedState.TAKE_PROFIT;

            if (m_IsTradable)
            {
                m_ClosedPositionMap[args.Position.Comment] = args;
            }

            if (m_PositionFinderMap.Remove(args.Position.Id, out TF sf) &&
                m_PositionSignalArgsMap.Remove(positionId, out TK signalEventArgs))
            {
                if (signalEventArgs.CanUseBreakeven && isTp && m_IsTradable)
                {
                    string bePositionId = ToTakeProfit(args.Position.Comment);
                    if (!string.IsNullOrEmpty(bePositionId) && 
                        m_PositionSignalArgsMap.TryGetValue(bePositionId, out TK beEventArgs))
                    {
                        OnBreakeven(sf, new LevelEventArgs(beEventArgs.StopLoss, beEventArgs.Level, true, beEventArgs.Comment));
                    }
                }

                if (!isSl && !isTp)// We don't want ot handle usual closing, just manual or securing ones
                    sf.NotifyManualClose(signalEventArgs, args);
            }
            
            if (isSl)
            {
                UpRisk();
                return;
            }

            if (isTp) DownRisk();
        }

        private TF GetSetupFinder(ISymbol symbol, ITimeFrame timeFrame)
        {
            string finderId = BaseSetupFinder<TK>.GetId(symbol.Name, timeFrame.Name);
            return m_SetupFindersMap.GetValueOrDefault(finderId);
        }

        private void BarClosed(object obj, System.EventArgs args)
        {
            if (!(obj is IBarsProvider barsProvider))
                return;

            try
            {
                int index = barsProvider.Count - 1;
                if (index < 0)
                {
                    return;
                }

                TF sf = GetSetupFinder(barsProvider.BarSymbol, barsProvider.TimeFrame);
                if (sf == null)
                {
                    return;
                }

                if (m_BarsInitMap[sf.Id])
                {
                    //Logger.Write($"{nameof(BarOpened)}: {barsProvider.BarSymbol} {barsProvider.TimeFrame}");
                    sf.CheckBar(barsProvider.GetOpenTime(index));
                    return;
                }

                for (int i = 0; i < barsProvider.Count; i++)
                {
                    sf.CheckBar(barsProvider.GetOpenTime(i));
                }

                sf.OnEnter += OnEnter;
                sf.OnStopLoss += OnStopLoss;
                sf.OnTakeProfit += OnTakeProfit;
                sf.OnActivated += OnActivated;
                sf.OnCanceled += OnCanceled;

                if (m_IsTradable)
                    sf.OnBreakeven += OnBreakeven;
                sf.OnManualClose += OnManualClose;
                sf.OnEdit += OnEdit;
                //sf.IsInSetup = false; //TODO
                m_BarsInitMap[sf.Id] = true;
                sf.MarkAsInitialized();
                Logger.Write($"Pair initialized - {barsProvider.BarSymbol.Name} {barsProvider.TimeFrame.ShortName}");
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(BarClosed)}: {ex}");
            }
        }

        private void OnEdit(object sender, TK e)
        {
            TF sf = (TF)sender;
            var setupId = sf.Id;
            if (!m_FinderPositionMap.TryGetValue(setupId, out List<int> posIds)
                || posIds == null || posIds.Count == 0)
                return;

            IPosition[] positionsToModify = TradeManager.GetPositions()
                .Where(a => posIds.Contains(a.Id))
                .ToArray();

            foreach (IPosition position in positionsToModify)
            {
                TradeManager.SetStopLossPrice(position, e.StopLoss.Value);

                if (e.TakeProfit != null)
                    TradeManager.SetTakeProfitPrice(position, e.TakeProfit.Value);
            }
        }

        private void OnManualClose(object sender, LevelEventArgs e)
        {
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            string resultChartPath = OnPositionClose(sender, setupId, positionId, true);
            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportManualClose(positionId, e.Level.Value, resultChartPath);
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
            if (!m_FinderPositionMap.TryGetValue(setupId, out List<int> posIds)
                || posIds == null || posIds.Count == 0)
                return;

            IPosition[] positionsToModify = TradeManager.GetPositions()
                .Where(a => posIds.Contains(a.Id) && a.Comment == posId)
                .ToArray();

            foreach (IPosition position in positionsToModify)
            {
                if (breakEvenPrice.HasValue)
                {
                    TradeManager.SetBreakeven(position);
                }
                else
                {
                    posIds.Remove(position.Id);
                    TradeManager.Close(position);
                }

                break;
            }
        }

        /// <summary>
        /// Handles the order (setup) event.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        /// <param name="price">The price.</param>
        /// <param name="setupId">The setup identifier.</param>
        /// <param name="positionId">The position identifier.</param>
        /// <returns>True, if the positions exist</returns>
        protected void HandleOrderEvent(object sender, LevelEventArgs e,
            out string price, out string setupId, out string positionId)
        {
            TF sf = (TF)sender;
            setupId = sf.Id;
            GetEventStrings(sender, e.Level, out price);
            positionId = Helper.GetPositionId(setupId, e.FromLevel, e.Comment);
        }

        /// <summary>
        /// Called when on position close.
        /// </summary>
        /// <param name="sender">The sender (setup finder object).</param>
        /// <param name="setupId">The setup identifier.</param>
        /// <param name="positionId">The position identifier.</param>
        /// <param name="successTrade">if set to <c>true</c> we close the trade with success.</param>
        /// <returns>Path to the final report or chart result image file.</returns>
        private string OnPositionClose(object sender, string setupId, string positionId, bool successTrade)
        {
            ModifySymbolPositions(setupId, positionId);
            ClosedPositionEventArgs closedArgs = null;
            if (m_RobotParams.AllowToTrade || m_IsBackTesting)
            {
                Logger.Write($"SetupId {setupId} - handle position {positionId}");
                m_ClosedPositionMap.Remove(positionId, out closedArgs);
            }

            if (sender is not TF sf ||
                !m_ChartFileFinderMap.TryGetValue(sf.Id, out TK signalEventArgs))
            {
                Logger.Write($"SetupId {setupId} - no chart map found");
                return null;
            }

            if (closedArgs == null)
            {
                Logger.Write($"SetupId {setupId} is not found");
                TradeManager.CancelOrder(positionId);
            }

            string resultChartPath = m_GenerateReport && closedArgs != null
                ? GenerateReportFile(closedArgs, sf.TimeFrame.ShortName)
                : ShowResultChart(sf, signalEventArgs, successTrade);

            return resultChartPath;
        }
        
        protected void OnStopLoss(object sender, LevelEventArgs e)
        {
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            m_StopCount++;
            Logger.Write($"SL hit! ({positionId})");
            string resultChartPath = OnPositionClose(sender, setupId, positionId, false);

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportStopLoss(positionId, resultChartPath);
        }

        /// <summary>
        /// Generates the second result chart for the setup finder
        /// </summary>
        /// <param name="sf">The setup finder we want to check. See <see cref="TF"/></param>
        /// <param name="signalEventArgs">The signal arguments we want to check. See <see cref="TK"/></param>
        /// <param name="successTrade">True - TP hit, False - SL hit</param>
        private string ShowResultChart(TF sf, TK signalEventArgs, bool successTrade)
        {
            Logger.Write($"On ShowResultChart, position comment: {signalEventArgs.Comment}");
            if (!m_RobotParams.SaveChartForManualAnalysis)
                return null;
            
            if (m_IsBackTesting)
                OnResultForManualAnalysis(signalEventArgs, sf, successTrade);

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
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            m_TakeCount++;
            Logger.Write($"TP hit! ({positionId})");
            string resultChartPath = OnPositionClose(sender, setupId, positionId, true);

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportTakeProfit(positionId, resultChartPath);
        }

        /// <summary>
        /// Called when the order is canceled.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnCanceled(object sender, LevelEventArgs e)
        {
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            TradeManager.CancelOrder(positionId);
            Logger.Write($"Order is canceled! ({positionId})");

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportCanceled(positionId);
        }

        /// <summary>
        /// Called when the order is canceled.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnActivated(object sender, LevelEventArgs e)
        {
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            //TradeManager.ConvertToMarketOrder(positionId); // If we want to open market order now if it is still pending
            Logger.Write($"Order is activated! ({positionId})");

            if (!TelegramReporter.IsReady)
                return;

            TelegramReporter.ReportActivated(positionId);
        }

        /// <summary>
        /// Called when breakeven is hit
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnBreakeven(object sender, LevelEventArgs e)
        {
            HandleOrderEvent(sender, e, out string price, out string setupId, out string positionId);
            Logger.Write($"Breakeven is set! {positionId}");

            string correctedPosId = m_IsTradable ? positionId + TAKE_PROFIT_SUFFIX : positionId;
            ModifySymbolPositions(setupId, correctedPosId, e.Level.Value);

            if (!TelegramReporter.IsReady)
                return;

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
            Logger.Write($"{e.Comment}: On after Enter");
            var sf = (TF)sender;
            if (!m_SymbolFindersMap.TryGetValue(sf.Symbol.Name, out TF[] finders))
            {
                Logger.Write($"{e.Comment}: No SF found");
                return;
            }
            
            ISymbol symbol = m_SymbolsMap[sf.Id];

            double tp = e.TakeProfit.Value;
            double sl = e.StopLoss.Value;
            bool isLong = sl < tp;

            if (TradeManager.IsBigSpread(symbol, sl, tp))
            {
                if (!m_RobotParams.AllowEnterOnBigSpread)
                {
                    Logger.Write("Big spread, ignore the signal");
                    return;
                }
            }

            if (!m_RobotParams.AllowOvernightTrade && IsOvernightTrade(e, sf))
            {
                Logger.Write($"{e.Comment}: overnight skip");
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

            if (!m_FinderPositionMap.ContainsKey(sf.Id))
                m_FinderPositionMap[sf.Id] = new List<int>();

            GetEventStrings(sender, e.Level, out string price);
            Logger.Write($"New setup found! {price}");
            double priceNow = e.IsLimit 
                ? e.Level.Value
                : isLong ? TradeManager.GetAsk(symbol) : TradeManager.GetBid(symbol);

            /*if (!isLong)
            {
                sl += spread * SPREAD_MARGIN_RATIO;
                tp += spread * SPREAD_MARGIN_RATIO;
            
            }*/

            if (m_IsBackTesting || m_RobotParams.AllowToTrade)
            {
                double slLen = Math.Abs(priceNow - sl);
                double slP = Math.Round(slLen / sf.Symbol.PipSize);
                if (slP < 0)
                    return;

                double tpLen = Math.Abs(priceNow - tp);
                double tpP = Math.Round(tpLen / sf.Symbol.PipSize);
                double rangeLen = Math.Abs(tp - sl);
                double rangeP = Math.Round(rangeLen / sf.Symbol.PipSize);

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
                
                if (e.CanUseBreakeven)
                {
                    // Split volume into two positions: one with original TP, one with TP = BreakEvenPrice
                    double halfVolume = volume / 2;
                    double breakEvenTpLen = Math.Abs(priceNow - e.BreakEvenPrice);
                    double breakEvenTpP = Math.Round(breakEvenTpLen / sf.Symbol.PipSize);

                    // First position: half volume with original TP
                    string stringPositionId1 = Helper.GetPositionId(sf.Id, e.Level, e.Comment + TAKE_PROFIT_SUFFIX);
                    OrderResult order1 = TradeManager.OpenOrder(
                        isLong, sf.Symbol, halfVolume, GetBotName(), slP, tpP, stringPositionId1,
                        e.IsLimit ? e.Level.Value : null);

                    if (order1?.IsSuccessful == true)
                    {
                        if (!e.IsLimit)
                        {
                            TradeManager.SetTakeProfitPrice(order1.Position, tp);
                            TradeManager.SetStopLossPrice(order1.Position, sl);
                        }

                        m_FinderPositionMap[sf.Id].Add(order1.Position.Id);
                        m_PositionFinderMap[order1.Position.Id] = sf;
                        m_PositionSignalArgsMap[stringPositionId1] = e;
                    }

                    // Second position: half volume with TP = BreakEvenPrice
                    string stringPositionId2 = Helper.GetPositionId(sf.Id, e.Level, e.Comment + BREAKEVEN_SUFFIX);
                    OrderResult order2 = TradeManager.OpenOrder(
                        isLong, sf.Symbol, halfVolume, GetBotName(), slP, breakEvenTpP, stringPositionId2,
                        e.IsLimit ? e.Level.Value : null);

                    if (order2?.IsSuccessful == true)
                    {
                        if (!e.IsLimit)
                        {
                            TradeManager.SetTakeProfitPrice(order2.Position, e.BreakEvenPrice);
                            TradeManager.SetStopLossPrice(order2.Position, sl);
                        }

                        m_FinderPositionMap[sf.Id].Add(order2.Position.Id);
                        m_PositionFinderMap[order2.Position.Id] = sf;
                        m_PositionSignalArgsMap[stringPositionId2] = e;
                    }
                }
                else
                {
                    // Single position with original TP
                    string stringPositionId = Helper.GetPositionId(sf.Id, e.Level, e.Comment);
                    OrderResult order = TradeManager.OpenOrder(
                        isLong, sf.Symbol, volume, GetBotName(), slP, tpP, stringPositionId,
                        e.IsLimit ? e.Level.Value : null);

                    if (order?.IsSuccessful == true)
                    {
                        if (!e.IsLimit)
                        {
                            TradeManager.SetTakeProfitPrice(order.Position, tp);
                            TradeManager.SetStopLossPrice(order.Position, sl);
                        }

                        m_FinderPositionMap[sf.Id].Add(order.Position.Id);
                        m_PositionFinderMap[order.Position.Id] = sf;
                        m_PositionSignalArgsMap[stringPositionId] = e;
                    }
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

            if (m_RobotParams.SaveChartForManualAnalysis || m_IsBackTesting || m_RobotParams.AllowToTrade)
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

        private string GenerateReportFile(ClosedPositionEventArgs closedArgs, string tfName)
        {
            Logger.Write($"On GenerateReportFile, position comment: {closedArgs.Position.Comment}");
            IPosition position = TradeManager.GetClosedPosition(
                closedArgs.Position.Comment, 
                closedArgs.Position.TakeProfit,
                closedArgs.Position.StopLoss);
            string folder = GetDirectoryToSave(tfName, position.Symbol.Name, "report", "0");
            string path = ReportGenerator.GetPngReport(position, closedArgs.State, folder);
            return path;
        }

        private string GetDirectoryToSave(string tfName, string symbolName, string prefix = "", string comment = "")
        {
            if (comment == null)
                comment = string.Empty;
            else
            {
                comment = $".{comment}";
                foreach (string rStr in m_RestrictedChars) comment = comment.Replace(rStr, string.Empty);
                comment = comment.Replace(" ", "_");
            }

            string dirPath = Path.Combine(Helper.DirectoryToSaveResults,
                $"{prefix}.{symbolName}.{tfName}{comment}");
            Directory.CreateDirectory(dirPath);

            return dirPath;
        }

        /// <summary>
        /// Gets the path to the generated chart image file.
        /// </summary>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="signalEventArgs">Signal info args</param>
        /// <param name="showTradeResult">True if we want to see the result of the first trade.</param>
        /// <param name="successTrade">Null - no result yet, true - TP hit, false - SL hit.</param>
        /// <returns>Path to an image file</returns>
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

            OnDrawChart(candlestickChart, signalEventArgs, barProvider, validDateTimes);
            GenericChart.GenericChart[] layers =
                GetAdditionalChartLayers(signalEventArgs, lastCloseDateTime)
                ?? Array.Empty<GenericChart.GenericChart>();
            GenericChart.GenericChart resultChart = Chart.Combine(
                    layers.Concat(new[] { candlestickChart }))
                .WithTitle(
                    $@"{barProvider.BarSymbol.Name} {barProvider.TimeFrame.ShortName} {lastCloseDateTime.ToUniversalTime():R} ",
                    Font.init(Size: CHART_FONT_HEADER));

            string dirPath = GetDirectoryToSave(barProvider.TimeFrame.ShortName, barProvider.BarSymbol.Name,
                startView.ToString("s").Replace(":", "-"), signalEventArgs.Comment);
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
                if (m_IsTradable)
                    sf.OnTakeProfit -= OnBreakeven;
                sf.BarsProvider.BarClosed -= BarClosed;
                IBarsProvider bp = m_FinderIdChartBarProviderMap[sf.Id];
                bp.Dispose();
            }

            Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}
