using System;
using System.Collections.Generic;
using System.IO;
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

        public JsonBarKeeper() : this(
            Path.Combine(Environment.CurrentDirectory, "main_history.json"))
        {
        }

        /// <summary>
        /// Saves the specified providers to JSON file.
        /// </summary>
        /// <param name="providers">The providers.</param>
        /// <param name="symbolName">Name of the symbol.</param>
        public void Save(List<IBarsProvider> providers, string symbolName)
        {
            var history = new JsonHistory{ Symbol = symbolName };
            int timeFrameCount = providers.Count;
            var jsonTimeFrames = new JsonTimeFrame[timeFrameCount];

            for (var i = 0; i < providers.Count; i++)
            {
                IBarsProvider provider = providers[i];
                var jsonTimeFrame= new JsonTimeFrame
                {
                    TimeFrameName = provider.TimeFrame.ToString()
                };

                int offset = provider.StartIndexLimit;
                jsonTimeFrame.Bars = new JsonBar[provider.Count - offset];
                for (int j = provider.StartIndexLimit; j < provider.Count; j++)
                {
                    jsonTimeFrame.Bars[j - offset] = new JsonBar
                    {
                        High = provider.GetHighPrice(j),
                        Low = provider.GetLowPrice(j),
                        Index = j,
                        OpenTime = provider.GetOpenTime(j)
                    };
                }
                jsonTimeFrames[i] = jsonTimeFrame;
            }

            history.JsonTimeFrames = jsonTimeFrames;
            File.WriteAllText(m_JsonFilePath, JsonConvert.SerializeObject(history));
        }
    }
}
