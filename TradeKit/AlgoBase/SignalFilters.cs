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
        private const int STOCHASTIC_UP = 75;
        private const int STOCHASTIC_UP_MIDDLE = 65;
        private const int STOCHASTIC_DOWN = 25;
        private const int STOCHASTIC_DOWN_MIDDLE = 35;

        /// <summary>
        /// Gets the trend based on the "Three Elder's Screens" strategy.
        /// </summary>
        /// <param name="barsProviderMajor">The bars provider (1st screen).</param>
        /// <param name="macdCrossOverMajor">The "MACD cross over" indicator (1st screen).</param>
        /// <param name="movingAverageMajor">The moving average indicator (1nd screen).</param>
        /// <param name="barsProviderMinor">The bars provider (2nd screen).</param>
        /// <param name="stochasticMinor">The stochastic indicator (2nd screen).</param>
        /// <param name="dateTimeBar">The date and time of the current bar (3rd screen).</param>
        public static TrendType GetElderTrend(
            IBarsProvider barsProviderMajor,
            MacdCrossOverIndicator macdCrossOverMajor, 
            MovingAverageIndicator movingAverageMajor,
            IBarsProvider barsProviderMinor,
            StochasticOscillatorIndicator stochasticMinor,
            DateTime dateTimeBar)
        {
            int majorIndex = barsProviderMajor.GetIndexByTime(dateTimeBar);
            if (majorIndex == 0)
                return TrendType.NoTrend;

            int minorIndex = barsProviderMinor.GetIndexByTime(dateTimeBar);
            if (minorIndex == 0)
                return TrendType.NoTrend;

            double min = barsProviderMajor.GetLowPrice(majorIndex);
            double average = movingAverageMajor.Result[majorIndex];
            double histValue = macdCrossOverMajor.Histogram[majorIndex];
            double histValuePrev = macdCrossOverMajor.Histogram[majorIndex - 1];
            double stochasticValue = stochasticMinor.PercentD[minorIndex];
            double stochasticValuePrev = stochasticMinor.PercentD[minorIndex - 1];

            if (min > average)
            {
                if (histValue <= 0 || histValue < histValuePrev)
                    return TrendType.NoTrend;

                if (stochasticValue is < STOCHASTIC_DOWN_MIDDLE and >= STOCHASTIC_DOWN &&
                    stochasticValuePrev < STOCHASTIC_DOWN)
                    return TrendType.Bullish;

                return TrendType.NoTrend;

            }

            double max = barsProviderMajor.GetHighPrice(majorIndex);
            if (max < average)
            {
                if (histValue >= 0 || histValue > histValuePrev)
                    return TrendType.NoTrend;

                if (stochasticValue is > STOCHASTIC_UP_MIDDLE and <= STOCHASTIC_UP &&
                    stochasticValuePrev > STOCHASTIC_UP)
                    return TrendType.Bearish;

                return TrendType.NoTrend;
            }

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
