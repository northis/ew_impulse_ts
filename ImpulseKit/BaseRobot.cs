using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Internals;
using TradeKit.Config;
using TradeKit.EventArgs;

namespace TradeKit
{
    public abstract class BaseRobot<T> : Robot where T: BaseSetupFinder
    {
        protected const double RISK_DEPOSIT_PERCENT = 1;
        protected const double RISK_DEPOSIT_PERCENT_MAX = 5;
        private double m_CurrentRisk = RISK_DEPOSIT_PERCENT;
        protected TelegramReporter TelegramReporter;
        private Dictionary<string, T> m_SetupFindersMap;
        private Dictionary<string, T[]> m_SymbolFindersMap;
        private Dictionary<string, Bars> m_BarsMap;
        private Dictionary<string, Symbol> m_SymbolsMap;
        private Dictionary<string, bool> m_BarsInitMap;
        private Dictionary<string, bool> m_PositionFinderMap;
        private int m_EnterCount;
        private int m_TakeCount;
        private int m_StopCount;

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

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public abstract string GetBotName();

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

        protected void UpRisk()
        {
            if (m_CurrentRisk >= RiskPercentFromDepositMax)
            {
                m_CurrentRisk = RiskPercentFromDepositMax;
                return;
            }
            
            m_CurrentRisk += RISK_DEPOSIT_PERCENT;
        }

        protected void DownRisk()
        {
            if (m_CurrentRisk <= RiskPercentFromDeposit)
            {
                m_CurrentRisk = RiskPercentFromDeposit;
                return;
            }

            m_CurrentRisk -= RISK_DEPOSIT_PERCENT;
        }

        private string[] SplitString(string str)
        {
            return str.Split(new[] { '|', ',', ';', }, StringSplitOptions.RemoveEmptyEntries);
        }

        protected override void OnStart()
        {
            Logger.SetWrite(a => Print(a));
            m_SetupFindersMap = new Dictionary<string, T>();
            m_BarsMap = new Dictionary<string, Bars>();
            m_BarsInitMap = new Dictionary<string, bool>();
            m_SymbolsMap = new Dictionary<string, Symbol>();
            m_SymbolFindersMap = new Dictionary<string, T[]>();
            m_PositionFinderMap = new Dictionary<string, bool>();
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

                var finders = new List<T>();
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
                    Symbol symbolEntity = Symbols.GetSymbol(symbolName);
                    T sf = CreateSetupFinder(bars, state, symbolEntity);
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
            TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId);
            Logger.Write($"OnStart is OK, is telegram ready: {TelegramReporter.IsReady}");
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected abstract T CreateSetupFinder(Bars bars, SymbolState state, Symbol symbolEntity);

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
            if (!m_SymbolFindersMap.TryGetValue(obj.SymbolName, out T[] finders))
            {
                return;
            }

            foreach (T sf in finders)
            {
                sf.CheckTick(obj.Bid);
            }
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

            string finderId = BaseSetupFinder.GetId(bars.SymbolName, bars.TimeFrame.Name);
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
                .Where(a => a.Label == GetBotName() && a.SymbolName == symbolName)
                .ToArray();

            foreach (Position positionToClose in positionsToClose)
            {
                positionToClose.Close();
            }
        }

        private bool HandleClose(object sender, LevelEventArgs e,
            out string price, out string setupId)
        {
            ImpulseSetupFinder sf = (ImpulseSetupFinder)sender;
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

        private void OnStopLoss(object sender, LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }

            m_StopCount++;
            Logger.Write($"SL hit! {price}");
            CloseSymbolPositions(setupId);
            if (IsBacktesting || !TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportStopLoss(setupId);
        }

        private void OnTakeProfit(object sender, LevelEventArgs e)
        {
            if (!HandleClose(sender, e, out string price, out string setupId))
            {
                return;
            }

            m_TakeCount++;
            Logger.Write($"TP hit! {price}");
            CloseSymbolPositions(setupId);
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
        /// <param name="signal">The <see cref="SignalEventArgs"/> instance containing the event data.</param>
        /// <returns>
        ///   <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected abstract bool HasSameSetupActive(T setupFinder, SignalEventArgs signal);

        private void OnEnter(object sender, SignalEventArgs e)
        {
            var sf = (T)sender;
            if (!m_SymbolFindersMap.TryGetValue(sf.State.Symbol, out T[] finders))
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

                if (!AllowEnterOnBigSpread)
                    return;
            }

            IReadonlyList<TradingSession> sessions = symbol.MarketHours.Sessions;
            TimeSpan safeTimeDurationStart = TimeSpan.FromHours(1);

            DateTime setupDayStart = setupStart
                .Subtract(setupStart.TimeOfDay)
                .AddDays(-(int)setupStart.DayOfWeek);
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

                if (!AllowOvernightTrade)
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
                    double volume = Symbol.GetVolume(RISK_DEPOSIT_PERCENT, Account.Balance, slP);
                    TradeResult order = ExecuteMarketOrder(
                    type, symbolInfo.Name, volume, GetBotName(), slP, tpP);

                    if (order?.IsSuccessful == true)
                    {
                        order.Position.ModifyTakeProfitPrice(tp);
                        order.Position.ModifyStopLossPrice(sl);
                    }
                }
            }

            if (IsBacktesting || !TelegramReporter.IsReady)
            {
                return;
            }

            TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
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
            var sf = (T)sender;
            symbolInfo = Symbols.GetSymbolInfo(sf.State.Symbol);
            string priceFmt = level.Price.ToString($"F{symbolInfo.Digits}", CultureInfo.InvariantCulture);
            price = $"Price:{priceFmt} ({sf.BarsProvider.GetOpenTime(level.Index):s}) - {sf.State.Symbol}";
        }

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
