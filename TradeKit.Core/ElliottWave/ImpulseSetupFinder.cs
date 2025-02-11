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
        private readonly ImpulseParams m_ImpulseParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        DeviationExtremumFinder m_PreFinder;

        private const double TRIGGER_PRE_LEVEL_RATIO = 0.2;
        private const double TRIGGER_LEVEL_RATIO = 0.35;

        private const int IMPULSE_END_NUMBER = 1;
        private const int IMPULSE_START_NUMBER = 2;
        // We want to collect at lease this amount of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out, weather this impulse is an initial one or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 2;

        public int SetupStartIndex { get; set; }
        public int SetupEndIndex { get; set; }
        
        public double SetupStartPrice { get; set; }
        
        public double SetupEndPrice { get; set; }
        
        public double TriggerLevel { get; set; }
        
        public int TriggerBarIndex { get; set; }
        internal string CurrentStatistic { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bar provider.</param>
        /// <param name="impulseParams">The impulse parameters.</param>
        public ImpulseSetupFinder(
            IBarsProvider mainBarsProvider,
            ImpulseParams impulseParams)
            : base(mainBarsProvider, mainBarsProvider.BarSymbol)
        {
            m_ImpulseParams = impulseParams;
            var localFinder = new DeviationExtremumFinder(impulseParams.Period, BarsProvider);
            m_ExtremumFinders.Add(localFinder);
        }

        /// <summary>
        /// Determines whether the movement from <see cref="startValue"/> to <see cref="endValue"/> is initial. We use current bar position and <see cref="IMPULSE_START_NUMBER"/> to rewind the bars to the past.
        /// </summary>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="finder">The extremum finder instance.</param>
        /// <param name="edgeExtremum">The extremum from the end of the movement to the previous counter-movement or how far this movement went away from the price channel.</param>
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInitialMovement(
            double startValue, 
            double endValue, 
            int startIndex, 
            DeviationExtremumFinder finder,
            out Candle edgeExtremum)
        {
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            edgeExtremum = null;

            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                edgeExtremum = Candle.FromIndex(finder.BarsProvider, curIndex);

                if (isImpulseUp)
                {
                    if (edgeExtremum.L <= startValue)
                    {
                        break;
                    }

                    if (edgeExtremum.H - endValue > 0)
                    {
                        isInitialMove = true;
                        break;
                    }

                    continue;
                }

                if (edgeExtremum.H >= startValue)
                {
                    break;
                }

                if (!(edgeExtremum.L - endValue < 0))
                {
                    continue;
                }

                isInitialMove = true;
                break;
            }

            return isInitialMove;
        }

        private bool IsSmoothImpulse(ImpulseResult stats)
        {
            bool res = stats.HeterogeneityDegree <= m_ImpulseParams.HeterogeneityDegreePercent / 100 &&
                       stats.HeterogeneityMax <= m_ImpulseParams.HeterogeneityMax / 100 &&
                       stats.OverlapseDegree <= m_ImpulseParams.MaxOverlapsePercent / 100 &&
                       stats.OverlapseMaxDepth <= m_ImpulseParams.MaxOverlapseLengthPercent / 100;
            return res;
        }
        
        /// <summary>
        /// Determines whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="finder">The extreme finder instance.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(int index, DeviationExtremumFinder finder, double? currentPriceBid = null)
        {
            SortedDictionary<DateTime, BarPoint> extrema = finder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return false;
            }
            
            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            KeyValuePair<DateTime, BarPoint> startItem = extrema
                .ElementAt(startIndex);
            KeyValuePair<DateTime, BarPoint> endItem = extrema
                .ElementAt(endIndex);

            bool isInSetupBefore = IsInSetup;
            void CheckImpulse()
            {
                double startValue = startItem.Value.Value;
                double endValue = endItem.Value.Value;

                bool isImpulseUp = endValue > startValue;

                ImpulseResult stats = MovementStatistic.GetMovementStatistic(
                    startItem.Value, endItem.Value, BarsProvider);
                if (stats.CandlesCount < m_ImpulseParams.BarsCount)
                    return;

                if (stats.Size < m_ImpulseParams.MinSizePercent / 100)
                    return;

                double max = isImpulseUp ? endValue : startValue;
                double min = isImpulseUp ? startValue : endValue;
                for (int i = endItem.Value.BarIndex + 1; i < index; i++)
                {
                    if (max <= BarsProvider.GetHighPrice(i) ||
                        min >= BarsProvider.GetLowPrice(i))
                    {
                        return;
                        // The setup is no longer valid, TP or SL is already hit.
                    }
                }

                bool isInitialMove = IsInitialMovement(
                    startValue, endValue, startItem.Value.BarIndex, finder, out Candle edgeExtremum);
                if (!isInitialMove)
                {
                    // The move (impulse candidate) is no longer initial.
                    return;
                }

                int edgeIndex = edgeExtremum.Index.GetValueOrDefault();
                double channelRatio = (startItem.Value.BarIndex - edgeIndex) / (double)stats.CandlesCount;
                if (channelRatio < m_ImpulseParams.ChannelRatio)
                {
                    return;
                }

                double triggerLevel;
                bool GotSetup(double levelRatio)
                {
                    double triggerSize = Math.Abs(endValue - startValue) * levelRatio;

                    bool gotSetup;
                    if (isImpulseUp)
                    {
                        triggerLevel = Math.Round(
                            endValue - triggerSize, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                        gotSetup = low <= triggerLevel && low > startValue;
                    }
                    else
                    {
                        triggerLevel = Math.Round(
                            endValue + triggerSize, Symbol.Digits, MidpointRounding.ToZero);
                        gotSetup = high >= triggerLevel && high < startValue;
                    }

                    return gotSetup;
                }

                if (!GotSetup(TRIGGER_LEVEL_RATIO))
                {
                    if (m_PreFinder == null && GotSetup(TRIGGER_PRE_LEVEL_RATIO))
                    {
                        m_PreFinder = finder;
                    }
                    return;
                }

                m_PreFinder = null;
                if (!IsSmoothImpulse(stats))
                {
                    // The move is not a smooth impulse.
                    // Logger.Write($"{m_Symbol}, {State.TimeFrame}: setup is not an impulse");
                    return;
                }

                var outExtrema = new ImpulseElliottModelResult
                {
                    Wave0 = startItem.Value, 
                    Wave5 = endItem.Value
                };

                if (SetupStartIndex == startItem.Value.BarIndex ||
                    SetupEndIndex == endItem.Value.BarIndex)
                {
                    // Cannot use the same impulse twice.
                    return;
                }

                if (endItem.Value.BarIndex == index)
                {
                    // Wait for the next bar
                    return;
                }

                double realPrice;
                if (triggerLevel >= low && triggerLevel <= high)
                {
                    realPrice = currentPriceBid ?? triggerLevel;
                }
                else if (Math.Abs(triggerLevel - low) < Math.Abs(triggerLevel - high))
                {
                    realPrice = currentPriceBid ?? low;
                }
                else
                {
                    realPrice = currentPriceBid ?? high;
                }

                TriggerLevel = realPrice;
                TriggerBarIndex = index;
                IsInSetup = true;
                
                double endAllowance = Math.Abs(realPrice - endValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
                double startAllowance = Math.Abs(realPrice - startValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

                SetupStartIndex = startItem.Value.BarIndex;
                SetupEndIndex = endItem.Value.BarIndex;

                double tpRatio = 1.6;
                double setupLength = Math.Abs(startValue - endValue) * tpRatio;
                
                if (isImpulseUp)
                {
                    endValue = startValue + setupLength;
                    SetupStartPrice = Math.Round(startValue - startAllowance, Symbol.Digits, MidpointRounding.ToZero);
                    SetupEndPrice = Math.Round(endValue - endAllowance, Symbol.Digits, MidpointRounding.ToZero);
                }
                else
                {
                    endValue = startValue - setupLength;
                    SetupStartPrice = Math.Round(
                        startValue + startAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                    SetupEndPrice = Math.Round(
                        endValue + endAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                }

                if (isImpulseUp && 
                    (realPrice>= SetupEndPrice || realPrice <= SetupStartPrice) ||
                    !isImpulseUp &&
                    (realPrice <= SetupEndPrice || realPrice >= SetupStartPrice))
                {
                    // TP or SL is already hit, cannot use this signal
                    Logger.Write($"{Symbol}, {TimeFrame}: TP or SL is already hit, cannot use this signal");
                    IsInSetup = false;
                    return;
                }

                var tpArg = new BarPoint(SetupEndPrice, SetupEndIndex, BarsProvider);
                var slArg = new BarPoint(SetupStartPrice, SetupStartIndex, BarsProvider);
                DateTime viewDateTime = BarsProvider.GetOpenTime(edgeIndex);

                CurrentStatistic = $"{stats};{channelRatio:F2}";
                OnEnterInvoke(new ImpulseSignalEventArgs(
                    new BarPoint(realPrice, index, BarsProvider),
                    tpArg,
                    slArg,
                    outExtrema,
                    viewDateTime,
                    CurrentStatistic));
                // Here we should give a trade signal.
            }

            if (!IsInSetup)
            {
                for (;;)
                {
                    CheckImpulse();
                    if (IsInSetup)
                    {
                        break;
                    }
                    // We don't know how far we are from the nearest initial impulse
                    // so we go deep and check

                    if (index - startItem.Value.BarIndex > Helper.BARS_DEPTH)
                    {
                        //Logger.Write($"{m_Symbol}, {State.TimeFrame}: maximum bar depth is exceeded");
                        break;
                    }

                    startIndex -= 1;
                    endIndex -= 1;
                    if (startIndex < 0 || endIndex < 0)
                    {
                        break;
                    }

                    startItem = extrema.ElementAt(startIndex);
                    endItem = extrema.ElementAt(endIndex);

                    // If we are no longer between start and end of the impulse
                    if (startItem.Value.Value >= low && endItem.Value.Value >= low ||
                        startItem.Value.Value <= high && endItem.Value.Value <= high)
                    {
                        break;
                    }
                }
            }

            if (!IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            bool isImpulseUp = SetupEndPrice > SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= SetupEndPrice
                               || !isImpulseUp && low <= SetupEndPrice;

            if (isProfitHit)
            {
                IsInSetup = false;
                OnTakeProfitInvoke(new LevelEventArgs(new BarPoint(SetupEndPrice, index, BarsProvider),
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentStatistic));
            }

            bool isStopHit = isImpulseUp && low <= SetupStartPrice
                             || !isImpulseUp && high >= SetupStartPrice;
            if (isStopHit)
            {
                IsInSetup = false;
                OnStopLossInvoke(new LevelEventArgs(new BarPoint(SetupStartPrice, index, BarsProvider),
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentStatistic));
            }

            return IsInSetup;
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        protected override void CheckSetup(int index)
        {
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders)
            {
                finder.OnCalculate(index, BarsProvider.GetOpenTime(index));
                if (IsSetup(LastBar, finder))
                {
                    break;
                }
            }
        }
    }
}
