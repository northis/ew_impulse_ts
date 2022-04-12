using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
    public class ImpulseFinder : Indicator
    {
        [Parameter("DeviationPercent", DefaultValue = 0.05, MinValue = 0.01)]
        public double DeviationPercent { get; set; }
        
        private readonly SortedDictionary<int, double> m_Extrema = new SortedDictionary<int, double>();
        private bool m_IsUpDirection;
        private bool m_IsInSetup;
        private int m_SetupStartIndex;
        private int m_SetupEndIndex;
        private double m_ExtremumPrice;
        private int m_ExtremumIndex;

        private const double TRIGGER_LEVEL_RATIO = 0.5;

        private void MoveExtremum(int index, double price)
        {
            m_Extrema.Remove(m_ExtremumIndex);
            SetExtremum(index, price);
        }

        private void SetExtremum(int index, double price)
        {
            m_ExtremumIndex = index;
            m_ExtremumPrice = price;
            m_Extrema[m_ExtremumIndex] = m_ExtremumPrice;
        }

        private double DeviationPrice
        {
            get
            {
                double percentRate = m_IsUpDirection ? -0.01 : 0.01;
                return m_ExtremumPrice * (1.0 + DeviationPercent * percentRate);
            }
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

        private void CheckSetup(int index)
        {
            int count = m_Extrema.Count;
            if (count < 3)
            {
                return;
            }

            KeyValuePair<int, double> startItem = m_Extrema.ElementAt(count - 3);
            KeyValuePair<int, double> endItem = m_Extrema.ElementAt(count - 2);

            double startValue = startItem.Value;
            double endValue = endItem.Value;

            bool isLocalCorrectionUp = endValue > startValue;

            double low = Bars.LowPrices[index];
            double high = Bars.HighPrices[index];

            if (!m_IsInSetup)
            {
                double triggerSize = Math.Abs(endValue - startValue) * TRIGGER_LEVEL_RATIO;
                double triggerLevel;
                bool gotSetup = isLocalCorrectionUp 
                    ? high >= (triggerLevel = startValue + triggerSize) 
                    : low <= (triggerLevel = endValue - triggerSize);

                if (gotSetup && m_SetupEndIndex != endItem.Key)
                {
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
            }

            if (!m_IsInSetup)
            {
                return;
            }

            // Re-define the setup-related start and end values
            startValue = m_Extrema[m_SetupStartIndex];
            endValue = m_Extrema[m_SetupEndIndex];
            isLocalCorrectionUp = endValue > startValue;

            bool isProfitHit = isLocalCorrectionUp && high >= endValue || !isLocalCorrectionUp && low <= endValue;

            if (isProfitHit)
            {
                Chart.DrawIcon(
                    ProfitChartName, ChartIconType.Star, index, endValue, Color.Green);
                m_IsInSetup = false;
            }

            bool isStopHit = isLocalCorrectionUp && low <= startValue || !isLocalCorrectionUp && high >= startValue;
            //add allowance
            if (isStopHit)
            {
                Chart.DrawIcon(
                    StopChartName, ChartIconType.Star, index, startValue, Color.Red);
                m_IsInSetup = false;
            }
        }

        public override void Calculate(int index)
        {
            double low = Bars.LowPrices[index];
            double high = Bars.HighPrices[index];
            if (m_ExtremumPrice == 0.0)
            {
                m_ExtremumPrice = high;
            }

            if (Bars.ClosePrices.Count < 2)
            {
                return;
            }

            if (m_IsUpDirection ? high >= m_ExtremumPrice : low <= m_ExtremumPrice)
            {
                MoveExtremum(index, m_IsUpDirection ? high : low);
            }
            else if (m_IsUpDirection ? low <= DeviationPrice : high >= DeviationPrice)
            {
                SetExtremum(index, m_IsUpDirection ? low : high);
                m_IsUpDirection = !m_IsUpDirection;
            }

            CheckSetup(index);
        }
    }
}
