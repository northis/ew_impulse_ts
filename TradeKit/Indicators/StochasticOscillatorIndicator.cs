using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the Stochastic Oscillator.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class StochasticOscillatorIndicator : Indicator
    {
        private StochasticOscillator m_StochasticOscillator;

        /// <summary>
        /// The value of the k periods used for calculation.
        /// </summary>
        [Parameter(nameof(PeriodsK), DefaultValue = Helper.STOCHASTIC_K_PERIODS)]
        public int PeriodsK { get; set; }

        /// <summary>
        /// The value of the d periods used for calculation.
        /// </summary>
        [Parameter(nameof(PeriodsD), DefaultValue = Helper.STOCHASTIC_D_PERIODS)]
        public int PeriodsD { get; set; }

        /// <summary>
        /// The value of the k slowing used for calculation.
        /// </summary>
        [Parameter(nameof(KSlowing), DefaultValue = Helper.STOCHASTIC_K_SLOWING)]
        public int KSlowing { get; set; }

        /// <summary>
        /// %D is 3 Period Exponential Moving Average of %K.
        /// </summary>
        [Output(nameof(PercentD), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries PercentD { get; set; }

        /// <summary>
        /// Calculation of %K is 100 multiplied by the ratio of the closing price minus the lowest price over the last N periods over the highest price over the last N minus the lowest price over the last N periods.
        /// </summary>
        [Output(nameof(PercentK), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries PercentK { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_StochasticOscillator = Indicators.StochasticOscillator(
                PeriodsK, KSlowing, PeriodsD, MovingAverageType.Simple);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            PercentD[index] = m_StochasticOscillator.PercentD[index];
            PercentK[index] = m_StochasticOscillator.PercentK[index];
        }
    }
}
