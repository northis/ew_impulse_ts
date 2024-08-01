using System;
using cAlgo.API;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    /// <summary>
    /// Calculates trend value (-1,0,1) based on Bill Williams' Alligator.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class ZoneAlligatorIndicator : Indicator
    {
        private ZoneAlligatorFinder m_Alligator;

        /// <summary>
        /// Gets or sets the histogram.
        /// </summary>
        [Output(nameof(Histogram), PlotType = PlotType.Line)]
        public IndicatorDataSeries Histogram { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_Alligator = new ZoneAlligatorFinder(
                new CTraderBarsProvider(Bars, Symbol.ToISymbol()));
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            DateTime openDate = Bars.OpenTimes[index];
            Histogram[index] = m_Alligator.GetResultValue(openDate);
        }
    }
}
