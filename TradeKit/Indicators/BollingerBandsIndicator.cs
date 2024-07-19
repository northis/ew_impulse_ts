using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;
using TradeKit.Core.Common;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Bollinger Bands are used to confirm signals. The bands indicate overbought and oversold levels relative to a moving average.
    /// <remarks>Bollinger bands widen in volatile market periods, and contract during less volatile periods. Tightening of the bands is often used a signal that there will shortly be a sharp increase in market volatility.
    /// </remarks>
    /// </summary>
    public class BollingerBandsIndicator : Indicator
    {
        private BollingerBands m_BollingerBands;
        
        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Periods), DefaultValue = Helper.BOLLINGER_PERIODS)]
        public int Periods { get; set; }

        /// <summary>
        /// The short period used calculation.
        /// </summary>
        [Parameter(nameof(StandardDeviations), DefaultValue = Helper.BOLLINGER_STANDARD_DEVIATIONS)]
        public double StandardDeviations { get; set; }

        /// <summary>
        /// Main Bollinger line.
        /// </summary>
        [Output(nameof(Main), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Main { get; set; }

        /// <summary>
        /// Upper Bollinger Band.
        /// </summary>
        [Output(nameof(Top), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Top { get; set; }

        /// <summary>
        /// Lower Bollinger Band.
        /// </summary>
        [Output(nameof(Bottom), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Bottom { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_BollingerBands = Indicators.BollingerBands(
                Bars.ClosePrices, Periods, StandardDeviations, MovingAverageType.Simple);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            Main[index] = m_BollingerBands.Main[index];
            Top[index] = m_BollingerBands.Top[index];
            Bottom[index] = m_BollingerBands.Bottom[index];
        }
    }
}
