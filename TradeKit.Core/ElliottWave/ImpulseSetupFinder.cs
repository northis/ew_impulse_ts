using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{

    /// <summary>
    /// Class contains the EW impulse logic of trade setups searching.
    /// </summary>
    public class ImpulseSetupFinder : SingleSetupFinder<ImpulseSignalEventArgs>
    {
        private readonly ITradeViewManager m_TradeViewManager;
        private readonly ImpulseParams m_ImpulseParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        private readonly double m_MaxZigzagRatio;
        private readonly double m_MaxOverlapseLengthRatio;

        private readonly Dictionary<DeviationExtremumFinder, Dictionary<DateTime, ImpulseResult>> m_ImpulseCache = new();
        
        private const double LIMIT_RATIO = 0.8;

        private const int IMPULSE_END_NUMBER = 2;
        private const int IMPULSE_START_NUMBER = 3;
        // We want to collect at least this number of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out whether this impulse is initial or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 3;

        public int SetupStartIndex { get; set; }
        public int SetupEndIndex { get; set; }
        public double SetupStartPrice { get; set; }
        public double SetupEndPrice { get; set; }
        public double TriggerLevel { get; set; }
        public int TriggerBarIndex { get; set; }
        internal string CurrentStatistic { get; set; }
        internal ImpulseSignalEventArgs CurrentSignalEventArgs { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bar provider.</param>
        /// <param name="tradeViewManager">The trade manager (read only).</param>
        /// <param name="impulseParams">The impulse parameters.</param>
        public ImpulseSetupFinder(
            IBarsProvider mainBarsProvider,
            ITradeViewManager tradeViewManager,
            ImpulseParams impulseParams)
            : base(mainBarsProvider, mainBarsProvider.BarSymbol)
        {
            m_TradeViewManager = tradeViewManager;
            m_ImpulseParams = impulseParams;

            for (int i = impulseParams.Period; i <= impulseParams.Period * 4; i += 10)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ImpulseCache.Add(localFinder, new Dictionary<DateTime, ImpulseResult>());
                m_ExtremumFinders.Add(localFinder);
            }

            m_MaxZigzagRatio = impulseParams.MaxZigzagPercent / 100;
            m_MaxOverlapseLengthRatio = impulseParams.MaxOverlapseLengthPercent / 100;
        }

        private bool IsSmoothImpulse(ImpulseResult stats)
        {
            bool res = stats.OverlapseMaxDepth <= m_ImpulseParams.MaxOverlapseLengthPercent / 100 &&
                        stats.RatioZigzag <= m_ImpulseParams.MaxZigzagPercent / 100 &&
                       stats.HeterogeneityMax <= m_ImpulseParams.HeterogeneityMax / 100 &&
                       stats.Size >= m_ImpulseParams.MinSizePercent / 100 &&
                       //stats.CandlesCount < 90 &&
                       stats.Area <= m_ImpulseParams.AreaPercent / 100;

            //stats.OverlapseDegree / stats.OverlapseMaxDepth > 0.5;
            return res;
        }
        
        /// <summary>
        /// Determines whether the data for a specified index contains a trade setup.
        /// </summary>
        /// <param name="openDateTime">Open datetime of the current candle.</param>
        /// <param name="finder">The extreme finder instance.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(DateTime openDateTime, DeviationExtremumFinder finder, double? currentPriceBid = null)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            SortedList<DateTime, BarPoint> extrema = finder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return false;
            }
            
            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            BarPoint startItem = extrema.Values[startIndex];
            BarPoint endItem = extrema.Values[endIndex];
            BarPoint lastItem = extrema.Values[^1];

            bool isInSetupBefore = IsInSetup;
            double startValue = startItem.Value;
            double endValue = endItem.Value;
            bool isImpulseUp = endValue > startValue;
            bool hasInCache = m_ImpulseCache[finder].ContainsKey(endItem.OpenTime);

            if (!IsInSetup)
            {
                if (isImpulseUp && (lastItem > endItem || lastItem < startItem) ||
                    !isImpulseUp && (lastItem < endItem || lastItem > startItem))
                {
                    return false;
                }
                
                if (!CheckForSignal(new CheckSignalArgs(index, finder, currentPriceBid, hasInCache, endItem, startValue, endValue, startItem, isImpulseUp, low, high))) return false;
            }

            if (!IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            isImpulseUp = CurrentSignalEventArgs.TakeProfit >
                          CurrentSignalEventArgs.StopLoss;
            bool isProfitHit = isImpulseUp && high >= CurrentSignalEventArgs.TakeProfit.Value
                               || !isImpulseUp && low <= CurrentSignalEventArgs.TakeProfit.Value;

            bool needToCheckLimit = CurrentSignalEventArgs.IsLimit &&
                                    !CurrentSignalEventArgs.IsActive;
            if (isProfitHit)
            {
                IsInSetup = false;
                LevelEventArgs levelArgs = new LevelEventArgs(
                    CurrentSignalEventArgs.TakeProfit.WithIndex(index,
                        BarsProvider),
                    CurrentSignalEventArgs.Level, false,
                    CurrentSignalEventArgs.Comment);
                if (needToCheckLimit)
                    OnCanceledInvoke(levelArgs);
                else
                    OnTakeProfitInvoke(levelArgs);
                m_ImpulseCache[finder].Clear();
            }

            bool isStopHit = isImpulseUp && low <= CurrentSignalEventArgs.StopLoss.Value
                             || !isImpulseUp && high >= CurrentSignalEventArgs.StopLoss.Value;
            if (isStopHit)
            {
                IsInSetup = false;
                LevelEventArgs levelArgs = new LevelEventArgs(
                    CurrentSignalEventArgs.StopLoss.WithIndex(index,
                        BarsProvider),
                    CurrentSignalEventArgs.Level, false,
                    CurrentSignalEventArgs.Comment);
                if (needToCheckLimit)
                    OnCanceledInvoke(levelArgs);
                else
                    OnStopLossInvoke(levelArgs);
                m_ImpulseCache[finder].Clear();
            }

            if (IsInSetup && needToCheckLimit && 
                (isImpulseUp && low <= CurrentSignalEventArgs.Level.Value || 
                 !isImpulseUp && high >= CurrentSignalEventArgs.Level.Value))
            {
                CurrentSignalEventArgs.IsActive = true;
                var levelArgs = new LevelEventArgs(CurrentSignalEventArgs.Level, CurrentSignalEventArgs.Level, false, CurrentStatistic);
                OnActivatedInvoke(levelArgs);
            }

            if (CurrentSignalEventArgs is not { CanUseBreakeven: true, HasBreakeven: false })
                return IsInSetup;

            if (!CurrentSignalEventArgs.CanUseBreakeven ||
                ((!isImpulseUp || !(CurrentSignalEventArgs.BreakEvenPrice <= high)) &&
                 (isImpulseUp || !(CurrentSignalEventArgs.BreakEvenPrice >= low)))) 
                return IsInSetup;

            DateTime currentDt = BarsProvider.GetOpenTime(index);
            CurrentSignalEventArgs.HasBreakeven = true;
            CurrentSignalEventArgs.StopLoss = new BarPoint(
                CurrentSignalEventArgs.BreakEvenPrice, currentDt, CurrentSignalEventArgs.StopLoss.BarTimeFrame,
                index);
            OnBreakEvenInvoke(new LevelEventArgs(CurrentSignalEventArgs.StopLoss, CurrentSignalEventArgs.Level, true, CurrentSignalEventArgs.Comment));

            return IsInSetup;
        }

        private bool CheckForSignal(CheckSignalArgs checkSignalArgs)
        {
            if (checkSignalArgs.HasInCache && 
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] == null)
            {
                return false;
            }

            //int impulseBarCount = endItem.Value.BarIndex - startItem.Value.BarIndex;
            //if (index < endItem.Value.BarIndex + impulseBarCount)
            //    return;
            Candle edgeExtremum = null;
            bool isInitialMove = checkSignalArgs.HasInCache || IsInitialMovement(
                checkSignalArgs.StartValue, checkSignalArgs.EndValue, 
                checkSignalArgs.StartItem.BarIndex, checkSignalArgs.Finder.BarsProvider, out edgeExtremum);
            if (!isInitialMove)
            {
                // The move (impulse candidate) is no longer initial.
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] = null;
                //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: CheckForSignal: {checkSignalArgs.EndItem.Key} is no longer initial");
                return false;
            }

            ImpulseResult stats = checkSignalArgs.HasInCache && m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] != null
                ? m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime]
                : MovementStatistic.GetMovementStatistic(
                    checkSignalArgs.StartItem, checkSignalArgs.EndItem, BarsProvider, m_MaxOverlapseLengthRatio, m_MaxZigzagRatio);
            if (!checkSignalArgs.HasInCache &&
                (stats.CandlesCount < m_ImpulseParams.BarsCount || !IsSmoothImpulse(stats)))
            {
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] = null;
                //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: CheckForSignal: not smooth enough ({stats}, {checkSignalArgs.EndItem:o})");
                return false;
            }

            if (!checkSignalArgs.HasInCache)
            {
                double max = checkSignalArgs.IsImpulseUp ? checkSignalArgs.EndValue : checkSignalArgs.StartValue;
                double min = checkSignalArgs.IsImpulseUp ? checkSignalArgs.StartValue : checkSignalArgs.EndValue;
                for (int i = checkSignalArgs.EndItem.BarIndex + 1; i < checkSignalArgs.Index; i++)
                {
                    if (max <= BarsProvider.GetHighPrice(i) ||
                        min >= BarsProvider.GetLowPrice(i))
                    {
                        m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] = null;
                        //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: CheckForSignal: The setup is no longer valid, TP or SL is already hit");
                        return false;
                        // The setup is no longer valid, TP or SL is already hit.  
                    }
                }
            }

            if (!checkSignalArgs.HasInCache)
            {
                stats.EdgeExtremum = edgeExtremum;
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime] = stats;
            }

            edgeExtremum ??= m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.OpenTime].EdgeExtremum;

            int edgeIndex = edgeExtremum.Index.GetValueOrDefault();
            //double channelRatio = (startItem.Value.BarIndex - edgeIndex) / (double)stats.CandlesCount;
            //if (channelRatio < m_ImpulseParams.ChannelRatio)
            //{
            //    return;
            //}

            var gotSetupArgs = new GotSetupArgs(
                m_ImpulseParams.EnterRatio,
                checkSignalArgs.EndValue,
                checkSignalArgs.StartValue,
                checkSignalArgs.IsImpulseUp,
                checkSignalArgs.Low,
                checkSignalArgs.High);

            bool gotSetupMain = GotSetup(gotSetupArgs, out double triggerLevel);
            bool useLimit = false;
            if (!gotSetupMain)
            {
                gotSetupArgs.LevelRatio *= LIMIT_RATIO;
                if (GotSetup(gotSetupArgs, out _))
                    useLimit = true;
                else
                {
                    //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: CheckForSignal: not at the level yet");
                    return false;
                }
            }

            var signalArgs = new SignalArgs(
                checkSignalArgs.Index,
                checkSignalArgs.CurrentPriceBid,
                checkSignalArgs.StartItem,
                checkSignalArgs.EndItem,
                triggerLevel,
                checkSignalArgs.Low,
                checkSignalArgs.High,
                checkSignalArgs.EndValue,
                checkSignalArgs.StartValue,
                checkSignalArgs.IsImpulseUp,
                edgeIndex,
                stats)
            {
                UseLimit = useLimit
            };

            return IssueSignal(signalArgs);
        }

        private bool IssueSignal(SignalArgs signalArgs)
        {
            var outExtrema = new ImpulseElliottModelResult
            {
                Wave0 = signalArgs.StartItem,
                Wave5 = signalArgs.EndItem
            };

            if (SetupStartIndex == signalArgs.StartItem.BarIndex ||
                SetupEndIndex == signalArgs.EndItem.BarIndex)
            {
                // Cannot use the same impulse twice.
                //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: Cannot use the same impulse twice");
                return false;
            }

            if (signalArgs.EndItem.BarIndex == signalArgs.Index)
            {
                // Wait for the next bar
                Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: Wait for the next bar");
                return false;
            }

            if (m_TradeViewManager.IsBigSpread(Symbol, signalArgs.EndItem.Value,
                    signalArgs.StartItem.Value))
            {
                //Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: big spread, lets wait for a while");
                //return false;
            }

            double realPrice;
            if (signalArgs.TriggerLevel >= signalArgs.Low && signalArgs.TriggerLevel <= signalArgs.High)
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.TriggerLevel;
            }
            else if (Math.Abs(signalArgs.TriggerLevel - signalArgs.Low) < Math.Abs(signalArgs.TriggerLevel - signalArgs.High))
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.Low;
            }
            else
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.High;
            }

            TriggerLevel = realPrice;
            TriggerBarIndex = signalArgs.Index;
            IsInSetup = true;

            double endAllowance = Math.Abs(realPrice - signalArgs.EndValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
            double startAllowance = Math.Abs(realPrice - signalArgs.StartValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

            SetupStartIndex = signalArgs.StartItem.BarIndex;
            SetupEndIndex = signalArgs.EndItem.BarIndex;

            double tpRatio = m_ImpulseParams.TakeRatio;
            double setupLength = Math.Abs(signalArgs.StartValue - signalArgs.EndValue) * tpRatio;

            if (signalArgs.IsImpulseUp)
            {
                signalArgs.EndValue = signalArgs.StartValue + setupLength;
                SetupStartPrice = Math.Round(signalArgs.StartValue - startAllowance, Symbol.Digits, MidpointRounding.ToZero);
                SetupEndPrice = Math.Round(signalArgs.EndValue - endAllowance, Symbol.Digits, MidpointRounding.ToZero);
            }
            else
            {
                signalArgs.EndValue = signalArgs.StartValue - setupLength;
                SetupStartPrice = Math.Round(
                    signalArgs.StartValue + startAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                SetupEndPrice = Math.Round(
                    signalArgs.EndValue + endAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            if (signalArgs.IsImpulseUp &&
                (realPrice >= SetupEndPrice || realPrice <= SetupStartPrice) ||
                !signalArgs.IsImpulseUp &&
                (realPrice <= SetupEndPrice || realPrice >= SetupStartPrice))
            {
                // TP or SL is already hit, cannot use this signal
                Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: TP or SL is already hit, cannot use this signal");
                IsInSetup = false;

                if (CurrentSignalEventArgs.IsLimit)
                {
                    LevelEventArgs levelArgs = new LevelEventArgs(
                        CurrentSignalEventArgs.Level,
                        new BarPoint(signalArgs.TriggerLevel,
                            signalArgs.Index, BarsProvider));
                    OnCanceledInvoke(levelArgs);
                }
                
                return false;
            }

            var tpArg = new BarPoint(SetupEndPrice, SetupEndIndex, BarsProvider);
            var slArg = new BarPoint(SetupStartPrice, SetupStartIndex, BarsProvider);
            DateTime viewDateTime = BarsProvider.GetOpenTime(signalArgs.EdgeIndex);

            CurrentStatistic = signalArgs.Stats.ToString();
            CurrentSignalEventArgs = new ImpulseSignalEventArgs(
                new BarPoint(
                    signalArgs.UseLimit ? signalArgs.TriggerLevel : realPrice,
                    signalArgs.Index, BarsProvider),
                tpArg,
                slArg,
                outExtrema,
                viewDateTime,
                CurrentStatistic, m_ImpulseParams.BreakEvenRatio is > 0 and <= 1
                    ? m_ImpulseParams.BreakEvenRatio
                    : null,
                signalArgs.UseLimit);
            Logger.Write($"{Symbol.Name}, {TimeFrame.ShortName}: On before Enter");
            OnEnterInvoke(CurrentSignalEventArgs);
            return true;
        }

        private bool GotSetup(GotSetupArgs gotSetupArgs, out double triggerLevel)
        {
            double triggerSize = Math.Abs(gotSetupArgs.EndValue - gotSetupArgs.StartValue) * gotSetupArgs.LevelRatio;

            bool gotSetup;
            if (gotSetupArgs.IsImpulseUp)
            {
                triggerLevel = Math.Round(
                    gotSetupArgs.EndValue - triggerSize, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                gotSetup = gotSetupArgs.Low <= triggerLevel && gotSetupArgs.Low > gotSetupArgs.StartValue;
            }
            else
            {
                triggerLevel = Math.Round(
                    gotSetupArgs.EndValue + triggerSize, Symbol.Digits, MidpointRounding.ToZero);
                gotSetup = gotSetupArgs.High >= triggerLevel && gotSetupArgs.High < gotSetupArgs.StartValue;
            }

            return gotSetup;
        }

        /// <summary>
        /// Checks whether a setup condition is satisfied at the specified open date and time.
        /// </summary>
        /// <param name="openDateTime">The open date and time to check the setup against.</param>
        protected override void CheckSetup(DateTime openDateTime)
        {
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders)
            {
                finder.OnCalculate(openDateTime);
                if (!IsInitialized)
                    continue;
                
                if (IsSetup(openDateTime, finder))
                {
                    break;
                }
            }
        }
    }
}
