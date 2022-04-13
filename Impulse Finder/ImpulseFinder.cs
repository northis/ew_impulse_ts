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
        /// Gets or sets the allowance to impulse recognition in percents.
        /// </summary>
        [Parameter("DeviationPercent", DefaultValue = 0.05, MinValue = 0.01)]
        public double DeviationPercent { get; set; }
        
        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;
        private ExtremumFinder m_ExtremumFinder;
        private const double TRIGGER_LEVEL_RATIO = 0.5;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_ExtremumFinder = new ExtremumFinder(DeviationPercent);
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

        /// <summary>
        /// Checks the conditions of possible setup for <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar (candle) to calculate.</param>
        private void CheckSetup(int index)
        {
            SortedDictionary<int, double> extrema = m_ExtremumFinder.Extrema;
            int count = extrema.Count;
            if (count < 3)
            {
                return;
            }

            KeyValuePair<int, double> startItem = extrema.ElementAt(count - 3);
            KeyValuePair<int, double> endItem = extrema.ElementAt(count - 2);
            KeyValuePair<int, double> lastItem = extrema.ElementAt(count - 1);

            double startValue = startItem.Value;
            double endValue = endItem.Value;
            
            bool isImpulseUp = endValue > startValue;
            double low = Bars.LowPrices[index];
            double high = Bars.HighPrices[index];

            if (!m_IsInSetup)
            {
                double lastValue = lastItem.Value;
                if (lastValue >= Math.Max(startValue, endValue)
                    || lastValue <= Math.Min(startValue, endValue))
                {
                    return; // The setup is no longer valid, TP or SL is already hit.
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

                m_SetupStartIndex = startItem.Key;
                m_SetupEndIndex = endItem.Key;
                m_IsInSetup = true;

                Chart.DrawTrendLine(StartSetupLineChartName, m_SetupStartIndex, startValue, index, triggerLevel,
                    Color.Gray);
                Chart.DrawTrendLine(EndSetupLineChartName, m_SetupEndIndex, endValue, index, triggerLevel,
                    Color.Gray);
                Chart.DrawIcon(EnterChartName, ChartIconType.Star, index, triggerLevel, Color.White);
                return;
            }
            
            // Re-define the setup-related start and end values
            startValue = extrema[m_SetupStartIndex];
            endValue = extrema[m_SetupEndIndex];
            isImpulseUp = endValue > startValue;

            bool isProfitHit = isImpulseUp && high >= endValue || 
                               !isImpulseUp && low <= endValue;
            if (isProfitHit)
            {
                Chart.DrawIcon(
                    ProfitChartName, ChartIconType.Star, index, endValue, Color.Green);
                m_IsInSetup = false;
            }

            bool isStopHit = isImpulseUp && low <= startValue || 
                             !isImpulseUp && high >= startValue; //add allowance
            if (isStopHit)
            {
                Chart.DrawIcon(
                    StopChartName, ChartIconType.Star, index, startValue, Color.Red);
                m_IsInSetup = false;
            }
        }
    }
}
