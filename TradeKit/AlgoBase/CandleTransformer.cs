using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    public static class CandleTransformer
    {
        /// <summary>
        /// Gets the profile for the set of candles.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="minPrice">The minimum price.</param>
        /// <param name="overlapsedIndex">Index of the overlapsed.</param>
        /// <returns>price-candles count dict.</returns>
        public static SortedDictionary<double, int> GetProfile(
            List<ICandle> candles, double minPrice, out double overlapsedIndex)
        {
            var points = new List<double>();
            foreach (ICandle candle in candles)
            {
                points.Add(candle.H);
                points.Add(candle.L);
            }

            double currentPoint = minPrice;
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
                double diff = nextPoint - currentPoint;
                profileInner.Add(nextPoint, cdlCount);

                if (cdlCount == 1) // gap (<1) or single candle (=1)
                {
                    return;
                }

                overlapsedIndexLocal += diff * cdlCount;
            }

            overlapsedIndex = overlapsedIndexLocal;

            foreach (double nextPoint in points.OrderBy(a => a).Skip(1))
            {
                if (Math.Abs(nextPoint - currentPoint) <= double.Epsilon)
                    continue;

                NextPoint(nextPoint);
                currentPoint = nextPoint;
            }

            return profileInner;
        }
    }
}
