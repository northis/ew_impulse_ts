using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Impulse
{
    /// <summary>
    /// Class contains the EW impulse logic of trade setups searching.
    /// </summary>
    public class ImpulseSetupFinder : SingleSetupFinder<ImpulseSignalEventArgs>
    {
        private readonly BarProvidersFactory m_BarProvidersFactory;
        private readonly PivotPointsFinder m_PivotPointsFinder;
        private readonly List<ExtremumFinder> m_ExtremumFinders = new();
        ExtremumFinder m_PreFinder;
        private readonly ElliottWavePatternFinder m_PatternFinder;

        private const double TRIGGER_PRE_LEVEL_RATIO = 0.4;
        private const double TRIGGER_LEVEL_RATIO = 0.5;

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

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="barProvidersFactory">The bar provider factory.</param>
        public ImpulseSetupFinder(
            IBarsProvider mainBarsProvider, 
            BarProvidersFactory barProvidersFactory)
            : base(mainBarsProvider, mainBarsProvider.Symbol)
        {
            m_BarProvidersFactory = barProvidersFactory;
            m_PivotPointsFinder = new PivotPointsFinder(Helper.PIVOT_PERIOD, BarsProvider);

            for (int i = Helper.MIN_IMPULSE_SCALE;
                 i <= Helper.MAX_IMPULSE_SCALE;
                 i += Helper.STEP_IMPULSE_SCALE)
            {
                m_ExtremumFinders.Add(new ExtremumFinder(i, BarsProvider));
            }

            m_PatternFinder = new ElliottWavePatternFinder(
                BarsProvider.TimeFrame, barProvidersFactory);
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
            ExtremumFinder finder,
            out BarPoint edgeExtremum)
        {
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            edgeExtremum = null;

            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                edgeExtremum = finder.Extrema.ElementAt(curIndex).Value;
                double curValue = edgeExtremum.Value;

                if (isImpulseUp)
                {
                    if (curValue <= startValue)
                    {
                        break;
                    }

                    if (curValue - endValue > 0)
                    {
                        isInitialMove = true;
                        break;
                    }

                    continue;
                }

                if (curValue >= startValue)
                {
                    break;
                }

                if (!(curValue - endValue < 0))
                {
                    continue;
                }

                isInitialMove = true;
                break;
            }

            return isInitialMove;
        }

        private DateTime? GetPrevious(
            SortedSet<DateTime> sortedSet,
            DateTime currentDt)
        {
            DateTime? prevDt = null;

            foreach (DateTime dt in sortedSet.Reverse())
            {
                if (dt < currentDt)
                {
                    prevDt = dt;
                    break;
                }
            }

            if (prevDt == null)
                return null;

            return prevDt;
        }

        void PreparePivotPointsFinder(
            DateTime startDate, DateTime endDate, DateTime endImpulseDate, bool isUp)
        {
            int endIndex = BarsProvider.GetIndexByTime(endDate);
            int startIndex = BarsProvider.GetIndexByTime(startDate);
            double period = endIndex - startIndex;

            period /= 1.5;
            for (;;)
            {
                m_PivotPointsFinder.Reset((int)period);
                m_PivotPointsFinder.Calculate(startDate, endImpulseDate);

                if (period < Helper.PIVOT_PERIOD)
                    return;

                if (isUp)
                {
                    if (m_PivotPointsFinder.LowExtrema.Count > 1 &&
                        m_PivotPointsFinder.LowExtrema.Contains(endDate) &&
                        m_PivotPointsFinder.HighExtrema.Count > 0)
                        return;
                }
                else
                {
                    if (m_PivotPointsFinder.HighExtrema.Count > 1 &&
                        m_PivotPointsFinder.HighExtrema.Contains(endDate) &&
                        m_PivotPointsFinder.LowExtrema.Count > 0)
                        return;
                }

                period /= 1.5;
            }
        }

        private bool CheckChannel(
            BarPoint impulseStart, BarPoint impulseEnd, DateTime viewDt, out BarPoint[] bars)
        {
            bool isUp = impulseEnd > impulseStart;

            // We want to discover view zone (before the impulse) + the impulse itself
            // to be able to calculate extremum points.
            PreparePivotPointsFinder(
                viewDt, impulseStart.OpenTime, impulseEnd.OpenTime, isUp);
            bars = Array.Empty<BarPoint>();

            if (isUp)
            {
                if (!m_PivotPointsFinder.LowValues.ContainsKey(impulseStart.OpenTime))
                    return false;

            }
            else
            {
                if (!m_PivotPointsFinder.HighValues.ContainsKey(impulseStart.OpenTime))
                    return false;
            }

            DateTime? prevLowDt = GetPrevious(
                m_PivotPointsFinder.LowExtrema,
                impulseStart.OpenTime);
            if (prevLowDt == null)
                return false;

            DateTime? prevHighDt = GetPrevious(
                m_PivotPointsFinder.HighExtrema,
                impulseStart.OpenTime);
            if (prevHighDt == null)
                return false;
            
            int prevLowBarIndex = BarsProvider.GetIndexByTime(prevLowDt.Value);
            int prevHighBarIndex = BarsProvider.GetIndexByTime(prevHighDt.Value);
            double prevLowBarValue = m_PivotPointsFinder.LowValues[prevLowDt.Value];
            double prevHighBarValue = m_PivotPointsFinder.HighValues[prevHighDt.Value];
            
            double k;
            double b;
            if (isUp)
            {
                k = (impulseStart.Value - prevLowBarValue)
                    / ( impulseStart.BarIndex - prevLowBarIndex);
                b = prevHighBarValue - prevHighBarIndex * k;
            }
            else
            {
                k = (impulseStart.Value - prevHighBarValue)
                    / (impulseStart.BarIndex - prevHighBarIndex);
                b = prevLowBarValue - prevLowBarIndex * k;
            }
            
            double edgeChannel = k * impulseEnd.BarIndex + b;

            bool res = isUp 
                ? impulseEnd.Value > edgeChannel 
                : impulseEnd.Value < edgeChannel;

            if (res)
            {
                bars = new BarPoint[4];

                bars[isUp ? 2 : 0] = new BarPoint(prevHighBarValue, prevHighBarIndex, BarsProvider);
                bars[1] = impulseStart;
                bars[isUp ? 0 : 2] = new BarPoint(prevLowBarValue, prevLowBarIndex, BarsProvider);
                bars[3] = new BarPoint(edgeChannel, impulseEnd.BarIndex, BarsProvider);
            }

            return true;// TODO use this value
        }
        
        /// <summary>
        /// Determines whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="finder">The extremum finder instance.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(int index, ExtremumFinder finder, double? currentPriceBid = null)
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
                int barsCount = endItem.Value.BarIndex - startItem.Value.BarIndex + 1;
                if (barsCount < Helper.MINIMUM_BARS_IN_IMPULSE)
                {
                    //Debugger.Launch();
                    //Logger.Write($"{m_Symbol}, {State.TimeFrame}: too few bars");
                    return;
                }

                double startValue = startItem.Value.Value;
                double endValue = endItem.Value.Value;

                bool isImpulseUp = endValue > startValue;
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
                    startValue, endValue, startIndex, finder, out BarPoint edgeExtremum);
                if (!isInitialMove)
                {
                    // The move (impulse candidate) is no longer initial.
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
                if (!m_PatternFinder.IsImpulse(startItem.Value, endItem.Value, out ImpulseElliottModelResult outExtrema))
                {
                    // The move is not an impulse.
                    // Logger.Write($"{m_Symbol}, {State.TimeFrame}: setup is not an impulse");
                    return;
                }

                //if (!CheckChannel(startItem.Value, endItem.Value, edgeExtremum.OpenTime,
                //        out BarPoint[] channelBarPoints))
                //{
                //    return;
                //}

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

                double setupLength = Math.Abs(startValue - endValue);
                
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
                DateTime viewDateTime = edgeExtremum.OpenTime;

                string waves = $"Wave 2 {outExtrema.Wave2Type}, wave 4 {outExtrema.Wave4Type}";
                string comment = waves;

                OnEnterInvoke(new ImpulseSignalEventArgs(
                    new BarPoint(realPrice, index, BarsProvider),
                    tpArg,
                    slArg,
                    outExtrema,
                    viewDateTime,
                    comment, Array.Empty<BarPoint>()));
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
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider)));
            }

            bool isStopHit = isImpulseUp && low <= SetupStartPrice
                             || !isImpulseUp && high >= SetupStartPrice;
            if (isStopHit)
            {
                IsInSetup = false;
                OnStopLossInvoke(new LevelEventArgs(new BarPoint(SetupStartPrice, index, BarsProvider),
                    new BarPoint(TriggerLevel, TriggerBarIndex, BarsProvider)));
            }

            return IsInSetup;
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        protected override void CheckSetup(int index, double? currentPriceBid = null)
        {
            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                finder.Calculate(index);

                if (finder.Extrema.Count > Helper.EXTREMA_MAX)
                { 
                   //Logger.Write("Cleaning the extrema...");
                   DateTime[] oldKeys = finder.Extrema.Keys
                        .Take(finder.Extrema.Count - Helper.EXTREMA_MAX)
                        .ToArray();
                    foreach (DateTime oldKey in oldKeys)
                    {
                        finder.Extrema.Remove(oldKey);
                    }
                }
            }

            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                if (IsSetup(LastBar, finder))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public override void CheckTick(double bid)
        {
            if (m_PreFinder == null)
            {
                return;
            }
            
            IsSetup(LastBar, m_PreFinder, bid);
        }
    }
}
