using System;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    /// <summary>
    /// Calculates the Moving Average Indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
#if !GARTLEY_PROD
    //[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
#endif
    public class PivotPointsIndicator : Indicator
    {
        private const string LOW = "L";
        private const string HIGH = "H";
        private readonly Color m_BearColorFill = Color.FromHex("#F0F08080");
        private readonly Color m_BullColorFill = Color.FromHex("#F090EE90");
        private PivotPointsFinder m_PivotPointsFinder;

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = Helper.PIVOT_PERIOD)]
        public int Period { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_PivotPointsFinder = new PivotPointsFinder(Period, 
                new CTraderBarsProvider(Bars, Symbol));
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int indexLast)
        {
            int index = m_PivotPointsFinder.Calculate(indexLast);
            if (index <= 0)
                return;

            DateTime dt = Bars.OpenTimes[index];

            double max = m_PivotPointsFinder.HighValues[dt];
            double min = m_PivotPointsFinder.LowValues[dt];

            if (max is not double.NaN)
            {
                Chart.DrawText($"{HIGH}{index}", HIGH, index, max, m_BearColorFill).ChartTextAlign(true);
            }

            if (min is not double.NaN)
            {
                Chart.DrawText($"{LOW}{index}", LOW, index, min, m_BullColorFill).ChartTextAlign(false);
            }
        }
    }
}
