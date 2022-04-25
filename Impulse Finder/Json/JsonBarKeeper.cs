using System;
using System.Diagnostics;
using System.IO;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.Json;
using Newtonsoft.Json;

namespace cAlgo
{
    /// <summary>
    /// Class can save and restore the market data from the chart to JSON file
    /// </summary>
    public class JsonBarKeeper
    {
        private readonly string m_JsonFilePath;
        private readonly int m_AnalyzedBarsCount;

        public JsonBarKeeper(string jsonFilePath, int analyzedBarsCount)
        {
            m_JsonFilePath = jsonFilePath;
            m_AnalyzedBarsCount = analyzedBarsCount;
        }

        public JsonBarKeeper(int analyzedBarsCount) : this(Path.Combine(Environment.CurrentDirectory, "main_history.json"), analyzedBarsCount)
        {
        }

        private JsonTimeFrame GetJsonTimeFrame(Bars bars, DateTime? startDate = null)
        {
            var jsonTimeFrame = new JsonTimeFrame
            {
                TimeFrameName = bars.TimeFrame.ToString()
            };
            if (startDate.HasValue)
            {
                Debugger.Launch();
                var brr = bars.LoadMoreHistory();
            }

            var jsonBars = new JsonBar[bars.Count];
            for (int i = 0; i < bars.Count; i++)
            {
                Bar bar = bars[i];
                jsonBars[i] = new JsonBar
                {
                    High = bar.High,
                    Low = bar.Low,
                    Index = i,
                    OpenTime = bar.OpenTime
                };
            }

            jsonTimeFrame.Bars = jsonBars;
            return jsonTimeFrame;
        }

        public void Save(
            Bars bars, MarketData marketData, TimeFrame[] minorTimeFrames)
        {
            // Use here IBarsProvider
            var history = new JsonHistory{ Symbol = bars.SymbolName};
            int timeFrameCount = minorTimeFrames.Length + 1;
            var jsonTimeFrames = new JsonTimeFrame[timeFrameCount];
            jsonTimeFrames[0] = GetJsonTimeFrame(bars);

            var startIndex = Math.Min(bars.Count, m_AnalyzedBarsCount);
            DateTime startDate = bars.OpenTimes[bars.Count - startIndex];

            for (int i = 0; i < minorTimeFrames.Length; i++)
            {
                Bars currentBars = marketData.GetBars(minorTimeFrames[i]);

                // We already got the main TF, so we add +1 here
                jsonTimeFrames[i + 1] = GetJsonTimeFrame(currentBars, startDate);
            }

            history.JsonTimeFrames = jsonTimeFrames;
            File.WriteAllText(m_JsonFilePath, JsonConvert.SerializeObject(history));
        }
    }
}
