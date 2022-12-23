using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Internals;
using Microsoft.FSharp.Core;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.EventArgs;
using TradeKit.Telegram;
using Color = Plotly.NET.Color;

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
        protected const string CHART_FILE_TYPE_EXTENSION = ".png";
        protected const int CHART_BARS_MARGIN_COUNT = 10;
        protected const int CHART_HEIGHT = 1000;
        protected const int CHART_WIDTH = 1000;
        protected const string FIRST_CHART_FILE_POSTFIX = ".01";
        protected const string SECOND_CHART_FILE_POSTFIX = ".02";
        private double m_CurrentRisk = RISK_DEPOSIT_PERCENT;
        protected TelegramReporter TelegramReporter;
        private Dictionary<string, T> m_SetupFindersMap;
        private Dictionary<string, T[]> m_SymbolFindersMap;
        private Dictionary<string, Bars> m_BarsMap;
        private Dictionary<string, Symbol> m_SymbolsMap;
        private Dictionary<string, bool> m_BarsInitMap;
        private Dictionary<string, bool> m_PositionFinderMap;
        private Dictionary<string, TK> m_ChartFileFinderMap;
        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;

        #region User properties

        /// <summary>
        /// Gets or sets the risk percent from deposit (regular).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDeposit), DefaultValue = RISK_DEPOSIT_PERCENT)]
        public double RiskPercentFromDeposit { get; set; }

        /// <summary>
        /// Gets or sets the risk percent from deposit (maximum).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDepositMax), DefaultValue = RISK_DEPOSIT_PERCENT_MAX)]
        public double RiskPercentFromDepositMax { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can trade.
        /// </summary>
        [Parameter(nameof(AllowToTrade), DefaultValue = false)]
        public bool AllowToTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can pass positions overnight (to the next trade day).
        /// </summary>
        [Parameter(nameof(AllowOvernightTrade), DefaultValue = false)]
        public bool AllowOvernightTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can open positions while big spread (spread/(tp-sl) ratio more than <see cref="Helper.MAX_SPREAD_RATIO"/>.
        /// </summary>
        [Parameter(nameof(AllowEnterOnBigSpread), DefaultValue = false)]
        public bool AllowEnterOnBigSpread { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should increase the volume every SL hit.
        /// </summary>
        [Parameter(nameof(UseProgressiveVolume), DefaultValue = false)]
        public bool UseProgressiveVolume { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use the symbols list.
        /// </summary>
        [Parameter(nameof(UseSymbolsList), DefaultValue = false)]
        public bool UseSymbolsList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should use the TF list.
        /// </summary>
        [Parameter(nameof(UseTimeFramesList), DefaultValue = false)]
        public bool UseTimeFramesList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should save .png files of the charts for manual analysis.
        /// </summary>
        [Parameter(nameof(SaveChartForManualAnalysis), DefaultValue = false)]
        public bool SaveChartForManualAnalysis { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should post the close messages like "tp/sl hit".
        /// </summary>
        [Parameter(nameof(PostCloseMessages), DefaultValue = true)]
        public bool PostCloseMessages { get; set; }

        /// <summary>
        /// Gets or sets the time frames we should use.
        /// </summary>
        [Parameter(nameof(TimeFramesToProceed), DefaultValue = "Minute5,Minute10,Minute15,Minute20,Minute30,Minute45")]
        public string TimeFramesToProceed { get; set; }

        /// <summary>
        /// Gets the symbol names.
        /// </summary>
        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,XAGUSD,XAUEUR,XAGEUR,EURUSD,GBPUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD,AUDCAD,AUDCHF,AUDJPY,CADJPY,CADCHF,CHFJPY,EURCAD,EURCHF,EURGBP,EURAUD,EURJPY,EURNZD,GBPCAD,GBPAUD,GBPJPY,GBPNZD,GBPCHF,NZDCAD,NZDJPY,US 30,US TECH 100,USDNOK,USDSEK,USDDDK")]
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
            m_BarsMap = new Dictionary<string, Bars>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, Symbol>();
            m_SymbolFindersMap = new Dictionary<string, T[]>();
            m_PositionFinderMap = new Dictionary<string, bool>();
            m_ChartFileFinderMap = new Dictionary<string, TK>();
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
                    {
                        continue;
                    }
                    
                    Bars bars = MarketData.GetBars(timeFrame, symbolName);
                    T sf = CreateSetupFinder(bars, symbolEntity);
                    string key = sf.Id;
                    m_BarsMap[key] = bars;
                    m_BarsMap[key].BarOpened += BarOpened;
                    m_SymbolsMap[key] = symbolEntity;
                    m_SymbolsMap[key].Tick += OnTick;
                    m_SetupFindersMap[key] = sf;
                    m_BarsInitMap[key] = false;
                    finders.Add(sf);
                    Logger.Write($"Symbol {symbolName}, time frame {timeFrame.Name} is added");
                }
                
                m_SymbolFindersMap[symbolName] = finders.ToArray();
            }

            if (SaveChartForManualAnalysis)
            {
                Logger.Write($"Your charts will be in this folder: {Helper.DirectoryToSaveImages}");
            }

            Positions.Closed += OnPositionsClosed;
            TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId, PostCloseMessages);
            Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }

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

        private void OnTick(SymbolTickEventArgs obj)
        {
            try
            {
                if (!m_SymbolFindersMap.TryGetValue(obj.SymbolName, out T[] finders))
                {
                    return;
                }

                foreach (T sf in finders)
                {
                    sf.CheckTick(obj.Bid);
                }
            }
            catch (Exception ex)
            {
               Logger.Write($"{nameof(OnTick)}: {ex.Message}");
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
                //sf.IsInSetup = false; //TODO
                m_BarsInitMap[finderId] = true;
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(BarOpened)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Closes the symbol positions.
        /// </summary>
        /// <param name="setupId">Id of the setup finder.</param>
        private void CloseSymbolPositions(string setupId)
        {
            string symbolName = m_SetupFindersMap[setupId].Symbol.Name;
            Position[] positionsToClose = Positions
                .Where(a => a.Label == GetBotName() && a.SymbolName == symbolName)
                .ToArray();

            foreach (Position positionToClose in positionsToClose)
            {
                positionToClose.Close();
            }
        }

        /// <summary>
        /// Handles the close.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        /// <param name="price">The price.</param>
        /// <param name="setupId">The setup identifier.</param>
        protected bool HandleClose(object sender, LevelEventArgs e,
            out string price, out string setupId)
        {
            T sf = (T)sender;
            setupId = sf.Id;
            price = null;
            if (!m_PositionFinderMap.TryGetValue(sf.Id, out bool isInPosition))
            {
                return false;
            }

            GetEventStrings(sender, e.Level, out price);
            m_PositionFinderMap[sf.Id] = false;
            return isInPosition;
        }
        
        protected void OnStopLoss(object sender, LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }
            
            m_StopCount++;
            Logger.Write($"SL hit! {price}");
            CloseSymbolPositions(setupId);
            ShowResultChart(sender);

            if (IsBacktesting || !TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportStopLoss(setupId);
        }

        /// <summary>
        /// Generates the second result chart for the setup finder
        /// </summary>
        /// <param name="setupFinder">The setup finder we want to check. See <see cref="T"/></param>
        private void ShowResultChart(object setupFinder)
        {
            if (!SaveChartForManualAnalysis || 
                setupFinder is not T sf ||
                !m_ChartFileFinderMap.TryGetValue(sf.Id, out TK signalEventArgs))
            {
                return;
            }

            GeneratePlotImageFile(sf, signalEventArgs, true);
            m_ChartFileFinderMap.Remove(sf.Id);
        }

        /// <summary>
        /// Called when on take profit. hit
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected void OnTakeProfit(object sender, LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }

            m_TakeCount++;
            Logger.Write($"TP hit! {price}");
            CloseSymbolPositions(setupId);
            ShowResultChart(sender);

            if (IsBacktesting || !TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportTakeProfit(setupId);
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
            
            if (!m_BarsMap.TryGetValue(sf.Id, out Bars bars))
            {
                return;
            }
            
            Symbol symbol = m_SymbolsMap[sf.Id];

            double tp = e.TakeProfit.Price;
            double sl = e.StopLoss.Price;
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

                if (HasSameSetupActive(finder,e))
                {
                    Logger.Write($"Already got this setup in on {finder.Symbol.Name} - {finder.TimeFrame}");
                    return;
                }
            }

            m_EnterCount++;
            m_PositionFinderMap[sf.Id] = true;
            GetEventStrings(sender, e.Level, out string price);
            Logger.Write($"New setup found! {price}");
            Symbol s = m_SymbolsMap[sf.Id];

            if (IsBacktesting || AllowToTrade)
            {
                TradeType type = isLong ? TradeType.Buy : TradeType.Sell;
                double priceNow = isLong ? s.Ask : s.Bid;

                double slP = Math.Round(Math.Abs(priceNow - sl) / sf.Symbol.PipSize);
                double tpP = Math.Round(Math.Abs(priceNow - tp) / sf.Symbol.PipSize);

                if (slP > 0)
                {
                    double volume = GetVolume(symbol, slP);
                    TradeResult order = ExecuteMarketOrder(
                    type, sf.Symbol.Name, volume, GetBotName(), slP, tpP);

                    if (order?.IsSuccessful == true)
                    {
                        order.Position.ModifyTakeProfitPrice(tp);
                        order.Position.ModifyStopLossPrice(sl);
                    }
                }
            }

            if (IsBacktesting && !SaveChartForManualAnalysis)
            {
                return;
            }
            
            Directory.CreateDirectory(Helper.DirectoryToSaveImages);
            if (!SaveChartForManualAnalysis)
            {
                foreach (string file in Directory.GetFiles(Helper.DirectoryToSaveImages))
                {
                    File.Delete(file);
                }
            }
            
            string plotImagePath = GeneratePlotImageFile(sf, e);
            if (SaveChartForManualAnalysis)
            {
                m_ChartFileFinderMap[sf.Id] = e;
            }

            if (!TelegramReporter.IsReady)
            {
                return;
            }

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
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="lastOpenDateTime">The last open date time.</param>
        protected virtual GenericChart.GenericChart[] GetAdditionalChartLayers(
            TK signalEventArgs, DateTime lastOpenDateTime)
        {
            double sl = signalEventArgs.StopLoss.Price;
            double tp = signalEventArgs.TakeProfit.Price;
            DateTime startView = signalEventArgs.StartViewBarTime;
            Color shortColor = Color.fromHex("#EF5350");
            Color longColor = Color.fromHex("#26A69A");
            GenericChart.GenericChart tpLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, tp), new(lastOpenDateTime, tp) },
                LineColor: new FSharpOption<Color>(longColor),
                ShowLegend: new FSharpOption<bool>(false), 
                LineDash: new FSharpOption<StyleParam.DrawingStyle>(StyleParam.DrawingStyle.Dash));
            GenericChart.GenericChart slLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, sl), new(lastOpenDateTime, sl) },
                LineColor: new FSharpOption<Color>(shortColor),
                ShowLegend: new FSharpOption<bool>(false),
                LineDash: new FSharpOption<StyleParam.DrawingStyle>(StyleParam.DrawingStyle.Dash));

            return new[] {tpLine, slLine};
        }

        /// <summary>
        /// Gets the path to the generated chart image file.
        /// </summary>
        /// <param name="setupFinder">Source setup finder</param>
        /// <param name="signalEventArgs">Signal info args</param>
        /// <param name="showTradeResult">True if we want to see the result of the first trade.</param>
        /// <returns>Path to file</returns>
        protected virtual string GeneratePlotImageFile(
            T setupFinder, TK signalEventArgs, bool showTradeResult = false)
        {
            DateTime startView = signalEventArgs.StartViewBarTime;
            IBarsProvider barProvider = setupFinder.BarsProvider;
            int firstIndex = barProvider.GetIndexByTime(signalEventArgs.StartViewBarTime);
            int earlyBar = Math.Max(0, firstIndex - CHART_BARS_MARGIN_COUNT);

            int lastIndex = barProvider.Count - 1;
            int barsCount = lastIndex - earlyBar;
            if (barsCount <= 0)
            {
                return null;
            }

            bool useCommonTimeFrame = TimeFrameHelper.TimeFrames
                .TryGetValue(barProvider.TimeFrame, out TimeFrameInfo timeFrameInfo);

            var o = new double[barsCount];
            var h = new double[barsCount];
            var c = new double[barsCount];
            var l = new double[barsCount];
            var d = new DateTime[barsCount];
            
            var rangeBreaks = new List<DateTime>();
            for (int i = earlyBar; i < lastIndex; i++)
            {
                int barIndex = i - earlyBar;
                DateTime currentDateTime = barProvider.GetOpenTime(i);
                o[barIndex] = barProvider.GetOpenPrice(i);
                h[barIndex] = barProvider.GetHighPrice(i);
                l[barIndex] = barProvider.GetLowPrice(i);
                c[barIndex] = barProvider.GetClosePrice(i);
                d[barIndex] = currentDateTime;

                if (!useCommonTimeFrame || i == earlyBar)
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
            }

            DateTime lastOpenDateTime = d[^1];
            DateTime lastCloseDateTime = useCommonTimeFrame
                ? lastOpenDateTime + timeFrameInfo.TimeSpan
                : lastOpenDateTime;

            Color blackColor = Color.fromARGB(255, 22, 26, 37);
            Color whiteColor = Color.fromARGB(255, 209, 212, 220);
            Color shortColor = Color.fromHex("#EF5350");
            Color longColor = Color.fromHex("#26A69A");

            GenericChart.GenericChart candlestickChart = Chart2D.Chart.Candlestick
                    <double, double, double, double, DateTime, string>(o, h, l, c, d,
                        IncreasingColor: new FSharpOption<Color>(longColor),
                        DecreasingColor: new FSharpOption<Color>(shortColor),
                        Name: barProvider.Symbol.Name,
                        ShowLegend: new FSharpOption<bool>(false));

            GenericChart.GenericChart[] layers = 
                GetAdditionalChartLayers(signalEventArgs, lastCloseDateTime) 
                ?? Array.Empty<GenericChart.GenericChart>();

            FSharpOption<int> dValue = timeFrameInfo == null
                ? null
                : new FSharpOption<int>((int) timeFrameInfo.TimeSpan.TotalMilliseconds);

            GenericChart.GenericChart resultChart = Plotly.NET.Chart.Combine(
                    layers.Concat(new[] {candlestickChart}))
                .WithTitle($@"{barProvider.Symbol.Name} {barProvider.TimeFrame.ShortName} {lastCloseDateTime:u} ",
                    new FSharpOption<Font>(Font.init(Size: new FSharpOption<double>(36))))
                .WithXAxisStyle(new Title(), ShowGrid: new FSharpOption<bool>(false))
                .WithYAxisStyle(new Title(), ShowGrid: new FSharpOption<bool>(false))
                .WithXAxisRangeSlider(RangeSlider.init(Visible: new FSharpOption<bool>(false)))
                .WithConfig(Config.init(
                    StaticPlot: new FSharpOption<bool>(true),
                    Responsive: new FSharpOption<bool>(false)))
                .WithLayout(Layout.init<string>(
                    PlotBGColor: new FSharpOption<Color>(blackColor),
                    PaperBGColor: new FSharpOption<Color>(blackColor),
                    Font: new FSharpOption<Font>(Font.init(
                        Color: new FSharpOption<Color>(whiteColor)))))
                .WithLayoutGrid(LayoutGrid.init(
                    Rows: new FSharpOption<int>(0),
                    Columns: new FSharpOption<int>(0),
                    XGap: new FSharpOption<double>(0),
                    YGap: new FSharpOption<double>(0)))
                .WithXAxis(LinearAxis.init<DateTime, DateTime, DateTime, DateTime, DateTime, DateTime>(
                    Rangebreaks: new FSharpOption<IEnumerable<Rangebreak>>(new[]
                        {
                            Rangebreak.init<string, string>(
                                new FSharpOption<bool>(rangeBreaks.Any()),
                                DValue: dValue,
                                Values: new FSharpOption<IEnumerable<string>>(
                                    rangeBreaks.Select(a => a.ToString("O"))))
                        }
                    )));

            string fileName = startView.ToString("s").Replace(":", "-");
            string postfix = string.Empty;
            if (SaveChartForManualAnalysis)
                postfix = showTradeResult ? SECOND_CHART_FILE_POSTFIX : FIRST_CHART_FILE_POSTFIX;

            string outPath = Path.Combine(Helper.DirectoryToSaveImages,
                $"{fileName}.{barProvider.Symbol.Name}.{barProvider.TimeFrame.ShortName}{postfix}");
            resultChart.SavePNG(outPath, null, CHART_WIDTH, CHART_HEIGHT);
            return $"{outPath}{CHART_FILE_TYPE_EXTENSION}";
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

        private void GetEventStrings(object sender, LevelItem level, out string price)
        {
            var sf = (T)sender;
            string priceFmt = level.Price.ToString($"F{sf.Symbol.Digits}", CultureInfo.InvariantCulture);
            price = $"Price:{priceFmt} ({sf.BarsProvider.GetOpenTime(level.Index.GetValueOrDefault()):s}) - {sf.Symbol.Name}";
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
                m_BarsMap[sf.Id].BarOpened -= BarOpened;
                m_SymbolsMap[sf.Id].Tick -= OnTick;
            }

            Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}
