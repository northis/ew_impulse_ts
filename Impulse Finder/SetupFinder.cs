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
        private readonly IBarsProvider m_BarsProviderMinor;
        private readonly IBarsProvider m_BarsProvider;
        private readonly PatternFinder m_PatternFinder;
        private readonly ExtremumFinder m_ExtremumFinder;
        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;
        private double m_SetupStartPrice;
        private double m_SetupEndPrice;

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
        /// <param name="deviationPercentMajor">The deviation percent.</param>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProviderMinor">The bars provider (minor).</param>
        /// <param name="barsProviderMain">The bars provider main (main).</param>
        public SetupFinder(
            double deviationPercentMajor,
            double correctionAllowancePercent,
            IBarsProvider barsProviderMinor,
            IBarsProvider barsProviderMain)
        {
            m_BarsProviderMinor = barsProviderMinor;
            m_BarsProvider = barsProviderMain;
            m_ExtremumFinder = new ExtremumFinder(
                deviationPercentMajor, m_BarsProvider);
            m_PatternFinder = new PatternFinder(
                correctionAllowancePercent, deviationPercentMajor, barsProviderMinor);
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
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInitialMovement(
            double startValue, double endValue, int startIndex)
        {
            int count = m_ExtremumFinder.Extrema.Count;
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                double curValue = m_ExtremumFinder
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
        /// Checks the conditions of possible setup for <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar (candle) to calculate.</param>
        /// <param name="minorIndex">The index of bar (minot TF) to calculate.</param>
        public void CheckSetup(int index, int minorIndex)
        {
            m_ExtremumFinder.Calculate(index);
            SortedDictionary<int, Extremum> extrema = m_ExtremumFinder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return;
            }

            double low = m_BarsProviderMinor.GetLowPrice(minorIndex);
            double high = m_BarsProviderMinor.GetHighPrice(minorIndex);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            KeyValuePair<int, Extremum> startItem = extrema
                .ElementAt(startIndex);
            KeyValuePair<int, Extremum> endItem = extrema
                .ElementAt(endIndex);

            bool isInSetup = m_IsInSetup;

            void CheckImpulse()
            {
                double startValue = startItem.Value.Value;
                double endValue = endItem.Value.Value;

                bool isImpulseUp = endValue > startValue;
                double maxValue = Math.Max(startValue, endValue);
                double minValue = Math.Min(startValue, endValue);
                for (int i = endItem.Key + 1; i < index; i++)
                {
                    if (maxValue <= m_BarsProvider.GetHighPrice(i) ||
                        minValue >= m_BarsProvider.GetLowPrice(i))
                    {
                        return;
                        // The setup is no longer valid, TP or SL is already hit.
                    }
                }

                bool isInitialMove = IsInitialMovement(startValue, endValue, startIndex);
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
                    triggerLevel = startValue + triggerSize;
                    gotSetup = high >= triggerLevel && high < endValue;
                }

                if (!gotSetup)
                {
                    return;
                }

                bool isImpulse = m_PatternFinder.IsImpulse(
                    startItem.Value.OpenTime, endItem.Value.CloseTime);
                if (!isImpulse)
                {
                    // The move is not an impulse.
                    return;
                }

                m_SetupStartIndex = startItem.Key;
                m_SetupEndIndex = endItem.Key;
                m_SetupStartPrice = startItem.Value.Value;
                m_SetupEndPrice = endItem.Value.Value;
                m_IsInSetup = true;

                LevelItem tpArg = isImpulseUp
                    ? new LevelItem(endValue, m_SetupEndIndex)
                    : new LevelItem(startValue, m_SetupStartIndex);
                LevelItem slArg = isImpulseUp
                    ? new LevelItem(startValue, m_SetupStartIndex)
                    : new LevelItem(endValue, m_SetupEndIndex);

                OnEnter?.Invoke(this,
                    new SignalEventArgs(
                        new LevelItem(triggerLevel, minorIndex),
                        tpArg,
                        slArg));
                // Here we should give a trade signal.
            }

            if (!m_IsInSetup)
            {
                CheckImpulse();
            }

            // We want to check if we have an extremum between a possible impulse
            // and the current position
            if (!m_IsInSetup && count > MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                startIndex -= 1;
                endIndex -= 1;
                startItem = extrema.ElementAt(startIndex);
                endItem = extrema.ElementAt(endIndex);
                CheckImpulse();
            }

            if (!m_IsInSetup)
            {
                return;
            }

            if (isInSetup != m_IsInSetup)
            {
                return;
            }

            bool isImpulseUp = m_SetupEndPrice > m_SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= m_SetupEndPrice
                               || !isImpulseUp && low <= m_SetupEndPrice;
            
            if (isProfitHit)
            {
                m_IsInSetup = false;
                OnTakeProfit?.Invoke(this, 
                    new LevelEventArgs(new LevelItem(m_SetupEndPrice, minorIndex)));
            }

            bool isStopHit = isImpulseUp && low <= m_SetupStartPrice
                             || !isImpulseUp && high >= m_SetupStartPrice;
            //add allowance
            if (isStopHit)
            {
                m_IsInSetup = false;
                OnStopLoss?.Invoke(this,
                    new LevelEventArgs(new LevelItem(m_SetupStartPrice, minorIndex)));
            }
        }
    }
}
