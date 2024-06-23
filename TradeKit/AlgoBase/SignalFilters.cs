using System;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
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
        /// Gets the spike based on the "Bollinger Bands" indicator.
        /// </summary>
        /// <param name="sti">The super trend input.</param>
        /// <param name="barPoint">The bar to check.</param>
        public static SpikeType GetSpike(TrendItem sti, BarPoint barPoint)
        {
            double value = barPoint.Value;
            
            double bandTop = sti.BollingerBands.Top[barPoint.BarIndex];
            //double bandMain = sti.BollingerBands.Main[barPoint.BarIndex];
            double bandBottom = sti.BollingerBands.Bottom[barPoint.BarIndex];

            Bars bars = sti.BollingerBands.Bars;
            if (value >= bandTop)
                return SpikeType.High;

            if(value <= bandBottom)
                return SpikeType.Low;

            return SpikeType.NoSpike;
        }

        /// <summary>
        /// Gets the trend based on the "Super trend" indicator.
        /// </summary>
        /// <param name="sti">The super trend input.</param>
        /// <param name="dateTimeBar">The date and time of the current bar .</param>
        public static TrendType GetTrend(TrendItem sti, DateTime dateTimeBar)
        {
            if (sti.Indicators.Length == 0)
                return TrendType.NoTrend;

            TimeSpan mainPeriod = TimeFrameHelper.GetTimeFrameInfo(sti.MainTrendIndicator.TimeFrame).TimeSpan;
            DateTime endDt = dateTimeBar + mainPeriod;

            int[] vals = new int[sti.Indicators.Length];
            for (int i = 0; i < sti.Indicators.Length; i++)
            {
                ZoneAlligator ind = sti.Indicators[i];

                TimeSpan period = TimeFrameHelper.GetTimeFrameInfo(ind.TimeFrame).TimeSpan;
                DateTime dt;
                if (period < mainPeriod)
                {
                    dt = endDt - period;
                }
                else if(period > mainPeriod)
                {
                    DateTime localDt = 
                        ind.Bars.OpenTimes[ind.Bars.OpenTimes.GetIndexByTime(dateTimeBar)];
                    dt = localDt == dateTimeBar ? dateTimeBar : dateTimeBar - period;
                }
                else
                {
                    dt = dateTimeBar;
                }

                int index = ind.Bars.OpenTimes.GetIndexByTime(dt);
                if (index < 0)
                    ind.Bars.LoadMoreHistory();

                //if (!IsCandleReliable(dateTimeBar, ind.Bars.OpenTimes[index], ind.Bars.TimeFrame))
                //    index--;

                double res = ind.Histogram[index];
                if (res == 0)
                    vals[i] = 0;
                else
                    vals[i] = ind.Histogram[index] > 0 ? 1 : -1;
            }
            
            if (vals.Count(a => a == 1) == sti.Indicators.Length)
                return TrendType.Bullish;
            if (vals.Count(a => a == -1) == sti.Indicators.Length)
                return TrendType.Bearish;
            return TrendType.NoTrend;
        }

        /// <summary>
        /// Determines whether a candle with open time <see cref="dateTimeCandleToCheck"/> is old enough to rely on (more than 50% has already past).
        /// </summary>
        /// <param name="dateTimeBar">The date time bar.</param>
        /// <param name="dateTimeCandleToCheck">The date time candle to check.</param>
        /// <param name="timeFrameToCheck">The time frame to check.</param>
        /// <returns>
        ///   <c>true</c> if the candle is reliable; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsCandleReliable(
            DateTime dateTimeBar, DateTime dateTimeCandleToCheck, TimeFrame timeFrameToCheck)
        {
            bool res = dateTimeBar < dateTimeCandleToCheck + 
                      TimeFrameHelper.TimeFrames[timeFrameToCheck].TimeSpan / 2;

            return res;
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
            if (IsCandleReliable(dateTimeBar, majorDateTime, barProvider.TimeFrame))
                majorIndex--;

            return majorIndex;
        }
#if !GARTLEY_PROD
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

                if (stochasticValue <= STOCHASTIC_UP && stochasticValuePrev > STOCHASTIC_UP)
                    return TrendType.Bearish;

                return TrendType.NoTrend;
            }

            return TrendType.NoTrend;
        }
#endif
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
            int indexStart = start.BarIndex;
            int indexEnd = end.BarIndex;
            int loopStart = indexEnd - DIVERGENCE_OFFSET_SEARCH;
            double macd = macdCrossOver.Histogram[indexEnd];
            double currentValHist = macdCrossOver.Histogram[loopStart];

            for (int i = loopStart; i >= indexStart; i--)
            {
                double localHist = macdCrossOver.Histogram[i];
                if (currentValHist * localHist < 0)
                    break;

                currentValHist = localHist;

                if (isBullSignal && barsProvider.GetLowPrice(i) < end.Value ||
                    !isBullSignal && barsProvider.GetHighPrice(i) > end.Value)
                    break;

                if (isBullSignal && currentValHist <= macd ||
                    !isBullSignal && currentValHist >= macd)
                {
                    // Find the inflection point of the histogram values
                    if (foundDivValue is null ||
                        isBullSignal && currentValHist <= foundDivValue ||
                        !isBullSignal && currentValHist >= foundDivValue)
                    {
                        foundDivValue = currentValHist;
                        continue;
                    }

                    int extremaIndex = i;
                    for (int j = i - 1; j >= indexStart; j--)
                    {
                        localHist = macdCrossOver.Histogram[j];

                        if (currentValHist * localHist < 0)
                            break;
                        currentValHist = localHist;

                        if (isBullSignal &&
                            barsProvider.GetLowPrice(j) > end.Value
                            && localHist < currentValHist ||
                            !isBullSignal &&
                            barsProvider.GetHighPrice(j) < end.Value
                            && localHist > currentValHist)
                        {

                            extremaIndex = j;
                            currentValHist = localHist;
                        }
                    }

                    var divItem = new BarPoint(isBullSignal
                        ? barsProvider.GetLowPrice(extremaIndex)
                        : barsProvider.GetHighPrice(extremaIndex), extremaIndex, barsProvider);
                    return divItem;
                }
            }

            return null;
        }

    }
}
