using cAlgo.API;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    /// <summary>
    /// Calculates "Super Trend" - one of the most popular trend trading indicators.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
    public class SuperTrendIndicator : Indicator
    {
        private SupertrendFinder m_SupertrendFinder;

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Periods), DefaultValue = 10)]
        public int Periods { get; set; }

        /// <summary>
        /// The long period used calculation.
        /// </summary>
        [Parameter(nameof(Multiplier), DefaultValue = 3)]
        public double Multiplier { get; set; }
        
        /// <summary>
        /// Gets or sets the histogram (diff flat sum).
        /// </summary>
        [Output(nameof(HistogramFlatFlatCounter), PlotType = PlotType.Histogram, LineColor = "Purple")]
        public IndicatorDataSeries HistogramFlatFlatCounter { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_SupertrendFinder = new SupertrendFinder(new CTraderBarsProvider(Bars, Symbol), Multiplier, Periods);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            int trend = m_SupertrendFinder.GetResultValue(index);
            HistogramFlatFlatCounter[index] = trend * m_SupertrendFinder.FlatCounter.GetResultValue(index);
        }
    }
}