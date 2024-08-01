using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.AlgoBase
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
            double bandTop = sti.BollingerBands.Top.GetResultValue(barPoint.BarIndex);
            //double bandMain = sti.BollingerBands.GetResultValue(barPoint.BarIndex);
            double bandBottom = sti.BollingerBands.Bottom.GetResultValue(barPoint.BarIndex);

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
        public static TrendType GetTrend(ZoneAlligatorFinder alligator, DateTime dateTimeBar)
        {
            double value = alligator.GetResultValue(dateTimeBar);
            if (value > ZoneAlligatorFinder.NO_VALUE)
                return TrendType.BULLISH;
            if (value < ZoneAlligatorFinder.NO_VALUE)
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

            TimeSpan mainPeriod = TimeFrameHelper.GetTimeFrameInfo(sti.MainTrendIndicator.BarsProvider.TimeFrame).TimeSpan;
            DateTime endDt = dateTimeBar + mainPeriod;

            int[] vals = new int[sti.Indicators.Length];
            for (int i = 0; i < sti.Indicators.Length; i++)
            {
                ZoneAlligatorFinder ind = sti.Indicators[i];

                TimeSpan period = TimeFrameHelper.GetTimeFrameInfo(
                    ind.BarsProvider.TimeFrame).TimeSpan;
                DateTime dt;
                if (period < mainPeriod)
                {
                    dt = endDt - period;
                }
                else if(period > mainPeriod)
                {
                    DateTime localDt = 
                        ind.BarsProvider.GetOpenTime(ind.BarsProvider.GetIndexByTime(dateTimeBar));
                    dt = localDt == dateTimeBar ? dateTimeBar : dateTimeBar - period;
                }
                else
                {
                    dt = dateTimeBar;
                }

                int index = ind.BarsProvider.GetIndexByTime(dt);
                if (index < 0)
                    ind.BarsProvider.LoadBars(dt);

                //if (!IsCandleReliable(dateTimeBar, ind.Bars.OpenTimes[index], ind.Bars.TimeFrame))
                //    index--;

                double res = ind.GetResultValue(dt);
                if (res == 0)
                    vals[i] = 0;
                else
                    vals[i] = res > 0 ? 1 : -1;
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
                      TimeFrameHelper.TimeFrames[timeFrameToCheck.Name].TimeSpan / 2;

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

        /// <summary>
        /// Finds the divergence for the possible signal.
        /// </summary>
        /// <param name="awesomeOscillator">The AO instance.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="start">The start bar to search.</param>
        /// <param name="end">The end bar to search.</param>
        /// <param name="isBullSignal">if set to <c>true</c> the signal is bullish, otherwise bearish.</param>
        /// <returns>Start of the divergence bar or null if no divergence has been found.</returns>
        public static BarPoint FindDivergence(AwesomeOscillatorFinder awesomeOscillator,
            IBarsProvider barsProvider, BarPoint start, BarPoint end, bool isBullSignal)
        {
            int indexStart = start.BarIndex;
            int indexEnd = end.BarIndex;
            //int loopStart = indexEnd - DIVERGENCE_OFFSET_SEARCH;
            double endHistValue = awesomeOscillator.GetResultValue(indexEnd);

            if (endHistValue < 0 && !isBullSignal || endHistValue > 0 && isBullSignal)
                return null;

            if(indexStart > indexEnd)
                return null;

            double endValue = isBullSignal
                ? barsProvider.GetLowPrice(indexEnd)
                : barsProvider.GetHighPrice(indexEnd);

            for (int i = indexEnd-1; i >= indexStart; i--)
            {
                double localHist = awesomeOscillator.GetResultValue(i);

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

                double nextHist = awesomeOscillator.GetResultValue(i - 1);
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
