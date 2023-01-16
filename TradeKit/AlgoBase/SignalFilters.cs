using System;
using System.Diagnostics;
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
        private const int STOCHASTIC_UP = 70;
        private const int STOCHASTIC_UP_MIDDLE = 65;
        private const int STOCHASTIC_DOWN = 30;
        private const int STOCHASTIC_DOWN_MIDDLE = 35;

        /// <summary>
        /// Gets the trend based on the "Super trend" indicator.
        /// </summary>
        /// <param name="sti">The super trend input.</param>
        /// <param name="dateTimeBar">The date and time of the current bar .</param>
        public static TrendType GetTrend(SuperTrendItem sti, DateTime dateTimeBar)
        {
            int mainIndex = sti.BarsProviderMain.GetIndexByTime(dateTimeBar);
            if (mainIndex <= 0)
                return TrendType.NoTrend;

            double resMain = sti.SuperTrendMain.Histogram[mainIndex];
            if (resMain == 0)
                return TrendType.NoTrend;
            
            TrendType trend = resMain > 0 ? TrendType.Bullish : TrendType.Bearish;
            if (sti.SuperTrendMajor != null)
            {
                int majorIndex = GetActualIndex(sti.BarsProviderMajor, dateTimeBar);
                double resMajor = sti.SuperTrendMajor.Histogram[majorIndex];
                if (resMajor == 0)
                    return TrendType.NoTrend;

                TrendType trendMajor = resMajor > 0 ? TrendType.Bullish : TrendType.Bearish;
                if (trendMajor != trend)
                    return TrendType.NoTrend;
            }
            
            return trend;
        }

        /// <summary>
        /// Gets the actual index for other time frame.
        /// </summary>
        /// <param name="barProvider">The bar provider we want to find an index against.</param>
        /// <param name="dateTimeBar">The date time bar.</param>
        private static int GetActualIndex(IBarsProvider barProvider, DateTime dateTimeBar)
        {
            int majorIndex = barProvider.GetIndexByTime(dateTimeBar);
            DateTime majorDateTime = barProvider.GetOpenTime(majorIndex);
            if (majorDateTime <
                majorDateTime + TimeFrameHelper.TimeFrames[barProvider.TimeFrame].TimeSpan / 2)
                majorIndex--;

            return majorIndex;
        }

        /// <summary>
        /// Gets the trend based on the "Three Elder's Screens" strategy.
        /// </summary>
        /// <param name="esi">The Elder's Screens input.</param>
        /// <param name="dateTimeBar">The date and time of the current bar (3rd screen).</param>
        public static TrendType GetElderTrend(ElderScreensItem esi, DateTime dateTimeBar)
        {
            int majorIndex = GetActualIndex(esi.BarsProviderMajor, dateTimeBar);
            int minorIndex = GetActualIndex(esi.BarsProviderMinor, dateTimeBar);

            if (majorIndex <= 0 || minorIndex <= 0)
                return TrendType.NoTrend;

            double min = esi.BarsProviderMajor.GetLowPrice(majorIndex);
            double average = esi.MovingAverageMajor.Result[majorIndex];
            double histValue = esi.MacdCrossOverMajor.Histogram[majorIndex];
            double histValuePrev = esi.MacdCrossOverMajor.Histogram[majorIndex - 1];
            double stochasticValue = esi.StochasticMinor.PercentD[minorIndex];
            double stochasticValuePrev = esi.StochasticMinor.PercentD[minorIndex - 1];

            if (min > average)
            {
                if (histValue <= 0 || histValue < histValuePrev)
                    return TrendType.NoTrend;

                if (stochasticValue >= STOCHASTIC_DOWN && stochasticValuePrev < STOCHASTIC_DOWN)
                    return TrendType.Bullish;

                return TrendType.NoTrend;

            }

            double max = esi.BarsProviderMajor.GetHighPrice(majorIndex);
            if (max < average)
            {
                if (histValue >= 0 || histValue > histValuePrev)
                    return TrendType.NoTrend;

                if(stochasticValue <= STOCHASTIC_UP && stochasticValuePrev > STOCHASTIC_UP)
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
