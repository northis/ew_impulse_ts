using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="cAlgo.API.Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
    public class ImpulseFinder : Indicator
    {
        /// <summary>
        /// Gets or sets the allowance to impulse recognition in percents (major).
        /// </summary>
        [Parameter("DeviationPercentMajor", DefaultValue = 0.1, MinValue = 0.01)]
        public double DeviationPercentMajor { get; set; }

        /// <summary>
        /// Gets or sets the allowance to impulse recognition in percents (minor).
        /// </summary>
        [Parameter("DeviationPercentMinor", DefaultValue = 0.05, MinValue = 0.01)]
        public double DeviationPercentMinor { get; set; }

        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;
        private ExtremumFinder m_ExtremumFinder;
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

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_ExtremumFinder = new ExtremumFinder(DeviationPercentMajor);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            m_ExtremumFinder.Calculate(index, Bars);
            CheckSetup(index);
        }

        private string StartSetupLineChartName
        {
            get { return "StartSetupLine" + Bars.OpenTimes.Last(1); }
        }

        private string EndSetupLineChartName
        {
            get { return "EndSetupLine" + Bars.OpenTimes.Last(1); }
        }

        private string EnterChartName
        {
            get { return "Enter" + Bars.OpenTimes.Last(1); }
        }

        private string StopChartName
        {
            get { return "SL" + Bars.OpenTimes.Last(1); }
        }

        private string ProfitChartName
        {
            get { return "TP" + Bars.OpenTimes.Last(1); }
        }

        private int GetExtremumCount(int startIndex, int endIndex)
        {
            var minorExtremumFinder = new ExtremumFinder(DeviationPercentMinor);
            minorExtremumFinder.Calculate(startIndex, endIndex, Bars);
            int impulseExtremaCount = minorExtremumFinder.Extrema.Count;
            return impulseExtremaCount;
        }

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
                double curValue = m_ExtremumFinder.Extrema.ElementAt(curIndex).Value;
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
        private void CheckSetup(int index)
        {
            SortedDictionary<int, double> extrema = m_ExtremumFinder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return;
            }

            KeyValuePair<int, double> startItem = extrema.ElementAt(count - IMPULSE_START_NUMBER);
            KeyValuePair<int, double> endItem = extrema.ElementAt(count - IMPULSE_END_NUMBER);
            KeyValuePair<int, double> lastItem = extrema.ElementAt(count - CORRECTION_EXTREMUM_NUMBER);

            double startValue = startItem.Value;
            double endValue = endItem.Value;

            bool isImpulseUp = endValue > startValue;
            double low = Bars.LowPrices[index];
            double high = Bars.HighPrices[index];

            if (!m_IsInSetup)
            {
                double lastValue = lastItem.Value;
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

                // TODO Find out why it says 3 when it's 2
                int impulseExtremaCount = GetExtremumCount(startItem.Key, endItem.Key);
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

                m_SetupStartIndex = startItem.Key;
                m_SetupEndIndex = endItem.Key;
                m_IsInSetup = true;

                Chart.DrawTrendLine(StartSetupLineChartName, m_SetupStartIndex, startValue, index, triggerLevel, Color.Gray);
                Chart.DrawTrendLine(EndSetupLineChartName, m_SetupEndIndex, endValue, index, triggerLevel, Color.Gray);
                Chart.DrawIcon(EnterChartName, ChartIconType.Star, index, triggerLevel, Color.White);
                Chart.DrawText(StartSetupLineChartName + "text", impulseExtremaCount.ToString(), index, triggerLevel, Color.White);
                return;
            }

            // Re-define the setup-related start and end values
            startValue = extrema[m_SetupStartIndex];
            endValue = extrema[m_SetupEndIndex];
            isImpulseUp = endValue > startValue;

            bool isProfitHit = isImpulseUp && high >= endValue || !isImpulseUp && low <= endValue;
            if (isProfitHit)
            {
                Chart.DrawIcon(ProfitChartName, ChartIconType.Star, index, endValue, Color.Green);
                m_IsInSetup = false;
            }

            bool isStopHit = isImpulseUp && low <= startValue || !isImpulseUp && high >= startValue;
            //add allowance
            if (isStopHit)
            {
                Chart.DrawIcon(StopChartName, ChartIconType.Star, index, startValue, Color.Red);
                m_IsInSetup = false;
            }
        }
    }
}
