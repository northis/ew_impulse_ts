using System.Diagnostics;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using static Microsoft.FSharp.Core.ByRefKinds;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Statistic-related metrics methods for the market part given.
    /// </summary>
    public static class MovementStatistic
    {
        /// <summary>
        /// Gets the statistic data for the movement (from start to the end).
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public static ImpulseResult GetMovementStatistic(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            var (heterogeneity, heterogeneityMax) = GetHeterogeneity(start, end, barsProvider);
            var (overlapseMaxDepth, overlapseMaxDistance) = GetMaxOverlapseScore(start, end, barsProvider);
            var (profile, overlapseDegree, singleCandle) = GetOverlapseStatistic(start, end, barsProvider);

            double den = start.Value > 0 ? start.Value : 1;
            double size = Math.Abs(start - end) / den;

            return new ImpulseResult(profile, overlapseDegree, overlapseMaxDepth, overlapseMaxDistance, heterogeneity, heterogeneityMax, end.BarIndex - start.BarIndex, size, singleCandle);
        }

        /// <summary>
        /// Gets the heterogeneity degree from 0 to 1 (avg square root, max).
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public static (double, double) GetHeterogeneity(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            double fullLength = Math.Abs(start.Value - end.Value);
            if (fullLength < double.Epsilon)
                return (1, 1);

            bool isUp = end > start;
            List<double> devs = new List<double>();

            int indexDiff = end.BarIndex - start.BarIndex;
            double min = Math.Min(start.Value, end.Value);
            double max = Math.Max(start.Value, end.Value);

            double dx = fullLength / (indexDiff > 0 ? indexDiff : 0.5);
            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                int count = i - start.BarIndex;
                Candle candle = Candle.FromIndex(barsProvider, i);

                double localLow = Math.Max(candle.L, min);
                double localHigh = Math.Min(candle.H, max);

                double currDx = count * dx;
                double currAvg = isUp
                    ? start.Value + currDx
                    : start.Value - currDx;
                double midPoint;
                if (i == start.BarIndex || i == end.BarIndex)
                    midPoint = 0;
                else midPoint = Math.Max(Math.Abs(localLow - currAvg), Math.Abs(localHigh - currAvg));

                double part = midPoint / fullLength;
                devs.Add(part);
            }

            double sqrtDev = Math.Sqrt(devs.Select(a => a * a).Average());
            double maxRes = devs.Max();
            return (sqrtDev, maxRes);
        }

        private static SortedDictionary<DateTime, double> GetPoints(
            BarPoint start, BarPoint end, Func<int, double> valueByIndex, IBarsProvider barsProvider)
        {
            var res = new SortedDictionary<DateTime, double>();
            int startIndex = start.BarIndex;
            int endIndex = end.BarIndex;

            for (int i = startIndex; i <= endIndex; i++) 
                res.Add(barsProvider.GetOpenTime(i), valueByIndex(i));

            return res;
        }

        private static bool IsUpCandle(DateTime dt, IBarsProvider barsProvider)
        {
            int index = barsProvider.GetIndexByTime(dt);
            return barsProvider.GetOpenPrice(index) < barsProvider.GetClosePrice(index);
        }

        /// <summary>
        /// Gets the maximum overlapse depth & distance (from 0 to 1)
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public static (double, double) GetMaxOverlapseScore(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            bool isUp = end > start;
            double length = Math.Abs(end - start);
            if (length < double.Epsilon)
                return (1, 1);

            double duration = Math.Abs(end.BarIndex - start.BarIndex);

            SortedDictionary<DateTime, double> hVals = GetPoints(
                start, end, barsProvider.GetHighPrice, barsProvider);

            SortedDictionary<DateTime, double> lVals = GetPoints(
                start, end, barsProvider.GetLowPrice, barsProvider);

            SortedDictionary<DateTime, double> inputKeys = isUp
                ? hVals
                : lVals;

            SortedDictionary<DateTime, double> inputCounterKeys = isUp
                ? lVals
                : hVals;

            SortedDictionary<DateTime, (double, DateTime)> scores =
                new SortedDictionary<DateTime, (double, DateTime)>();
            foreach (DateTime dt in Helper.GetKeysRange(inputKeys, start.OpenTime, end.OpenTime))
            {
                if ((dt == start.OpenTime || dt == end.OpenTime) && IsUpCandle(dt, barsProvider) != isUp)
                {
                    continue;
                }

                double currentPrice = inputKeys[dt];
                foreach (DateTime inputCounterKey in Helper.GetKeysRange(inputCounterKeys, dt, end.OpenTime))
                {
                    if (inputCounterKey == dt || inputCounterKey == end.OpenTime)
                        continue;

                    double localLength = (isUp ? 1 : -1) *
                                         (currentPrice - inputCounterKeys[inputCounterKey]);
                    if (localLength < 0)
                        break;

                    if (!scores.ContainsKey(dt) || scores[dt].Item1 < localLength)
                    {
                        scores[dt] = new ValueTuple<double, DateTime>(localLength, inputCounterKey);
                    }
                }
            }

            if (scores.Count > 0)
            {
                KeyValuePair<DateTime, (double, DateTime)> maxBy = scores.MaxBy(a => a.Value.Item1);
                double depth = maxBy.Value.Item1 / length;

                int startIndex = barsProvider.GetIndexByTime(maxBy.Key);
                int endIndex = barsProvider.GetIndexByTime(maxBy.Value.Item2);
                return (depth, duration > 0 ? (endIndex - startIndex) / duration : 0);
            }

            return (0, 0);
        }

        /// <summary>
        /// Gets the index of the overlapse.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public static (SortedDictionary<double, int>, double, double) GetOverlapseStatistic(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            bool isUp = end > start;
            var candles = new List<ICandle>();
            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                Candle cdl = Candle.FromIndex(barsProvider, i);
                candles.Add(cdl);
            }

            SortedDictionary<double, int> profile = GetProfile(
                candles, isUp, out double overlapseIndex, out double singleCandle);
            if (overlapseIndex > 1)
                overlapseIndex = 1;

            return (profile, overlapseIndex, singleCandle);
        }

        /// <summary>
        /// Gets the profile for the set of candles.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="isUp">True if we consider the set of candles as ascending movement, otherwise false.</param>
        /// <param name="overlapseIndex">The overlapse degree.</param>
        /// <param name="singleCandle">The single candle degree.</param>
        /// <returns>price-candles count dict.</returns>
        public static SortedDictionary<double, int> GetProfile(
            List<ICandle> candles, bool isUp, out double overlapseIndex, out double singleCandle)
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
            double singleCandleLocal = 0;
            overlapseIndex = overlapsedIndexLocal;

            var profileInner = new SortedDictionary<double, int>();
            void NextPoint(double nextPoint)
            {
                double localMax = Math.Max(nextPoint, currentPoint);
                double localMin = Math.Min(nextPoint, currentPoint);

                int cdlCount = candles.Count(a => 
                    a.L <= localMin && a.H >= localMax);

                cdlCount = cdlCount == 0 ? 1 : cdlCount;
                double diff = (nextPoint - currentPoint) * (isUp ? 1 : -1);
                profileInner.Add(nextPoint, cdlCount);

                double diffLength = diff / length;

                if (cdlCount == 1) // gap (<1) or single candle (=1)
                {
                    singleCandleLocal += diffLength;
                    return;
                }

                overlapsedIndexLocal += diffLength * cdlCount / countToCompare;
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

            overlapseIndex = candles.Count > 1 ? overlapsedIndexLocal : 1;
            singleCandle = 1 - singleCandleLocal;
            return profileInner;
        }
    }
}
