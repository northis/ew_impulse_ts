using System;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Core.Common;
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
        public static SpikeType GetSpike(SuperTrendItem sti, BarPoint barPoint)
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
        /// Gets the trend based on the "Bill Williams' Alligator + Zone" indicator.
        /// </summary>
        /// <param name="alligator">The zone alligator input.</param>
        /// <param name="dateTimeBar">The date and time of the current bar .</param>
        public static TrendType GetTrend(ZoneAlligator alligator, DateTime dateTimeBar)
        {
            int index = alligator.Bars.OpenTimes.GetIndexByTime(dateTimeBar);
            double value = alligator.Histogram[index];
            if (value > ZoneAlligator.NO_VALUE)
                return TrendType.BULLISH;
            if (value < ZoneAlligator.NO_VALUE)
                return TrendType.BEARISH;

            return TrendType.NO_TREND;
        }

        /// <summary>
        /// Gets the trend based on the "Super trend" indicator.
        /// </summary>
        /// <param name="sti">The super trend input.</param>
        /// <param name="dateTimeBar">The date and time of the current bar .</param>
        public static TrendType GetTrend(SuperTrendItem sti, DateTime dateTimeBar)
        {
            if (sti.Indicators.Length == 0)
                return TrendType.NO_TREND;

            TimeSpan mainPeriod = TimeFrameHelper.GetTimeFrameInfo(sti.MainTrendIndicator.TimeFrame.ToITimeFrame()).TimeSpan;
            DateTime endDt = dateTimeBar + mainPeriod;

            int[] vals = new int[sti.Indicators.Length];
            for (int i = 0; i < sti.Indicators.Length; i++)
            {
                SuperTrendIndicator ind = sti.Indicators[i];

                TimeSpan period = TimeFrameHelper.GetTimeFrameInfo(
                    ind.TimeFrame.ToITimeFrame()).TimeSpan;
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
                return TrendType.BULLISH;
            if (vals.Count(a => a == -1) == sti.Indicators.Length)
                return TrendType.BEARISH;
            return TrendType.NO_TREND;
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
            DateTime dateTimeBar, DateTime dateTimeCandleToCheck, ITimeFrame timeFrameToCheck)
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
                return TrendType.NO_TREND;

            double min = esi.BarsProviderMajor.GetLowPrice(majorIndex);
            double average = esi.MovingAverageMajor.Result[majorIndex];
            double histValue = esi.MacdCrossOverMajor.Histogram[majorIndex];
            double histValuePrev = esi.MacdCrossOverMajor.Histogram[majorIndex - 1];
            double stochasticValue = esi.StochasticMinor.PercentD[minorIndex];
            double stochasticValuePrev = esi.StochasticMinor.PercentD[minorIndex - 1];

            if (min > average)
            {
                if (histValue <= 0 || histValue < histValuePrev)
                    return TrendType.NO_TREND;

                if (stochasticValue >= STOCHASTIC_DOWN && stochasticValuePrev < STOCHASTIC_DOWN)
                    return TrendType.BULLISH;

                return TrendType.NO_TREND;

            }

            double max = esi.BarsProviderMajor.GetHighPrice(majorIndex);
            if (max < average)
            {
                if (histValue >= 0 || histValue > histValuePrev)
                    return TrendType.NO_TREND;

                if (stochasticValue <= STOCHASTIC_UP && stochasticValuePrev > STOCHASTIC_UP)
                    return TrendType.BEARISH;

                return TrendType.NO_TREND;
            }

            return TrendType.NO_TREND;
        }
#endif
        /// <summary>
        /// Finds the divergence for the possible signal.
        /// </summary>
        /// <param name="awesomeOscillator">The AO instance.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="start">The start bar to search.</param>
        /// <param name="end">The end bar to search.</param>
        /// <param name="isBullSignal">if set to <c>true</c> the signal is bullish, otherwise bearish.</param>
        /// <returns>Start of the divergence bar or null if no divergence has been found.</returns>
        public static BarPoint FindDivergence(AwesomeOscillatorIndicator awesomeOscillator,
            IBarsProvider barsProvider, BarPoint start, BarPoint end, bool isBullSignal)
        {
            int indexStart = start.BarIndex;
            int indexEnd = end.BarIndex;
            //int loopStart = indexEnd - DIVERGENCE_OFFSET_SEARCH;
            double endHistValue = awesomeOscillator.Result[indexEnd];

            if (endHistValue < 0 && !isBullSignal || endHistValue > 0 && isBullSignal)
                return null;

            if(indexStart > indexEnd)
                return null;

            double endValue = isBullSignal
                ? barsProvider.GetLowPrice(indexEnd)
                : barsProvider.GetHighPrice(indexEnd);

            for (int i = indexEnd-1; i >= indexStart; i--)
            {
                double localHist = awesomeOscillator.Result[i];

                if (isBullSignal && localHist > 0 ||
                    !isBullSignal && localHist < 0)
                    break;

                if (isBullSignal && barsProvider.GetLowPrice(i) < end.Value ||
                    !isBullSignal && barsProvider.GetHighPrice(i) > end.Value)
                    break;

                double currentValue = isBullSignal
                    ? barsProvider.GetLowPrice(i)
                    : barsProvider.GetHighPrice(i);

                if (isBullSignal && currentValue < endValue ||
                    !isBullSignal && currentValue > endValue)
                    break;

                if (isBullSignal && localHist > endHistValue ||
                    !isBullSignal && localHist < endHistValue)
                    continue;

                if (i - 1 <= indexStart)
                    break;

                double nextHist = awesomeOscillator.Result[i - 1];
                if ((!isBullSignal || !(nextHist > localHist)) &&
                    (isBullSignal || !(nextHist < localHist)))
                    continue;

                var divItem = new BarPoint(currentValue, i, barsProvider);
                return divItem;
            }

            return null;
        }

    }
}
