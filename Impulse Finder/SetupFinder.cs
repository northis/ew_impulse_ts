using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.EventArgs;

namespace cAlgo
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// </summary>
    public class SetupFinder
    {
        private readonly IBarsProvider m_BarsProvider;
        private readonly PatternFinder m_PatternFinder;
        private readonly List<ExtremumFinder> m_ExtremumFinders = new List<ExtremumFinder>();
        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;
        private double m_SetupStartPrice;
        private double m_SetupEndPrice;
        private double m_TriggerLevel;
        private int m_TriggerBarIndex;

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
        /// Initializes a new instance of the <see cref="SetupFinder"/> class.
        /// </summary>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public SetupFinder(double correctionAllowancePercent, IBarsProvider barsProvider)
        {
            m_BarsProvider = barsProvider;
            for (double d = Helper.DEVIATION_MAX;
                 d >= Helper.DEVIATION_MIN;
                 d -= Helper.DEVIATION_STEP)
            {
                m_ExtremumFinders.Add(new ExtremumFinder(d, m_BarsProvider));
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
                double curValue = finder
                    .Extrema
                    .ElementAt(curIndex).Value.Value;
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
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(int index, ExtremumFinder finder)
        {
            SortedDictionary<int, Extremum> extrema = finder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return false;
            }

            double low = m_BarsProvider.GetLowPrice(index);
            double high = m_BarsProvider.GetHighPrice(index);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            KeyValuePair<int, Extremum> startItem = extrema
                .ElementAt(startIndex);
            KeyValuePair<int, Extremum> endItem = extrema
                .ElementAt(endIndex);

            bool isInSetupBefore = m_IsInSetup;
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
                    if (maxValue <= m_BarsProvider.GetHighPrice(i) ||
                        minValue >= m_BarsProvider.GetLowPrice(i))
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

                double triggerSize = Math.Abs(endValue - startValue) * TRIGGER_LEVEL_RATIO;

                double triggerLevel;
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

                if (!gotSetup)
                {
                    return;
                }

                bool isImpulse = m_PatternFinder.IsImpulse(
                    startItem.Value, endItem.Value, finder.DeviationPercent, out List<Extremum> outExtrema);
                if (!isImpulse)
                {
                    // The move is not an impulse.
                    return;
                }

                if (m_SetupStartIndex == startItem.Key ||
                    m_SetupEndIndex == endItem.Key)
                {
                    // Cannot use the same impulse twice.
                    return;
                }

                if (endItem.Key == index)
                {
                    // Wait for the next bar
                    return;
                }

                m_TriggerLevel = triggerLevel;
                m_TriggerBarIndex = index;
                m_IsInSetup = true;

                double endAllowance = Math.Abs(triggerLevel - endValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
                double startAllowance = Math.Abs(triggerLevel - startValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

                m_SetupStartIndex = startItem.Key;
                m_SetupEndIndex = endItem.Key;

                if (isImpulseUp)
                {
                    m_SetupStartPrice = startValue - startAllowance;
                    m_SetupEndPrice = endValue - endAllowance;
                }
                else
                {
                    m_SetupStartPrice = startValue + startAllowance;
                    m_SetupEndPrice = endValue + endAllowance;
                }

                var tpArg = new LevelItem(m_SetupEndPrice, m_SetupEndIndex);
                var slArg = new LevelItem(m_SetupStartPrice, m_SetupStartIndex);

                OnEnter?.Invoke(this,
                    new SignalEventArgs(
                        new LevelItem(triggerLevel, index),
                        tpArg,
                        slArg,
                        outExtrema));
                // Here we should give a trade signal.
            }

            if (!m_IsInSetup)
            {
                for (; ; )
                {
                    CheckImpulse();
                    if (m_IsInSetup)
                    {
                        break;
                    }
                    // We don't know how far we are from the nearest initial impulse
                    // so we go deep and check

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

            if (!m_IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            bool isImpulseUp = m_SetupEndPrice > m_SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= m_SetupEndPrice
                               || !isImpulseUp && low <= m_SetupEndPrice;

            if (isProfitHit)
            {
                m_IsInSetup = false;
                OnTakeProfit?.Invoke(this,
                    new LevelEventArgs(new LevelItem(m_SetupEndPrice, index),
                        new LevelItem(m_TriggerLevel, m_TriggerBarIndex)));
            }

            bool isStopHit = isImpulseUp && low <= m_SetupStartPrice
                             || !isImpulseUp && high >= m_SetupStartPrice;
            if (isStopHit)
            {
                m_IsInSetup = false;
                OnStopLoss?.Invoke(this,
                    new LevelEventArgs(new LevelItem(m_SetupStartPrice, index),
                        new LevelItem(m_TriggerLevel, m_TriggerBarIndex)));
            }

            return m_IsInSetup;
        }

        /// <summary>
        /// Checks the conditions of possible setup for <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public void CheckSetup(int index)
        {
            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                finder.Calculate(index);
            }

            foreach (ExtremumFinder finder in m_ExtremumFinders)
            {
                if (IsSetup(index, finder))
                {
                    break;
                }
            }
        }
    }
}
