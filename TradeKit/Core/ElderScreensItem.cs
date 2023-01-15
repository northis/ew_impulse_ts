using TradeKit.Indicators;

namespace TradeKit.Core
{
    /// <summary>
    ///  Class contains indicators & providers for the trend based on the "Three Elder's Screens" strategy.
    /// </summary>
    public class ElderScreensItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ElderScreensItem"/> class.
        /// </summary>
        /// <param name="barsProviderMajor">The bars provider (1st screen).</param>
        /// <param name="macdCrossOverMajor">The "MACD cross over" indicator (1st screen).</param>
        /// <param name="movingAverageMajor">The moving average indicator (1nd screen).</param>
        /// <param name="barsProviderMinor">The bars provider (2nd screen).</param>
        /// <param name="stochasticMinor">The stochastic indicator (2nd screen).</param>
        public ElderScreensItem(IBarsProvider barsProviderMajor, 
            MacdCrossOverIndicator macdCrossOverMajor,
            MovingAverageIndicator movingAverageMajor, 
            IBarsProvider barsProviderMinor,
            StochasticOscillatorIndicator stochasticMinor)
        {
            BarsProviderMajor = barsProviderMajor;
            MacdCrossOverMajor = macdCrossOverMajor;
            MovingAverageMajor = movingAverageMajor;
            BarsProviderMinor = barsProviderMinor;
            StochasticMinor = stochasticMinor;
        }

        /// <summary>
        /// The bars provider (1st screen)
        /// </summary>
        public IBarsProvider BarsProviderMajor { get; }

        /// <summary>
        /// The "MACD cross over" indicator (1st screen)
        /// </summary>
        public MacdCrossOverIndicator MacdCrossOverMajor { get; }
        
        /// <summary>
        /// The moving average indicator (1nd screen).
        /// </summary>
        public MovingAverageIndicator MovingAverageMajor { get; }

        /// <summary>
        /// The bars provider (2nd screen).
        /// </summary>
        public IBarsProvider BarsProviderMinor { get; }

        /// <summary>
        /// The stochastic indicator (2nd screen).
        /// </summary>
        public StochasticOscillatorIndicator StochasticMinor { get; }
    }
}
