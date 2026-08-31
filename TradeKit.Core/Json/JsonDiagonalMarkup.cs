using Newtonsoft.Json;
using TradeKit.Core.Common;

namespace TradeKit.Core.Json
{
    /// <summary>
    /// Serializable representation of a found contracting diagonal (DIAGONAL.md):
    /// the 0-1-2-3-4-5 skeleton plus the counter-move signal levels.
    /// </summary>
    public class JsonDiagonalMarkup
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("timeframe")]
        public string Timeframe { get; set; }

        [JsonProperty("isUp")]
        public bool IsUp { get; set; }

        [JsonProperty("points")]
        public JsonMarkupPoint[] Points { get; set; }

        [JsonProperty("entry")]
        public JsonMarkupPoint Entry { get; set; }

        [JsonProperty("takeProfit")]
        public JsonMarkupPoint TakeProfit { get; set; }

        [JsonProperty("stopLoss")]
        public JsonMarkupPoint StopLoss { get; set; }

        /// <summary>
        /// Builds the exportable record from a diagonal signal's wave points and levels.
        /// </summary>
        public static JsonDiagonalMarkup FromSignal(
            string symbol, string timeframe, BarPoint[] wavePoints,
            BarPoint entry, BarPoint takeProfit, BarPoint stopLoss)
        {
            if (wavePoints == null || wavePoints.Length < 2)
                return null;

            return new JsonDiagonalMarkup
            {
                Model = "DIAGONAL_CONTRACTING",
                Symbol = symbol,
                Timeframe = timeframe,
                IsUp = wavePoints[1].Value > wavePoints[0].Value,
                Points = wavePoints.Select(JsonMarkupPoint.FromBarPoint).ToArray(),
                Entry = JsonMarkupPoint.FromBarPoint(entry),
                TakeProfit = JsonMarkupPoint.FromBarPoint(takeProfit),
                StopLoss = JsonMarkupPoint.FromBarPoint(stopLoss),
            };
        }
    }
}
