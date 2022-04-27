using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.Json;

namespace cAlgo
{
    /// <summary>
    /// Governs the creation of instances of <see cref="IBarsProvider"/>
    /// </summary>
    public static class BarsProviderFactory
    {
        /// <summary>
        /// Creates the <see cref="JsonBarsProvider"/> providers.
        /// </summary>
        /// <param name="analyzeBarsCount">The analyze bars count.</param>
        /// <param name="initialTimeFrame">The initial time frame.</param>
        /// <param name="analyzeDepth">The analyze depth.</param>
        /// <param name="jsonHistory">The json history.</param>
        public static List<IBarsProvider> CreateJsonBarsProviders(
            int analyzeBarsCount,
            TimeFrame initialTimeFrame,
            int analyzeDepth,
            JsonHistory jsonHistory)
        {
            List<TimeFrame> timeFrames = TimeFrameHelper.GetMinorTimeFrames(
                initialTimeFrame, analyzeDepth);
            var bBarsProvider = new JsonBarsProvider(jsonHistory, initialTimeFrame, analyzeBarsCount);
            var resList = new List<IBarsProvider> { bBarsProvider };
            bBarsProvider.LoadBars();

            IBarsProvider currentBarsProvider = bBarsProvider;
            foreach (TimeFrame timeFrame in timeFrames)
            {
                // We want to convert the start bar index the datetime
                // because there are other indices on other time frames.
                DateTime currentStartDate = currentBarsProvider.GetOpenTime(currentBarsProvider.StartIndexLimit);
                var jsonBarsProvider = new JsonBarsProvider(jsonHistory, timeFrame);
                int limit = jsonBarsProvider.GetIndexByTime(currentStartDate);
                jsonBarsProvider.Limit = limit;
                currentBarsProvider = jsonBarsProvider;
                resList.Add(jsonBarsProvider);
            }

            return resList;
        }

        /// <summary>
        /// Creates the <see cref="CTraderBarsProvider"/> providers.
        /// </summary>
        /// <param name="analyzeBarsCount">The analyze bars count.</param>
        /// <param name="initialTimeFrame">The initial time frame.</param>
        /// <param name="analyzeDepth">The analyze depth.</param>
        /// <param name="marketData">The market data.</param>
        /// <param name="mainBars">The main bars.</param>
        /// <returns>The list of instances</returns>
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
                currentBarsProvider = GetCTraderBarProvider(marketData, timeFrame, currentStartDate);

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
        private static IBarsProvider GetCTraderBarProvider(
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

            } while (bars[0].OpenTime > currentStartDate);

            var res = new CTraderBarsProvider(bars, marketData, bars.Count);
            return res;
        }

    }
}
