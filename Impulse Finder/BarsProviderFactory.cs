using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo
{
    public static class BarsProviderFactory
    {
        public static List<IBarsProvider> CreateCTraderBarsProviders(
            int analyzeBarsCount, 
            TimeFrame initialTimeFrame, 
            int analyzeDepth,
            MarketData marketData,
            Bars mainBars)
        {
            List<TimeFrame> timeFrames = TimeFrameHelper.GetMinorTimeFrames(
                initialTimeFrame, analyzeDepth);
            var bBarsProvider = new CTraderBarsProvider(
                mainBars, marketData, analyzeBarsCount);
            var resList = new List<IBarsProvider>{ bBarsProvider };
            bBarsProvider.LoadBars();

            IBarsProvider currentBarsProvider = bBarsProvider;
            foreach (TimeFrame timeFrame in timeFrames)
            {
                // We want to convert the start bar index the datetime
                // because there are other indices on other time frames.
                DateTime currentStartDate = currentBarsProvider.GetOpenTime(currentBarsProvider.StartIndexLimit);
                currentBarsProvider = GetBarProvider(marketData, timeFrame, currentStartDate);

                resList.Add(currentBarsProvider);
            }

            return resList;
        }

        /// <summary>
        /// Gets the bars of the specified time frame.
        /// </summary>
        /// <param name="marketData">The market data.</param>
        /// <param name="timeFrame">The time frame.</param>
        /// <param name="currentStartDate">The start date we want to load bars.</param>
        /// <returns>
        /// A new instance for the <see cref="timeFrame" />
        /// </returns>
        private static IBarsProvider GetBarProvider(
            MarketData marketData, TimeFrame timeFrame, DateTime currentStartDate)
        {
            //TODO How to handle the bar updating
            Bars bars = marketData.GetBars(timeFrame);
            if (bars.Count == 0)
            {
                // What to do if we cannot load the bars? Dunno
                bars.LoadMoreHistory();
            }

            do
            {
                bars.LoadMoreHistory();

            } while (bars[^1].OpenTime > currentStartDate);

            var res = new CTraderBarsProvider(bars, marketData, bars.Count);
            return res;
        }

    }
}
