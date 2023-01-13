using System;
using TradeKit.Core;
using TradeKit.Indicators;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// The filter logic for the signals
    /// </summary>
    internal static class SignalFilters
    {
        private const int DIVERGENCE_OFFSET_SEARCH = 2;

        /// <summary>
        /// Gets the trend based on the "Three Elder's Screens" strategy.
        /// </summary>
        /// <param name="barsProviderMajor">The bars provider (1st screen).</param>
        /// <param name="macdCrossOverMajor">The "MACD cross over" indicator (1st screen).</param>
        /// <param name="movingAverageMajor">The moving average indicator (1nd screen).</param>
        /// <param name="stochasticMinor">The stochastic indicator (2nd screen).</param>
        /// <param name="dateTimeBar">The date and time of the current bar (3rd screen).</param>
        public static TrendType GetElderTrend(
            IBarsProvider barsProviderMajor,
            MacdCrossOverIndicator macdCrossOverMajor, 
            MovingAverageIndicator movingAverageMajor,
            StochasticOscillatorIndicator stochasticMinor,
            DateTime dateTimeBar)
        {
            return TrendType.NoTrend;
        }

        /// <summary>
        /// Finds the divergence for the possible signal.
        /// </summary>
        /// <param name="macdCrossOver">The MACD cross over indicator instance.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="start">The start bar to search.</param>
        /// <param name="end">The end bar to search.</param>
        /// <param name="isBullSignal">if set to <c>true</c> the signal is bullish, otherwise bearish.</param>
        /// <returns>Start of the divergence bar or null if no divergence has been found.</returns>
        public static BarPoint FindDivergence(MacdCrossOverIndicator macdCrossOver,
            IBarsProvider barsProvider, BarPoint start, BarPoint end, bool isBullSignal)
        {
            double? foundDivValue = null;
            int indexX = start.BarIndex;
            int indexD = end.BarIndex;

            double macdD = macdCrossOver.Histogram[indexD];
            for (int i = indexD - DIVERGENCE_OFFSET_SEARCH; i >= indexX; i--)
            {
                double currentVal = macdCrossOver.Histogram[i];
                if (macdD <= 0 && currentVal > 0 ||
                    macdD >= 0 && currentVal < 0)
                    break;

                if (isBullSignal && barsProvider.GetLowPrice(i) < end.Value ||
                    !isBullSignal && barsProvider.GetHighPrice(i) > end.Value)
                    break;

                double histValue = macdCrossOver.Histogram[i];
                if (isBullSignal && histValue <= macdD ||
                    !isBullSignal && histValue >= macdD)
                {
                    // Find the inflection point of the histogram values
                    if (foundDivValue is null ||
                        isBullSignal && currentVal <= foundDivValue ||
                        !isBullSignal && currentVal >= foundDivValue)
                    {
                        foundDivValue = currentVal;
                    }
                    else
                    {
                        var divItem = new BarPoint(isBullSignal
                            ? barsProvider.GetLowPrice(i)
                            : barsProvider.GetHighPrice(i), i, barsProvider);
                        return divItem;
                    }
                }
            }

            return null;
        }

    }
}
