using System;
using cAlgo.API;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the Moving Average Indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
#if !GARTLEY_PROD
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
#endif
    public class PivotPointsIndicator : Indicator
    {
        private const string LOW = "L";
        private const string HIGH = "H";
        private Color m_BearColorFill = Color.FromHex("#F0F08080");
        private Color m_BullColorFill = Color.FromHex("#F090EE90");
        private int m_PeriodX2;

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = Helper.PIVOT_PERIOD)]
        public int Period { get; set; }

        ///// <summary>
        ///// The period used for the calculation of the signal.
        ///// </summary>
        //[Output(nameof(Result), LineStyle = LineStyle.Lines)]
        //public IndicatorDataSeries Result { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_PeriodX2 = Period * 2;
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int indexLast)
        {
            if (indexLast < m_PeriodX2) // before+after
                return;

            int index = indexLast - Period;
            double max = Bars.HighPrices[index];
            double min = Bars.LowPrices[index];

            bool gotHigh = true;
            bool gotLow = true;

            for (int i = index - Period; i < index + Period; i++)
            {
                if (i == index)
                    continue;

                double lMax = Bars.HighPrices[i];
                double lMin = Bars.LowPrices[i];

                if (lMax > max && gotHigh)
                    gotHigh = false;

                if(lMin < min && gotLow)
                    gotLow = false;
            }

            if (gotHigh) Chart.DrawText($"{HIGH}{index}", HIGH, index, max, m_BearColorFill).ChartTextAlign(true);
            if (gotLow) Chart.DrawText($"{LOW}{index}", LOW, index, min, m_BullColorFill).ChartTextAlign(false);
        }
    }
}
