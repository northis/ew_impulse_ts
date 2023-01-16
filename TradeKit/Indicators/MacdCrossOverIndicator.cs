using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the MACD (Moving Average Convergence/Divergence) indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
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
        [Output(nameof(Histogram), LineStyle = LineStyle.Lines)]
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
        /// Gets or sets the MACD div histogram.
        /// </summary>
        [Output(nameof(HistogramDiv), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries HistogramDiv { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_MacdCrossOver = Indicators.MacdCrossOver(LongCycle, ShortCycle, SignalPeriods);
        }

        private double? m_PrevPrice = null;
        private double? m_PrevHist = null;
        private bool? m_IsUpDivergence = null;

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            if (index <= 0) return;

            double currentHist = m_MacdCrossOver.Histogram[index];
            double currentPrice = Bars[index].Close;
            Histogram[index] = currentHist;
            MACD[index] = m_MacdCrossOver.MACD[index];
            Signal[index] = m_MacdCrossOver.Signal[index];

            if (m_PrevPrice.HasValue && m_PrevHist.HasValue)
            {
                if (m_PrevHist <= 0 && currentHist >= 0 || m_PrevHist >= 0 && currentHist <= 0 ||
                    m_PrevHist < currentHist && m_PrevPrice < currentPrice ||
                    m_PrevHist > currentHist && m_PrevPrice > currentPrice)
                {
                    HistogramDiv[index] = double.NaN;
                    m_PrevHist = null;
                    m_IsUpDivergence = null;
                    return;
                }
                
                if (currentHist > 0 && currentPrice > m_PrevPrice)
                {
                    m_PrevPrice = Bars[index].High;
                    m_PrevHist = currentHist;
                    HistogramDiv[index] = double.NaN;
                    m_IsUpDivergence = null;
                    return;
                }

                if (currentHist < 0 && currentPrice < m_PrevPrice)
                {
                    m_PrevPrice = Bars[index].Low;
                    m_PrevHist = currentHist;
                    HistogramDiv[index] = double.NaN;
                    m_IsUpDivergence = null;
                    m_PrevHist = null;
                    return;
                }

                if (m_IsUpDivergence == true && currentHist< m_PrevHist||
                    m_IsUpDivergence == false && currentHist > m_PrevHist)
                {
                    HistogramDiv[index] = double.NaN;
                    m_IsUpDivergence = null;
                    m_PrevHist = null;
                    return;
                }

                if (currentHist > 0 && currentHist <= m_PrevHist && Bars[index].High > m_PrevPrice)
                {
                    m_PrevPrice = Bars[index].High;
                    HistogramDiv[index] = -1;
                    m_IsUpDivergence = false;
                    return;
                }

                if (currentHist < 0 && currentHist >= m_PrevHist && Bars[index].Low < m_PrevPrice)
                {
                    m_PrevPrice = Bars[index].Low;
                    HistogramDiv[index] = 1;
                    m_IsUpDivergence = true;
                    return;
                }
                
                HistogramDiv[index] = 0;
            }
            else
            {
                m_PrevPrice = Bars[index].Close;
                m_PrevHist = m_MacdCrossOver.Histogram[index];
                HistogramDiv[index] = double.NaN;
                m_IsUpDivergence = null;
            }
        }
    }
}
