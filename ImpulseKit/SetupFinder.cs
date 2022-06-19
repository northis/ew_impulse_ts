using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Config;
using TradeKit.EventArgs;

namespace TradeKit
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// </summary>
    public class SetupFinder
    {
        private readonly PatternFinder m_PatternFinder;
        private readonly List<ExtremumFinder> m_ExtremumFinders = new();
        private int m_LastBarIndex;
        ExtremumFinder m_PreFinder;

        private const double TRIGGER_PRE_LEVEL_RATIO = 0.236;
        private const double TRIGGER_LEVEL_RATIO = 0.5;
        
        private const int IMPULSE_END_NUMBER = 1;
        private const int IMPULSE_START_NUMBER = 2;
        // We want to collect at lease this amount of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out, weather this impulse is an initial one or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 2;

        /// <summary>
        /// Gets the state.
        /// </summary>
        public SymbolState State { get; }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        public IBarsProvider BarsProvider { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupFinder"/> class.
        /// </summary>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="state">The state.</param>
        public SetupFinder(double correctionAllowancePercent, IBarsProvider barsProvider, SymbolState state)
        {
            BarsProvider = barsProvider;
            State = state;
            for (double d = Helper.DEVIATION_MAX;
                 d >= Helper.DEVIATION_MIN;
                 d -= Helper.DEVIATION_STEP)
            {
                m_ExtremumFinders.Add(new ExtremumFinder(d, BarsProvider));
            }

            m_PatternFinder = new PatternFinder(correctionAllowancePercent, barsProvider);
        }

        /// <summary>
        /// Occurs on stop loss.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnStopLoss;

        /// <summary>
        /// Occurs when on take profit.
        /// </summary>
        public event EventHandler<LevelEventArgs> OnTakeProfit;

        /// <summary>
        /// Occurs when a new setup is found.
        /// </summary>
        public event EventHandler<SignalEventArgs> OnEnter;

        /// <summary>
        /// Determines whether the movement from <see cref="startValue"/> to <see cref="endValue"/> is initial. We use current bar position and <see cref="IMPULSE_START_NUMBER"/> to rewind the bars to the past.
        /// </summary>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="finder">The extremum finder instance.</param>
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInitialMovement(
            double startValue, double endValue, int startIndex, ExtremumFinder finder)
        {
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                double curValue = finder.Extrema.ElementAt(curIndex).Value.Value;
                if (isImpulseUp)
                {
                    if (curValue <= startValue)
                    {
                        break;
                    }

                    if (curValue > endValue)
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

                if (curValue < endValue)
                {
                    isInitialMove = true;
                    break;
                }
            }

            return isInitialMove;
        }

        /// <summary>
        /// Determines whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="finder">The extremum finder instance.</param>
        /// <param name="currentPrice">The current price.</param>
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(int index, ExtremumFinder finder, double? currentPrice = null)
        {
            SortedDictionary<int, Extremum> extrema = finder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return false;
            }

            double low = currentPrice ?? BarsProvider.GetLowPrice(index);
            double high = currentPrice ?? BarsProvider.GetHighPrice(index);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            KeyValuePair<int, Extremum> startItem = extrema
                .ElementAt(startIndex);
            KeyValuePair<int, Extremum> endItem = extrema
                .ElementAt(endIndex);
            
            bool isInSetupBefore = State.IsInSetup;
            void CheckImpulse()
            {
                if (endItem.Key - startItem.Key < Helper.MINIMUM_BARS_IN_IMPULSE)
                {
                    return;
                }

                double startValue = startItem.Value.Value;
                double endValue = endItem.Value.Value;

                var isImpulseUp = endValue > startValue;
                double maxValue = Math.Max(startValue, endValue);
                double minValue = Math.Min(startValue, index);
                for (int i = endItem.Key + 1; i < index; i++)
                {
                    if (maxValue <= BarsProvider.GetHighPrice(i) ||
                        minValue >= BarsProvider.GetLowPrice(i))
                    {
                        return;
                        // The setup is no longer valid, TP or SL is already hit.
                    }
                }

                bool isInitialMove = IsInitialMovement(
                    startValue, endValue, startIndex, finder);
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
                        triggerLevel = endValue - triggerSize;
                        gotSetup = low <= triggerLevel && low > startValue;
                    }
                    else
                    {
                        triggerLevel = endValue + triggerSize;
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

                bool isImpulse = m_PatternFinder.IsImpulse(
                    startItem.Value, endItem.Value, finder.DeviationPercent, out List<Extremum> outExtrema);
                if (!isImpulse)
                {
                    // The move is not an impulse.
                    return;
                }

                if (State.SetupStartIndex == startItem.Key ||
                    State.SetupEndIndex == endItem.Key)
                {
                    // Cannot use the same impulse twice.
                    return;
                }

                if (endItem.Key == index)
                {
                    // Wait for the next bar
                    return;
                }

                State.TriggerLevel = triggerLevel;
                State.TriggerBarIndex = index;
                State.IsInSetup = true;

                double endAllowance = Math.Abs(triggerLevel - endValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
                double startAllowance = Math.Abs(triggerLevel - startValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

                State.SetupStartIndex = startItem.Key;
                State.SetupEndIndex = endItem.Key;

                if (isImpulseUp)
                {
                    State.SetupStartPrice = startValue - startAllowance;
                    State.SetupEndPrice = endValue - endAllowance;
                }
                else
                {
                    State.SetupStartPrice = startValue + startAllowance;
                    State.SetupEndPrice = endValue + endAllowance;
                }

                var tpArg = new LevelItem(State.SetupEndPrice, State.SetupEndIndex);
                var slArg = new LevelItem(State.SetupStartPrice, State.SetupStartIndex);

                double realPrice;
                if (triggerLevel >= low && triggerLevel <= high)
                {
                    realPrice = triggerLevel;
                }
                else if (Math.Abs(triggerLevel - low) < Math.Abs(triggerLevel - high))
                {
                    realPrice = low;
                }
                else
                {
                    realPrice = high;
                }

                if (isImpulseUp && 
                    (realPrice>= State.SetupEndPrice || realPrice <= State.SetupStartPrice) ||
                    !isImpulseUp &&
                    (realPrice <= State.SetupEndPrice || realPrice >= State.SetupStartPrice))
                {
                    // TP or SL is already hit, cannot use this signal
                    return;
                }
                
                OnEnter?.Invoke(this,
                    new SignalEventArgs(
                        new LevelItem(realPrice, index),
                        tpArg,
                        slArg,
                        outExtrema));
                // Here we should give a trade signal.
            }

            if (!State.IsInSetup)
            {
                for (;;)
                {
                    CheckImpulse();
                    if (State.IsInSetup)
                    {
                        break;
                    }
                    // We don't know how far we are from the nearest initial impulse
                    // so we go deep and check

                    if (index - startIndex > Helper.BARS_DEPTH)
                    {
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

            if (!State.IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            bool isImpulseUp = State.SetupEndPrice > State.SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= State.SetupEndPrice
                               || !isImpulseUp && low <= State.SetupEndPrice;

            if (isProfitHit)
            {
                State.IsInSetup = false;
                OnTakeProfit?.Invoke(this,
                    new LevelEventArgs(new LevelItem(State.SetupEndPrice, index),
                        new LevelItem(State.TriggerLevel, State.TriggerBarIndex)));
            }

            bool isStopHit = isImpulseUp && low <= State.SetupStartPrice
                             || !isImpulseUp && high >= State.SetupStartPrice;
            if (isStopHit)
            {
                State.IsInSetup = false;
                OnStopLoss?.Invoke(this,
                    new LevelEventArgs(new LevelItem(State.SetupStartPrice, index),
                        new LevelItem(State.TriggerLevel, State.TriggerBarIndex)));
            }

            return State.IsInSetup;
        }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public void CheckBar(int index)
        {
            m_LastBarIndex = index;
            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                finder.Calculate(index);
            }

            CheckSetup(null);
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="price">The price.</param>
        public void CheckTick(double price)
        {
            if (m_PreFinder == null)
            {
                return;
            }

            IsSetup(m_LastBarIndex, m_PreFinder, price);
        }

        private void CheckSetup(double? price)
        {
            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                if (IsSetup(m_LastBarIndex, finder))
                {
                    break;
                }
            }
        }
    }
}
