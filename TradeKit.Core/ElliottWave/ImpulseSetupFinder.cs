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
        private readonly double m_PrePatio;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        DeviationExtremumFinder m_PreFinder;
        public double m_MaxZigzagRatio;
        public double m_MaxOverlapseLengthRatio;

        private readonly Dictionary<DeviationExtremumFinder, Dictionary<DateTime, ImpulseResult>> m_ImpulseCache = new();

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
        internal ImpulseSignalEventArgs CurrentSignalEventArgs { get; set; }

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

            for (int i = impulseParams.Period; i <= impulseParams.Period * 2; i += 10)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ImpulseCache.Add(localFinder, new Dictionary<DateTime, ImpulseResult>());
                m_ExtremumFinders.Add(localFinder);
            }

            m_PrePatio = m_ImpulseParams.EnterRatio * 0.5;
            m_MaxZigzagRatio = impulseParams.MaxZigzagPercent / 100;
            m_MaxOverlapseLengthRatio = impulseParams.MaxOverlapseLengthPercent / 100;
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
            bool res = stats.OverlapseMaxDepth <= m_ImpulseParams.MaxOverlapseLengthPercent / 100 &&
                       stats.RatioZigzag <= m_ImpulseParams.MaxZigzagPercent / 100 && stats.RatioZigzag > 0.005 &&
                       stats.HeterogeneityMax <= m_ImpulseParams.HeterogeneityMax / 100 &&
                       stats.Size >= m_ImpulseParams.MinSizePercent / 100;// &&
                       //stats.CandlesCount < 90 &&
                       //stats.Area <= 0.1;

            //stats.OverlapseDegree / stats.OverlapseMaxDepth > 0.5;
            return res;
        }

        private bool GotFlat(BarPoint startC, BarPoint endC)
        {
            bool isImpulseUp = endC > startC;
            double waveCLength = Math.Abs(startC - endC);

            foreach (DeviationExtremumFinder extremumFinder in m_ExtremumFinders)
            {
                BarPoint[] testExtrema = extremumFinder.Extrema
                    .TakeWhile(a => a.Key <= startC.OpenTime)
                    .TakeLast(3)
                    .Select(a => a.Value)
                    .ToArray();

                if (testExtrema.Length != 3 || testExtrema[2].OpenTime != startC.OpenTime)
                    continue;

                BarPoint startB = testExtrema[1];
                BarPoint startA = testExtrema[0];

                if (isImpulseUp != startA < startB ||
                    isImpulseUp != startA > startC)
                    continue;

                double waveALength = Math.Abs(startB - startA);
                if (waveALength < double.Epsilon)
                    continue;

                double ratio = waveCLength / waveALength;
                if (ratio is > 0.618 and < 0.7 or > 1 and < 1.1 or > 1.618 and < 1.7)
                    return true;
            }

            return false;
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
            double startValue = startItem.Value.Value;
            double endValue = endItem.Value.Value;
            bool isImpulseUp = endValue > startValue;
            bool hasInCache = m_ImpulseCache[finder].ContainsKey(endItem.Key);

            double GetRealPrice(double triggerLevel)
            {
                double realPrice1;
                if (triggerLevel >= low && triggerLevel <= high)
                {
                    realPrice1 = currentPriceBid ?? triggerLevel;
                }
                else if (Math.Abs(triggerLevel - low) < Math.Abs(triggerLevel - high))
                {
                    realPrice1 = currentPriceBid ?? low;
                }
                else
                {
                    realPrice1 = currentPriceBid ?? high;
                }

                return realPrice1;
            }

            bool IsExpired(double realPrice)
            {
                return isImpulseUp &&
                       (realPrice >= SetupEndPrice || realPrice <= SetupStartPrice) ||
                       !isImpulseUp &&
                       (realPrice <= SetupEndPrice || realPrice >= SetupStartPrice);
            }

            if (!IsInSetup)
            {
                if (hasInCache && m_ImpulseCache[finder][endItem.Key] == null)
                {
                    return false;
                }

                //int impulseBarCount = endItem.Value.BarIndex - startItem.Value.BarIndex;
                //if (index < endItem.Value.BarIndex + impulseBarCount)
                //    return;
                Candle edgeExtremum = null;
                bool isInitialMove = hasInCache || IsInitialMovement(
                    startValue, endValue, startItem.Value.BarIndex, finder, out edgeExtremum);
                if (!isInitialMove)
                {
                    // The move (impulse candidate) is no longer initial.
                    m_ImpulseCache[finder][endItem.Key] = null;
                    return false;
                }

                ImpulseResult stats = hasInCache && m_ImpulseCache[finder][endItem.Key] != null
                    ? m_ImpulseCache[finder][endItem.Key]
                    : MovementStatistic.GetMovementStatistic(
                        startItem.Value, endItem.Value, BarsProvider, m_MaxOverlapseLengthRatio, m_MaxZigzagRatio);
                if (!hasInCache &&
                    (stats.CandlesCount < m_ImpulseParams.BarsCount || !IsSmoothImpulse(stats)))
                {
                    m_ImpulseCache[finder][endItem.Key] = null;
                    return false;
                }

                if (!hasInCache)
                {
                    double max = isImpulseUp ? endValue : startValue;
                    double min = isImpulseUp ? startValue : endValue;
                    for (int i = endItem.Value.BarIndex + 1; i < index; i++)
                    {
                        if (max <= BarsProvider.GetHighPrice(i) ||
                            min >= BarsProvider.GetLowPrice(i))
                        {
                            m_ImpulseCache[finder][endItem.Key] = null;
                            return false;
                            // The setup is no longer valid, TP or SL is already hit.  
                        }
                    }
                }

                if (!hasInCache)
                {
                    stats.EdgeExtremum = edgeExtremum;
                    m_ImpulseCache[finder][endItem.Key] = stats;
                }

                edgeExtremum ??= m_ImpulseCache[finder][endItem.Key].EdgeExtremum;

                int edgeIndex = edgeExtremum.Index.GetValueOrDefault();
                //double channelRatio = (startItem.Value.BarIndex - edgeIndex) / (double)stats.CandlesCount;
                //if (channelRatio < m_ImpulseParams.ChannelRatio)
                //{
                //    return;
                //}

                bool gotSetup = GotSetup(m_ImpulseParams.EnterRatio, endValue, startValue, isImpulseUp,
                    out double triggerLevel, low, high);

                //if (!GotSetup(m_ImpulseParams.EnterRatio, endValue, startValue, isImpulseUp, out double triggerLevel, low, high))
                //{
                //    if (m_PreFinder == null && GotSetup(m_PrePatio, endValue, startValue, isImpulseUp, out triggerLevel, low, high))
                //    {
                //        m_PreFinder = finder;
                //    }
                //    return false;
                //}

                m_PreFinder = null;

                var outExtrema = new ImpulseElliottModelResult
                {
                    Wave0 = startItem.Value,
                    Wave5 = endItem.Value
                };

                if (SetupStartIndex == startItem.Value.BarIndex ||
                    SetupEndIndex == endItem.Value.BarIndex)
                {
                    // Cannot use the same impulse twice.
                    return false;
                }

                if (endItem.Value.BarIndex == index)
                {
                    // Wait for the next bar
                    return false;
                }

                var realPrice = GetRealPrice(triggerLevel);

                TriggerLevel = triggerLevel;
                TriggerBarIndex = index;
                IsInSetup = true;

                double endAllowance = Math.Abs(triggerLevel - endValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
                double startAllowance = Math.Abs(triggerLevel - startValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

                SetupStartIndex = startItem.Value.BarIndex;
                SetupEndIndex = endItem.Value.BarIndex;

                double tpRatio = m_ImpulseParams.TakeRatio;
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

                if (IsExpired(realPrice))
                {
                    // TP or SL is already hit, cannot use this signal
                    Logger.Write($"{Symbol}, {TimeFrame}: TP or SL is already hit, cannot use this signal");
                    IsInSetup = false;
                    return false;
                }

                var tpArg = new BarPoint(SetupEndPrice, SetupEndIndex, BarsProvider);
                var slArg = new BarPoint(SetupStartPrice, SetupStartIndex, BarsProvider);
                DateTime viewDateTime = BarsProvider.GetOpenTime(edgeIndex);

                bool hasFlat = GotFlat(startItem.Value, endItem.Value);
                CurrentStatistic = $"{stats};{hasFlat:F2}";
                CurrentSignalEventArgs = new ImpulseSignalEventArgs(
                    new BarPoint(gotSetup ? realPrice : triggerLevel, index, BarsProvider),
                    tpArg,
                    slArg,
                    outExtrema,
                    viewDateTime,
                    CurrentStatistic, m_ImpulseParams.BreakEvenRatio is > 0 and <= 1
                        ? m_ImpulseParams.BreakEvenRatio
                        : null,
                    !gotSetup);
                OnEnterInvoke(CurrentSignalEventArgs);
                // Here we should give a trade signal.
            }

            if (!IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            isImpulseUp = SetupEndPrice > SetupStartPrice;

            if (CurrentSignalEventArgs.IsLimit)
            {
                Debugger.Launch();
                double price = GetRealPrice(TriggerLevel);
                if (IsExpired(price))
                {
                    OnCanceledInvokeInner();
                    IsInSetup = false;
                    return false;
                }

                if (!CurrentSignalEventArgs.IsActive &&
                         GotSetup(m_ImpulseParams.EnterRatio, endValue, 
                             startValue, isImpulseUp, out double _, low, high))
                {
                    CurrentSignalEventArgs.IsActive = true;
                    OnActivatedInvokeInner();
                }
            }

            bool isProfitHit = isImpulseUp && high >= SetupEndPrice
                               || !isImpulseUp && low <= SetupEndPrice;

            if (isProfitHit)
            {
                IsInSetup = false;
                OnTakeProfitInvoke(new LevelEventArgs(new BarPoint(SetupEndPrice, index, BarsProvider),
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentStatistic));
                m_ImpulseCache[finder].Clear();
            }

            bool isStopHit = isImpulseUp && low <= SetupStartPrice
                             || !isImpulseUp && high >= SetupStartPrice;
            if (isStopHit)
            {
                IsInSetup = false;
                OnStopLossInvoke(new LevelEventArgs(new BarPoint(SetupStartPrice, index, BarsProvider),
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentStatistic));
                m_ImpulseCache[finder].Clear();
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
            OnBreakEvenInvokeInner();

            return IsInSetup;
        }

        private void OnCanceledInvokeInner()
        {
            OnCanceledInvoke(new LevelEventArgs(CurrentSignalEventArgs.Level, new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentSignalEventArgs.Comment));
        }

        private void OnActivatedInvokeInner()
        {
            OnActivatedInvoke(new LevelEventArgs(CurrentSignalEventArgs.Level, new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider), false, CurrentSignalEventArgs.Comment));
        }

        private void OnBreakEvenInvokeInner()
        {
            OnBreakEvenInvoke(new LevelEventArgs(CurrentSignalEventArgs.StopLoss, CurrentSignalEventArgs.Level, true, CurrentSignalEventArgs.Comment));
        }

        private bool GotSetup(double levelRatio, double endValue, double startValue, bool isImpulseUp, out double triggerLevel, double low, double high)
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
