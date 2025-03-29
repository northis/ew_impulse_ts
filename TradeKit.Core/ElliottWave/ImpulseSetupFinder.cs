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
        public double m_MaxZigzagRatio;
        public double m_MaxOverlapseLengthRatio;

        public SetupItem CurrentSetupItem { get; } = new();

        private const int IMPULSE_END_NUMBER = 1;
        private const int IMPULSE_START_NUMBER = 2;
        // We want to collect at lease this amount of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out, weather this impulse is an initial one or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 2;

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
                       stats.RatioZigzag <= m_ImpulseParams.MaxZigzagPercent / 100 &&
                       stats.HeterogeneityMax <= m_ImpulseParams.HeterogeneityMax / 100;
                       //stats.OverlapseDegree / stats.OverlapseMaxDepth > 0.5;
            return res;
        }

        public override void CheckTick(SymbolTickEventArgs tick)
        {
            base.CheckTick(tick);
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

            if (!IsInSetup)
            {
                for (;;)
                {
                    CheckImpulse(startItem, endItem, finder, index, low, high, in currentPriceBid);
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
            bool isImpulseUp = CurrentSetupItem.SetupEndPrice > CurrentSetupItem.SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= CurrentSetupItem.SetupEndPrice
                               || !isImpulseUp && low <= CurrentSetupItem.SetupEndPrice;

            if (isProfitHit)
            {
                IsInSetup = false;
                OnTakeProfitInvoke(new LevelEventArgs(new BarPoint(CurrentSetupItem.SetupEndPrice, index, BarsProvider),
                    new BarPoint(CurrentSetupItem.TriggerLevel, CurrentSetupItem.TriggerBarIndex, BarsProvider), false, CurrentSetupItem.CurrentStatistic));
            }

            bool isStopHit = isImpulseUp && low <= CurrentSetupItem.SetupStartPrice
                             || !isImpulseUp && high >= CurrentSetupItem.SetupStartPrice;
            if (isStopHit)
            {
                IsInSetup = false;
                OnStopLossInvoke(new LevelEventArgs(new BarPoint(CurrentSetupItem.SetupStartPrice, index, BarsProvider),
                    new BarPoint(CurrentSetupItem.TriggerLevel, CurrentSetupItem.TriggerBarIndex, BarsProvider), false, CurrentSetupItem.CurrentStatistic));
            }

            if (CurrentSetupItem.CurrentSignalEventArgs is not { CanUseBreakeven: true, HasBreakeven: false })
                return IsInSetup;

            if (!CurrentSetupItem.CurrentSignalEventArgs.CanUseBreakeven ||
                ((!isImpulseUp || !(CurrentSetupItem.CurrentSignalEventArgs.BreakEvenPrice <= high)) &&
                 (isImpulseUp || !(CurrentSetupItem.CurrentSignalEventArgs.BreakEvenPrice >= low)))) 
                return IsInSetup;

            DateTime currentDt = BarsProvider.GetOpenTime(index);
            CurrentSetupItem.CurrentSignalEventArgs.HasBreakeven = true;
            CurrentSetupItem.CurrentSignalEventArgs.StopLoss = new BarPoint(
                CurrentSetupItem.CurrentSignalEventArgs.BreakEvenPrice, currentDt, CurrentSetupItem.CurrentSignalEventArgs.StopLoss.BarTimeFrame,
                index);
            OnBreakEvenInvoke(new LevelEventArgs(CurrentSetupItem.CurrentSignalEventArgs.StopLoss, CurrentSetupItem.CurrentSignalEventArgs.Level, true, CurrentSetupItem.CurrentSignalEventArgs.Comment));

            return IsInSetup;
        }

        private void CheckImpulse(KeyValuePair<DateTime, BarPoint> startItem, KeyValuePair<DateTime, BarPoint> endItem, DeviationExtremumFinder finder, int index, double low, double high, in double? currentPriceBid)
        {
            double startValue = startItem.Value.Value;
            double endValue = endItem.Value.Value;

            //int impulseBarCount = endItem.Value.BarIndex - startItem.Value.BarIndex;
            //if (index < endItem.Value.BarIndex + impulseBarCount)
            //    return;

            bool isImpulseUp = endValue > startValue;
            bool isInitialMove = IsInitialMovement(
                startValue, endValue, startItem.Value.BarIndex, finder, out Candle edgeExtremum);
            if (!isInitialMove)
            {
                // The move (impulse candidate) is no longer initial.
                return;
            }

            ImpulseResult stats = null;
            bool hasPreFinder = CurrentSetupItem.PreFinder == finder;
            if (!hasPreFinder)
            {
                stats = MovementStatistic.GetMovementStatistic(
                    startItem.Value, endItem.Value, BarsProvider, m_MaxOverlapseLengthRatio, m_MaxZigzagRatio);
                if (stats.CandlesCount < m_ImpulseParams.BarsCount ||
                    !IsSmoothImpulse(stats))
                    return;
                CurrentSetupItem.IsSmoothImpulse = true;
            }

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

            int edgeIndex = edgeExtremum.Index.GetValueOrDefault();
            //double channelRatio = (startItem.Value.BarIndex - edgeIndex) / (double)stats.CandlesCount;
            //if (channelRatio < m_ImpulseParams.ChannelRatio)
            //{
            //    return;
            //}

            if (!CurrentSetupItem.IsSmoothImpulse)
            {
                return;
            }

            if (!GotSetup(m_ImpulseParams.EnterRatio,
                    endValue, startValue, isImpulseUp, out double triggerLevel, low,
                    high))
            {
                if (!hasPreFinder && GotSetup(m_PrePatio,
                        endValue, startValue, isImpulseUp, out triggerLevel,
                        low, high))
                {
                    CurrentSetupItem.PreFinder = finder;
                }

                return;
            }

            var outExtrema = new ImpulseElliottModelResult
            {
                Wave0 = startItem.Value, 
                Wave5 = endItem.Value
            };

            if (CurrentSetupItem.SetupStartIndex == startItem.Value.BarIndex ||
                CurrentSetupItem.SetupEndIndex == endItem.Value.BarIndex)
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

            CurrentSetupItem.TriggerLevel = realPrice;
            CurrentSetupItem.TriggerBarIndex = index;
            IsInSetup = true;
                
            double endAllowance = Math.Abs(realPrice - endValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
            double startAllowance = Math.Abs(realPrice - startValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

            CurrentSetupItem.SetupStartIndex = startItem.Value.BarIndex;
            CurrentSetupItem.SetupEndIndex = endItem.Value.BarIndex;

            double tpRatio = m_ImpulseParams.TakeRatio;
            double setupLength = Math.Abs(startValue - endValue) * tpRatio;
                
            if (isImpulseUp)
            {
                endValue = startValue + setupLength;
                CurrentSetupItem.SetupStartPrice = Math.Round(startValue - startAllowance, Symbol.Digits, MidpointRounding.ToZero);
                CurrentSetupItem.SetupEndPrice = Math.Round(endValue - endAllowance, Symbol.Digits, MidpointRounding.ToZero);
            }
            else
            {
                endValue = startValue - setupLength;
                CurrentSetupItem.SetupStartPrice = Math.Round(
                    startValue + startAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                CurrentSetupItem.SetupEndPrice = Math.Round(
                    endValue + endAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            if (isImpulseUp && 
                (realPrice>= CurrentSetupItem.SetupEndPrice || realPrice <= CurrentSetupItem.SetupStartPrice) ||
                !isImpulseUp &&
                (realPrice <= CurrentSetupItem.SetupEndPrice || realPrice >= CurrentSetupItem.SetupStartPrice))
            {
                // TP or SL is already hit, cannot use this signal
                Logger.Write($"{Symbol}, {TimeFrame}: TP or SL is already hit, cannot use this signal");
                IsInSetup = false;
                return;
            }

            CurrentSetupItem.PreFinder = null;
            CurrentSetupItem.IsSmoothImpulse = false;

            var tpArg = new BarPoint(CurrentSetupItem.SetupEndPrice, CurrentSetupItem.SetupEndIndex, BarsProvider);
            var slArg = new BarPoint(CurrentSetupItem.SetupStartPrice, CurrentSetupItem.SetupStartIndex, BarsProvider);
            DateTime viewDateTime = BarsProvider.GetOpenTime(edgeIndex);
            
            if (stats != null)
            {
                CurrentSetupItem.CurrentStatistic = stats.ToString();//$"{stats};{channelRatio:F2}";
            }

            CurrentSetupItem.CurrentSignalEventArgs = new ImpulseSignalEventArgs(
                new BarPoint(realPrice, index, BarsProvider),
                tpArg,
                slArg,
                outExtrema,
                viewDateTime,
                CurrentSetupItem.CurrentStatistic, m_ImpulseParams.BreakEvenRatio is > 0 and <= 1
                    ? m_ImpulseParams.BreakEvenRatio 
                    : null);
            OnEnterInvoke(CurrentSetupItem.CurrentSignalEventArgs);
            // Here we should give a trade signal.
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

        public class SetupItem
        {
            public int SetupStartIndex { get; set; }
            public int SetupEndIndex { get; set; }
            public double SetupStartPrice { get; set; }
            public double SetupEndPrice { get; set; }
            public double TriggerLevel { get; set; }
            public int TriggerBarIndex { get; set; }
            internal string CurrentStatistic { get; set; }
            internal ImpulseSignalEventArgs CurrentSignalEventArgs { get; set; }
            public DeviationExtremumFinder PreFinder { get; set; }

            public bool IsSmoothImpulse { get; set; }
        }
    }
}
