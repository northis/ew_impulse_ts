using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.EventArgs;

namespace cAlgo
{
    public class SetupFinder
    {
        private readonly double m_CorrectionAllowancePercent;
        private readonly int m_AnalyzeDepth;
        private readonly IBarsProvider m_BarsProvider;
        private readonly ExtremumFinder m_ExtremumFinder;
        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;

        private const double TRIGGER_LEVEL_RATIO = 0.5;

        private const int CORRECTION_EXTREMUM_NUMBER = 1;
        private const int IMPULSE_END_NUMBER = 2;
        private const int IMPULSE_START_NUMBER = 3;
        // We want to collect at lease this amount of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out, weather this impulse is an initial one or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 4;

        public SetupFinder(
            double deviationPercentMajor,
            double deviationPercentMinor,
            double correctionAllowancePercent,
            int analyzeDepth,
            IBarsProvider barsProvider)
        {
            m_CorrectionAllowancePercent = correctionAllowancePercent;
            m_AnalyzeDepth = analyzeDepth;
            m_BarsProvider = barsProvider;
            m_ExtremumFinder = new ExtremumFinder(deviationPercentMajor, barsProvider);
        }

        public event EventHandler<LevelEventArgs> OnStopLoss;
        public event EventHandler<LevelEventArgs> OnTakeProfit;
        public event EventHandler<SignalEventArgs> OnEnter;

        /// <summary>
        /// Determines whether the movement from <see cref="startValue"/> to <see cref="endValue"/> is initial. We use current bar position and <see cref="IMPULSE_START_NUMBER"/> to rewind the bars to the past.
        /// </summary>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInitialMovement(double startValue, double endValue)
        {
            int count = m_ExtremumFinder.Extrema.Count;
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            for (int curIndex = count - IMPULSE_START_NUMBER - 1; curIndex >= 0; curIndex--)
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

        private List<ExtremumFinder> GetAllFindersOrdered()
        {
            //TimeFrame minorTimeFrame =
            //    TimeFrameHelper.GetMinorTimeFrame(m_BarsProvider.TimeFrame);
            //Extremum[] minorExtrema = null;
            //if (minorTimeFrame != TimeFrame)
            //{
            //    Bars bars = MarketData.GetBars(minorTimeFrame);
            //    var minorExtremumFinder = new ExtremumFinder(DeviationPercentMinor);

            //    minorExtremumFinder.Calculate(
            //        startItem.Value.OpenTime, endItem.Value.OpenTime, bars);
            //    minorExtrema = minorExtremumFinder.ToExtremaArray();
            //}

            //var mainExtremumFinder = new ExtremumFinder(DeviationPercentMinor);
            //mainExtremumFinder.Calculate(startItem.Key, endItem.Key, Bars);
            return new List<ExtremumFinder>();
        }

        /// <summary>
        /// Checks the conditions of possible setup for <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar (candle) to calculate.</param>
        public void CheckSetup(int index)
        {
            SortedDictionary<int, Extremum> extrema = m_ExtremumFinder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return;
            }

            KeyValuePair<int, Extremum> startItem = extrema.ElementAt(count - IMPULSE_START_NUMBER);
            KeyValuePair<int, Extremum> endItem = extrema.ElementAt(count - IMPULSE_END_NUMBER);
            KeyValuePair<int, Extremum> lastItem = extrema.ElementAt(count - CORRECTION_EXTREMUM_NUMBER);

            double startValue = startItem.Value.Value;
            double endValue = endItem.Value.Value;

            bool isImpulseUp = endValue > startValue;
            double low = m_BarsProvider.GetLowPrice(index);
            double high = m_BarsProvider.GetHighPrice(index);

            if (!m_IsInSetup)
            {
                double lastValue = lastItem.Value.Value;
                if (lastValue >= Math.Max(startValue, endValue) || lastValue <= Math.Min(startValue, endValue))
                {
                    return;
                    // The setup is no longer valid, TP or SL is already hit.
                }

                bool isInitialMove = IsInitialMovement(startValue, endValue);
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

                List<Extremum[]> extremaList = GetAllFindersOrdered()
                    .Select(a => a.ToExtremaArray())
                    .ToList();

                bool isImpulse = PatternFinder.IsImpulse(
                    extremaList, m_CorrectionAllowancePercent);
                if (!isImpulse)
                {
                    // The move is not an impulse.
                    return;
                }

                m_SetupStartIndex = startItem.Key;
                m_SetupEndIndex = endItem.Key;
                m_IsInSetup = true;

                OnEnter?.Invoke(this, 
                    new SignalEventArgs(
                        new LevelItem(triggerLevel, index),
                        new LevelItem(startValue, m_SetupStartIndex),
                        new LevelItem(endValue, m_SetupEndIndex)));
                // Here we should give a trade signal.
                return;
            }

            // Re-define the setup-related start and end values
            startValue = extrema[m_SetupStartIndex].Value;
            endValue = extrema[m_SetupEndIndex].Value;
            isImpulseUp = endValue > startValue;

            bool isProfitHit = isImpulseUp && high >= endValue || !isImpulseUp && low <= endValue;
            if (isProfitHit)
            {
                OnTakeProfit?.Invoke(this, 
                    new LevelEventArgs(new LevelItem(endValue, index)));
                m_IsInSetup = false;
            }

            bool isStopHit = isImpulseUp && low <= startValue || !isImpulseUp && high >= startValue;
            //add allowance
            if (isStopHit)
            {
                OnStopLoss?.Invoke(this,
                    new LevelEventArgs(new LevelItem(startValue, index)));
                m_IsInSetup = false;
            }
        }
    }
}
