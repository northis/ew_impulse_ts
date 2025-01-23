using System.Diagnostics;
using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    public static class CandleTransformer
    {
        /// <summary>
        /// Gets the index of the overlapsed.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="isUp">if set to <c>true</c> [is up].</param>
        /// <returns></returns>
        public static double GetOverlapsedIndex(
            BarPoint start, BarPoint end, IBarsProvider barsProvider, bool isUp)
        {
            var candles = new List<ICandle>();
            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                Candle cdl = Candle.FromIndex(barsProvider, i);
                candles.Add(cdl);
            }

            GetProfile(candles, isUp, out double overlapsedIndex);
            if (overlapsedIndex > 1)
                overlapsedIndex = 1;

            return overlapsedIndex;
        }

        /// <summary>
        /// Gets the profile for the set of candles.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="isUp">True if we consider the set of candles as ascending movement, otherwise false.</param>
        /// <param name="overlapsedIndex">Index of the overlapsed.</param>
        /// <returns>price-candles count dict.</returns>
        public static SortedDictionary<double, int> GetProfile(
            List<ICandle> candles, bool isUp, out double overlapsedIndex)
        {
            var points = new List<double>();
            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (ICandle candle in candles)
            {
                points.Add(candle.H);
                points.Add(candle.L);
                min = Math.Min(min, candle.L);
                max = Math.Max(max, candle.H);
            }

            double length = max - min;
            int countToCompare = candles.Count - 1;//except the current candle
            double currentPoint = isUp ? min : max;
            double overlapsedIndexLocal = 0;
            overlapsedIndex = overlapsedIndexLocal;

            var profileInner = new SortedDictionary<double, int>();
            void NextPoint(double nextPoint)
            {
                int cdlCount = candles.Count(a => 
                    a.L < currentPoint && a.H > currentPoint ||
                    a.L >= currentPoint && a.H <= nextPoint ||
                    a.L < nextPoint && a.H > nextPoint ||
                    a.L <= currentPoint && a.H >= nextPoint);

                cdlCount = cdlCount == 0 ? 1 : cdlCount;
                double diff = (nextPoint - currentPoint) * (isUp ? 1 : -1);
                profileInner.Add(nextPoint, cdlCount);

                if (cdlCount == 1) // gap (<1) or single candle (=1)
                {
                    return;
                }

                overlapsedIndexLocal += diff / length * cdlCount / countToCompare;
            }

            IOrderedEnumerable<double> orderedPoints = isUp 
                ? points.OrderBy(a => a) 
                : points.OrderByDescending(a => a);

            foreach (double nextPoint in orderedPoints.Skip(1))
            {
                if (Math.Abs(nextPoint - currentPoint) <= double.Epsilon)
                    continue;

                NextPoint(nextPoint);
                currentPoint = nextPoint;
            }
            overlapsedIndex = overlapsedIndexLocal;

            return profileInner;
        }
    }
}
