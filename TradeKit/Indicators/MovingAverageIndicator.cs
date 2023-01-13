using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the Moving Average Indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class MovingAverageIndicator : Indicator
    {
        private MovingAverage m_MovingAverage;
        
        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = Helper.MOVING_AVERAGE_PERIOD)]
        public int Period { get; set; }

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Output(nameof(Result), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Result { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_MovingAverage = Indicators.MovingAverage(
                Bars.ClosePrices, Period, MovingAverageType.Simple);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            Result[index] = m_MovingAverage.Result[index];
        }
    }
}
