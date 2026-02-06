namespace TradeKit.Core.ML
{
    /// <summary>
    /// Merges candles to fit the expected count for ML model input.
    /// </summary>
    public static class CandleMerger
    {
        /// <summary>
        /// Merges input candle features to match the expected candle count.
        /// </summary>
        /// <param name="features">The OHLC features array (4 values per candle: O, C, H, L).</param>
        /// <param name="candleCount">The actual candle count.</param>
        /// <param name="expectedCandleCount">The expected candle count for the model.</param>
        /// <returns>Merged features array with exactly expectedCandleCount candles.</returns>
        /// <exception cref="ArgumentException">Thrown when candleCount is less than expectedCandleCount.</exception>
        public static double[] MergeCandles(double[] features, int candleCount, int expectedCandleCount)
        {
            if (candleCount < expectedCandleCount)
            {
                throw new ArgumentException(
                    $"Input candle count ({candleCount}) is less than expected ({expectedCandleCount}). " +
                    "Cannot merge candles when there are fewer than required.",
                    nameof(candleCount));
            }

            if (candleCount == expectedCandleCount)
                return features;

            int excessCandles = candleCount - expectedCandleCount;
            int baseGroupSize = candleCount / expectedCandleCount;
            int remainder = candleCount % expectedCandleCount;

            int[] groupSizes = BuildGroupSizes(expectedCandleCount, baseGroupSize, remainder);

            double[] result = new double[expectedCandleCount * 4];
            int srcIndex = 0;

            for (int i = 0; i < expectedCandleCount; i++)
            {
                int groupSize = groupSizes[i];
                (double o, double c, double h, double l) = MergeGroup(features, srcIndex, groupSize);

                result[i * 4] = o;
                result[i * 4 + 1] = c;
                result[i * 4 + 2] = h;
                result[i * 4 + 3] = l;

                srcIndex += groupSize * 4;
            }

            return result;
        }

        /// <summary>
        /// Builds an array of group sizes, distributing extra candles from center outward.
        /// </summary>
        /// <param name="groupCount">The number of groups (expected candle count).</param>
        /// <param name="baseSize">The base size for each group.</param>
        /// <param name="extraCandles">The number of extra candles to distribute.</param>
        /// <returns>Array of group sizes.</returns>
        private static int[] BuildGroupSizes(int groupCount, int baseSize, int extraCandles)
        {
            int[] sizes = new int[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                sizes[i] = baseSize;
            }

            if (extraCandles <= 0)
                return sizes;

            int center = groupCount / 2;
            int left = center;
            int right = center;
            int distributed = 0;

            while (distributed < extraCandles)
            {
                if (left >= 0 && distributed < extraCandles)
                {
                    sizes[left]++;
                    distributed++;
                    left--;
                }

                if (right < groupCount && right != center && distributed < extraCandles)
                {
                    sizes[right]++;
                    distributed++;
                }

                right++;
            }

            return sizes;
        }

        /// <summary>
        /// Merges a group of consecutive candles into one.
        /// </summary>
        /// <param name="features">The source features array.</param>
        /// <param name="startIndex">The start index in features array.</param>
        /// <param name="groupSize">The number of candles to merge.</param>
        /// <returns>Merged OHLC values.</returns>
        private static (double O, double C, double H, double L) MergeGroup(
            double[] features, int startIndex, int groupSize)
        {
            if (groupSize <= 0)
                return (0, 0, 0, 0);

            if (groupSize == 1)
            {
                return (
                    features[startIndex],
                    features[startIndex + 1],
                    features[startIndex + 2],
                    features[startIndex + 3]);
            }

            double open = features[startIndex];
            double close = features[startIndex + (groupSize - 1) * 4 + 1];
            double high = double.MinValue;
            double low = double.MaxValue;

            for (int i = 0; i < groupSize; i++)
            {
                int idx = startIndex + i * 4;
                double h = features[idx + 2];
                double l = features[idx + 3];

                if (h > high) high = h;
                if (l < low) low = l;
            }

            return (open, close, high, low);
        }
    }
}
