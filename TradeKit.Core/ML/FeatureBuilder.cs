using TradeKit.Core.Common;
using TradeKit.Core.Json;

namespace TradeKit.Core.ML
{
    /// <summary>
    /// Builds fixed-size feature vectors from market data.
    /// </summary>
    public static class FeatureBuilder
    {
        /// <summary>
        /// Builds normalized OHLC features.
        /// </summary>
        /// <returns>Feature array or null when invalid.</returns>
        public static double[] BuildFeatures(
            BarPoint start, BarPoint end, IBarsProvider barsProvider, out int count)
        {
            count = end.BarIndex - start.BarIndex + 1;
            if (count <= 0)
                return null;

            double min = Math.Min(start.Value, end.Value);
            double max = Math.Max(start.Value, end.Value);
            double range = max - min;
            if (range <= 0)
                return null;

            double[] features = new double[count * 4];
            int index = 0;
            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                features[index++] = Normalize(barsProvider.GetOpenPrice(i), min, range);
                features[index++] = Normalize(barsProvider.GetClosePrice(i), min, range);
                features[index++] = Normalize(barsProvider.GetHighPrice(i), min, range);
                features[index++] = Normalize(barsProvider.GetLowPrice(i), min, range);
            }

            return features;
        }

        /// <summary>
        /// Builds normalized OHLC features.
        /// </summary>
        /// <param name="candles">The source candles.</param>
        /// <returns>Feature array or null when invalid.</returns>
        public static double[] BuildFeatures(IReadOnlyList<JsonCandleExport> candles)
        {
            if (candles == null || candles.Count == 0)
                return null;
            
            double min = candles.Min(a => a.L);
            double max = candles.Max(a => a.H);
            double range = max - min;
            if (range <= 0)
                return null;

            double[] features = new double[candles.Count * 4];
            int index = 0;
            foreach (JsonCandleExport candle in candles)
            {
                features[index++] = Normalize(candle.O, min, range);
                features[index++] = Normalize(candle.C, min, range);
                features[index++] = Normalize(candle.H, min, range);
                features[index++] = Normalize(candle.L, min, range);
            }

            return features;
        }

        private static double Normalize(double value, double min, double range)
        {
            return (value - min) / range;
        }
    }
}
