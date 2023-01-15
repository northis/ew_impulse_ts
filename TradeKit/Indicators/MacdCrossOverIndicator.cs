using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the MACD (Moving Average Convergence/Divergence) indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class MacdCrossOverIndicator : Indicator
    {
        private MacdCrossOver m_MacdCrossOver;

        /// <summary>
        /// The long period used calculation.
        /// </summary>
        [Parameter(nameof(LongCycle), DefaultValue = Helper.MACD_LONG_CYCLE)]
        public int LongCycle { get; set; }

        /// <summary>
        /// The short period used calculation.
        /// </summary>
        [Parameter(nameof(ShortCycle), DefaultValue = Helper.MACD_SHORT_CYCLE)]
        public int ShortCycle { get; set; }

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(SignalPeriods), DefaultValue = Helper.MACD_SIGNAL_PERIODS)]
        public int SignalPeriods { get; set; }

        /// <summary>
        /// Gets or sets the MACD histogram.
        /// </summary>
        [Output(nameof(Histogram), PlotType = PlotType.Histogram)]
        public IndicatorDataSeries Histogram { get; set; }

        /// <summary>
        /// Gets or sets the MACD series.
        /// </summary>
        [Output(nameof(MACD), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries MACD { get; set; }

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Output(nameof(Signal), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Signal { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_MacdCrossOver = Indicators.MacdCrossOver(LongCycle, ShortCycle, SignalPeriods);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            Histogram[index] = m_MacdCrossOver.Histogram[index];
            MACD[index] = m_MacdCrossOver.MACD[index];
            Signal[index] = m_MacdCrossOver.Signal[index];
        }
    }
}
