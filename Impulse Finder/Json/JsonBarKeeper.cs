using System;
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

        public JsonBarKeeper(string jsonFilePath)
        {
            m_JsonFilePath = jsonFilePath;
        }

        public JsonBarKeeper() : this(Path.Combine(Environment.CurrentDirectory, "main_history.json"))
        {
        }

        private JsonTimeFrame GetJsonTimeFrame(
            Bars bars, DateTime? startDateTime = null)
        {
            var jsonTimeFrame = new JsonTimeFrame
            {
                TimeFrameName = bars.TimeFrame.ToString()
            };
            if (startDateTime.HasValue)
            {
                do
                {
                    bars.LoadMoreHistory();
                } while (bars[0].OpenTime > startDateTime.Value);
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

            return jsonTimeFrame;
        }

        public void Save(
            Bars bars, MarketData marketData, TimeFrame[] minorTimeFrames)
        {
            var history = new JsonHistory{ Symbol = bars.SymbolName};
            int timeFrameCount = minorTimeFrames.Length + 1;
            var jsonTimeFrames = new JsonTimeFrame[timeFrameCount];
            jsonTimeFrames[0] = GetJsonTimeFrame(bars);
            DateTime startDateTime = bars[0].OpenTime;

            for (int i = 0; i < minorTimeFrames.Length; i++)
            {
                Bars currentBars = marketData.GetBars(minorTimeFrames[i]);

                // We already got the main TF, so we add +1 here
                jsonTimeFrames[i + 1] = GetJsonTimeFrame(currentBars, startDateTime);
            }

            history.JsonTimeFrames = jsonTimeFrames;
            using StreamWriter file = File.CreateText(m_JsonFilePath);
            var serializer = new JsonSerializer();
            serializer.Serialize(file, history);
        }
    }
}
