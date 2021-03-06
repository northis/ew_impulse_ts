using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Internals;
using TradeKit.Config;

namespace TradeKit
{
    public class ImpulseSignalerBaseRobot : Robot
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";
        private const double RISK_DEPOSIT_PERCENT = 1;
        private const double RISK_DEPOSIT_PERCENT_MAX = 5;

        private double m_CurrentRisk = RISK_DEPOSIT_PERCENT;

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

        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;

        private Dictionary<string, SetupFinder> m_SetupFindersMap;
        private Dictionary<string, SetupFinder[]> m_SymbolFindersMap;
        private TelegramReporter m_TelegramReporter;
        private Dictionary<string, Bars> m_BarsMap;
        private Dictionary<string, Symbol> m_SymbolsMap;
        private Dictionary<string, bool> m_BarsInitMap;
        private Dictionary<string, bool> m_PositionFinderMap;

        private string[] SplitString(string str)
        {
            return str.Split(new[] {'|', ',', ';',}, StringSplitOptions.RemoveEmptyEntries);
        }

        protected override void OnStart()
        {
            Logger.SetWrite(a => Print(a));
            m_SetupFindersMap = new Dictionary<string, SetupFinder>();
            m_BarsMap = new Dictionary<string, Bars>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, Symbol>();
            m_SymbolFindersMap = new Dictionary<string, SetupFinder[]>();
            m_PositionFinderMap = new Dictionary<string, bool>();

            m_CurrentRisk = RiskPercentFromDeposit;

            string[] symbols = !UseSymbolsList || string.IsNullOrEmpty(SymbolsToProceed)
                ? new[] { SymbolName }
                : SplitString(SymbolsToProceed);
            string[] timeFrames = !UseTimeFramesList || string.IsNullOrEmpty(TimeFramesToProceed)
                ? new[] { TimeFrame.Name }
                : SplitString(TimeFramesToProceed);
            
            foreach (string symbolName in symbols)
            {
                if (!Symbols.Exists(symbolName))
                {
                    continue;
                }
                
                var finders = new List<SetupFinder>();
                foreach (string timeFrameStr in timeFrames)
                {
                    if (!TimeFrame.TryParse(timeFrameStr, out TimeFrame timeFrame))
                    {
                        continue;
                    }

                    var state = new SymbolState
                    {
                        Symbol = symbolName,
                        TimeFrame = timeFrame.Name
                    };

                    Bars bars = MarketData.GetBars(timeFrame, symbolName);
                    var barsProvider = new CTraderBarsProvider(bars);
                    Symbol symbolEntity = Symbols.GetSymbol(symbolName);
                    var sf = new SetupFinder(barsProvider, state, symbolEntity);
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

            Positions.Closed += OnPositionsClosed;

            m_TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId);
            Logger.Write($"OnStart is OK, is telegram ready: {m_TelegramReporter.IsReady}");
        }

        private double GetCurrentRisk
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
            if (!m_SymbolFindersMap.TryGetValue(obj.SymbolName, out SetupFinder[] finders))
            {
                return;
            }
            
            foreach (SetupFinder sf in finders)
            {
                sf.CheckTick(obj.Bid);
            }
        }

        private void UpRisk()
        {
            if (m_CurrentRisk >= RiskPercentFromDepositMax)
            {
                m_CurrentRisk = RiskPercentFromDepositMax;
                return;
            }

            m_CurrentRisk += RISK_DEPOSIT_PERCENT;
        }

        private void DownRisk()
        {
            if (m_CurrentRisk <= RiskPercentFromDeposit)
            {
                m_CurrentRisk = RiskPercentFromDeposit;
                return;
            }

            m_CurrentRisk -= RISK_DEPOSIT_PERCENT;
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

            string finderId = SetupFinder.GetId(bars.SymbolName, bars.TimeFrame.Name);
            if (!m_SetupFindersMap.TryGetValue(finderId, out SetupFinder sf))
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
            sf.State.IsInSetup = false;
            m_BarsInitMap[finderId] = true;
        }

        /// <summary>
        /// Closes the symbol positions.
        /// </summary>
        /// <param name="setupId">Id of the symbol.</param>
        private void CloseSymbolPositions(string setupId)
        {
            string symbolName = m_SetupFindersMap[setupId].State.Symbol;
            Position[] positionsToClose = Positions
                .Where(a => a.Label == BOT_NAME && a.SymbolName == symbolName)
                .ToArray();

            foreach (Position positionToClose in positionsToClose)
            {
                positionToClose.Close();
            }
        }

        private bool HandleClose(object sender, EventArgs.LevelEventArgs e, 
            out string price, out string setupId)
        {
            SetupFinder sf = (SetupFinder)sender;
            setupId = sf.Id;
            price = null;
            if (!m_PositionFinderMap.TryGetValue(sf.Id, out bool isInPosition))
            {
                return false;
            }

            GetEventStrings(sender, e.Level, out price, out SymbolInfo _);
            m_PositionFinderMap[sf.Id] = false;
            return isInPosition;
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }

            m_StopCount++;
            Logger.Write($"SL hit! {price}");
            CloseSymbolPositions(setupId);
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }
            
            m_TelegramReporter.ReportStopLoss(setupId);
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }

            m_TakeCount++;
            Logger.Write($"TP hit! {price}");
            CloseSymbolPositions(setupId);
            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }
            
            m_TelegramReporter.ReportTakeProfit(setupId);
        }

        private void OnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            SetupFinder sf = (SetupFinder)sender;
            if (!m_SymbolFindersMap.TryGetValue(sf.State.Symbol, out SetupFinder[] finders))
            {
                return;
            }

            if (!m_BarsMap.TryGetValue(sf.Id, out Bars bars))
            {
                return;
            }
            
            DateTime setupStart = bars.OpenTimes[e.StopLoss.Index];
            DateTime setupEnd = bars.OpenTimes[e.Level.Index] + TimeFrameHelper.TimeFrames[bars.TimeFrame].TimeSpan * 2;
            Symbol symbol = m_SymbolsMap[sf.Id];

            double tp = e.TakeProfit.Price;
            double sl = e.StopLoss.Price;
            bool isLong = sl < tp;
            double spread = symbol.Spread;

            if (isLong && symbol.Ask >= tp || 
                spread > 0 && Math.Abs(sl - tp) / spread < Helper.MAX_SPREAD_RATIO)
            {
                Logger.Write("Big spread, ignore the signal");
                return;
            }

            IReadonlyList<TradingSession> sessions = symbol.MarketHours.Sessions;
            TimeSpan safeTimeDurationStart = TimeSpan.FromHours(1);

            DateTime setupDayStart = setupStart
                .Subtract(setupStart.TimeOfDay)
                .AddDays(-(int) setupStart.DayOfWeek);
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

                if (setupStart > sessionDateTime && setupEnd < sessionEndTime)
                {
                    isSetupInDay = true;
                    break;
                }
            }
            
            if (!isSetupInDay)
            {
                Logger.Write(
                    $"A risky signal, the setup contains a trade session change: {symbol.Name}, {sf.State.TimeFrame}, {setupStart:s}-{setupEnd:s}");
                return;
            }

            foreach (SetupFinder finder in finders)
            {
                if (finder.Id == sf.Id)
                {
                    continue;
                }

                if (Math.Abs(finder.State.SetupStartPrice - e.StopLoss.Price) < double.Epsilon &&
                    Math.Abs(finder.State.SetupEndPrice - e.TakeProfit.Price) < double.Epsilon)
                {
                    Logger.Write($"Already got this setup in on {finder.State.Symbol} - {finder.State.TimeFrame}");
                    return;
                }
            }

            m_EnterCount++;
            m_PositionFinderMap[sf.Id] = true;
            GetEventStrings(sender, e.Level, out string price, out SymbolInfo symbolInfo);
            Logger.Write($"New setup found! {price}");
            Symbol s = m_SymbolsMap[sf.Id];

            if (IsBacktesting || AllowToTrade)
            {
                TradeType type = isLong ? TradeType.Buy : TradeType.Sell;
                double priceNow = isLong ? s.Ask : s.Bid;
                
                double slP = Math.Round(Math.Abs(priceNow - sl) / symbolInfo.PipSize);
                double tpP = Math.Round(Math.Abs(priceNow - tp) / symbolInfo.PipSize);

                if (slP > 0)
                {
                    double volP = Math.Round(Math.Abs(tp - sl) / symbolInfo.PipSize / 2);
                    double volume = s.GetVolume(GetCurrentRisk, Account.Balance, volP);
                    TradeResult order = ExecuteMarketOrder(
                    type, symbolInfo.Name, volume, BOT_NAME, slP, tpP);

                    if (order?.IsSuccessful == true)
                    {
                        order.Position.ModifyTakeProfitPrice(tp);
                        order.Position.ModifyStopLossPrice(sl);
                    }
                }
            }

            if (IsBacktesting || !m_TelegramReporter.IsReady)
            {
                return;
            }

            m_TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = s.Ask,
                Bid = s.Bid,
                Digits = symbolInfo.Digits,
                SignalEventArgs = e,
                SymbolName = symbolInfo.Name,
                SenderId = sf.Id
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
            foreach (SetupFinder sf in m_SetupFindersMap.Values)
            {
                sf.OnEnter -= OnEnter;
                sf.OnStopLoss -= OnStopLoss;
                sf.OnTakeProfit -= OnTakeProfit;
                m_BarsMap[sf.Id].BarOpened -= BarOpened;
                m_SymbolsMap[sf.Id].Tick -= OnTick;
                Positions.Closed -= OnPositionsClosed;
            }

            Logger.Write($"Enters: {m_EnterCount}; take profits: {m_TakeCount}; stop losses {m_StopCount}");
        }
    }
}