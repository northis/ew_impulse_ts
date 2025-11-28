using System.Diagnostics;
using System.Net;
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
        /// <param name="overlapseMaxDepthMaxLimit">Optional max value of overlapse to reduce calculations</param>
        /// <param name="rateZigzagMaxLimit">Optional max value of rate zigzag to reduce calculations</param>
        /// <param name="rateHeterogeneityMaxLimit">Optional max value of heterogeneity to reduce calculations</param>
        public static ImpulseResult GetMovementStatistic(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            double? overlapseMaxDepthMaxLimit = null,
            double? rateZigzagMaxLimit = null,
            double? rateHeterogeneityMaxLimit = null)
        {
            var (overlapseMaxDepth, _, rz, _) = GetMaxOverlapseScore(start, end, barsProvider,
                overlapseMaxDepthMaxLimit, rateZigzagMaxLimit);
            double heterogeneityMax = 1;
            //if (rateZigzag < 1)
            //{
            //var (heterogeneity, heterogeneityMaxLoc) = GetHeterogeneity(start, end, barsProvider, rateHeterogeneityMaxLimit);
            //    heterogeneityMax = heterogeneityMaxLoc;
            //}

            //double pullback = GetPullbackScore(start, end, barsProvider);
            
            SortedDictionary<double, int> profiles = GetOverlapseStatistic(start, end, barsProvider).Item1;
            int decCount = profiles.Count / 10;

            double middlePart = 1;
            double diffPart = 1;
            //double diffDec = 1;
            double firstDecPart = 1;
            if (decCount >= 1)
            {
                int firstThird = profiles.Take(decCount * 3).Sum(a => a.Value);
                int secondThird = profiles.Skip(decCount * 3).Take(decCount * 4).Sum(a => a.Value);
                int lastThird = profiles.Skip(decCount * 7).Sum(a => a.Value);
                int whole = profiles.Sum(a => a.Value);
                middlePart = (double)secondThird / whole;
                diffPart = Math.Abs((double)firstThird - lastThird) / whole;
                
                int firstDec = profiles.Take(decCount).Sum(a => a.Value);
                int lastDec = profiles.Skip(decCount * 9).Sum(a => a.Value);
                //diffDec = Math.Abs((double)firstDec - lastDec) / whole;
                firstDecPart = (double)firstDec / whole;
            }

            //double entropy = GetEntropy(profileVals);

            //var (heterogeneity, _) = GetHeterogeneity(start, end, barsProvider, rateHeterogeneityMaxLimit);

            //var (profile, overlapseDegree, singleCandle) = GetOverlapseStatistic(start, end, barsProvider);

            double size = Math.Abs(start - end) / Math.Abs(Math.Min(start.Value, end.Value));
            //return new ImpulseResult(profile, overlapseDegree, overlapseMaxDepth, overlapseMaxDistance, heterogeneity,
            //    heterogeneityMax, end.BarIndex - start.BarIndex, size, singleCandle, rateZigzag);

            double area = GetEnvelopeAreaScore(start, end, barsProvider);
            int count = end.BarIndex - start.BarIndex;
            return new ImpulseResult(rz, count, size, overlapseMaxDepth, diffPart, area);
        }

        public static double GetEntropy(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            double sum = values.Sum();
            if (sum == 0)
                return 0;

            double[] p = values.Select(v => v / sum).ToArray();
            double entropy = 0;
            foreach (double pi in p)
            {
                if (pi > 0)
                    entropy -= pi * Math.Log(pi, 2);
            }

            double maxEntropy = Math.Log(values.Count, 2);
            double unevenness = 1.0 - (entropy / maxEntropy);
            return unevenness;
        }

        /// <summary>
        /// Gets the heterogeneity degree from 0 to 1 (avg square root, max).
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="rateHeterogeneityMaxLimit">Optional max value of heterogeneity to reduce calculations</param>
        public static (double, double) GetHeterogeneity(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            double? rateHeterogeneityMaxLimit = null)
        {
            double fullLength = Math.Abs(start.Value - end.Value);
            if (fullLength < double.Epsilon)
                return (1, 1);

            //if (rateHeterogeneityMaxLimit.HasValue)
            //    rateHeterogeneityMaxLimit *= fullLength;

            bool isUp = end > start;
            List<double> devs = new List<double>();

            int indexDiff = end.BarIndex - start.BarIndex;
            double min = Math.Min(start.Value, end.Value);
            double max = Math.Max(start.Value, end.Value);

            double dx = fullLength / (indexDiff > 0 ? indexDiff : 0.5);
            //double currentValue = start.Value;
            double maxSum = fullLength * indexDiff;

            if (indexDiff < 3)
                return (1, 1);

            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                if (i == start.BarIndex)
                    continue;

                //double open = barsProvider.GetOpenPrice(i);

                int count = i - start.BarIndex;
                double localLow = Math.Max(barsProvider.GetLowPrice(i), min);
                double localHigh = Math.Min(barsProvider.GetHighPrice(i), max);

                double currDx = count * dx;
                double currAvg = isUp
                    ? start.Value + currDx
                    : start.Value - currDx;
                //double midPoint;
                //if (i == start.BarIndex || i == end.BarIndex)
                //    midPoint = 0;
                //else midPoint = Math.Max(Math.Abs(localLow - currAvg), Math.Abs(localHigh - currAvg));

                //if (rateHeterogeneityMaxLimit < midPoint)
                //    return (1, 1);

                //double part = midPoint / fullLength;
                //devs.Add(part);

                double part = Math.Max(Math.Abs(localLow - currAvg), Math.Abs(localHigh - currAvg));
                devs.Add(part);
            }

            double ent = GetEntropy(devs);
            //double sqrtDev = Math.Sqrt(devs.Select(a => a * a).Average()) / fullLength;
            //double sumRelative = devs.Count > 0 ? devs.Sum() / maxSum : 1;
            double maxDevs = devs.Max();

            //int third = Convert.ToInt32(devs.Count / 3);
            //double range = maxDevs - devs.Min();
            //if (range < double.Epsilon)
            //    return (1, 1);

            //double firstThird = devs.Take(third).Average();
            //double secondThird = devs.TakeLast(third).Take(third).Average();
            //double lastThird = devs.TakeLast(third).Average();
            //double rev1Thirds = Math.Abs(firstThird - lastThird) / fullLength;
            //double rev2Thirds = Math.Abs(secondThird - lastThird) / fullLength;
            //double maxD = 1 - devs.Average() / maxDevs;

            return (ent, maxDevs);
        }

        /// <summary>
        /// Calculates the normalized area of the ribbon stretched over highs (top) and lows (bottom) between two bar points.
        /// 0 means the ribbon is a straight segment between start and end, 1 corresponds to the rectangle that spans
        /// the whole price range of the movement across every bar between start and end.
        /// </summary>
        /// <param name="start">The start point.</param>
        /// <param name="end">The end point.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <returns>Normalized ribbon area from 0 to 1.</returns>
        public static double GetEnvelopeAreaScore(BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            double fullLength = Math.Abs(start.Value - end.Value);
            if (fullLength < double.Epsilon)
                return 0;

            int startIndex = start.BarIndex;
            int endIndex = end.BarIndex;
            int barCount = endIndex - startIndex;
            if (barCount <= 0)
                return 0;

            double minPrice = Math.Min(start.Value, end.Value);
            double maxPrice = Math.Max(start.Value, end.Value);

            var upperCandidates = new List<Point>();
            var lowerCandidates = new List<Point>();

            upperCandidates.Add(new Point(startIndex, start.Value));
            lowerCandidates.Add(new Point(startIndex, start.Value));

            for (int i = startIndex + 1; i < endIndex; i++)
            {
                double high = barsProvider.GetHighPrice(i);
                double low = barsProvider.GetLowPrice(i);

                double clampedHigh = Math.Min(Math.Max(high, minPrice), maxPrice);
                double clampedLow = Math.Max(Math.Min(low, maxPrice), minPrice);

                upperCandidates.Add(new Point(i, clampedHigh));
                lowerCandidates.Add(new Point(i, clampedLow));
            }

            upperCandidates.Add(new Point(endIndex, end.Value));
            lowerCandidates.Add(new Point(endIndex, end.Value));

            var upperEnvelope = BuildEnvelope(upperCandidates, true);
            var lowerEnvelope = BuildEnvelope(lowerCandidates, false);

            if (upperEnvelope.Count < 2 || lowerEnvelope.Count < 2)
                return 0;

            var polygon = new List<Point>(upperEnvelope);
            for (int i = lowerEnvelope.Count - 2; i > 0; i--)
            {
                polygon.Add(lowerEnvelope[i]);
            }

            double area = ComputeArea(polygon);
            double maxArea = fullLength * barCount;
            if (maxArea <= double.Epsilon)
                return 0;

            double score = area / maxArea;
            if (score < 0)
                return 0;
            if (score > 1)
                return 1;

            return score;
        }

        private static List<Point> BuildEnvelope(IReadOnlyList<Point> candidates, bool isUpper)
        {
            List<Point> envelope = new List<Point>();
            foreach (Point candidate in candidates)
            {
                while (envelope.Count >= 2)
                {
                    Point penultimate = envelope[envelope.Count - 2];
                    Point last = envelope[envelope.Count - 1];
                    double cross = GetCrossProduct(penultimate, last, candidate);
                    bool shouldRemove = isUpper ? cross >= 0 : cross <= 0;
                    if (!shouldRemove)
                        break;

                    envelope.RemoveAt(envelope.Count - 1);
                }

                envelope.Add(candidate);
            }

            return envelope;
        }

        private static double GetCrossProduct(Point first, Point second, Point third)
        {
            double firstDx = second.Index - first.Index;
            double firstDy = second.Value - first.Value;
            double secondDx = third.Index - second.Index;
            double secondDy = third.Value - second.Value;
            return firstDx * secondDy - firstDy * secondDx;
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
        /// Gets the pullback score from 0 to 1, where 0 means monotonic movement without pullbacks,
        /// and 1 means theoretical maximum pullback (each bar travels the full movement length in opposite direction).
        /// </summary>
        /// <param name="start">The start point.</param>
        /// <param name="end">The end point.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <returns>Pullback score from 0 to 1.</returns>
        public static double GetPullbackScore(BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            bool isUp = end > start;
            double length = Math.Abs(end - start);
            if (length < double.Epsilon)
                return 0;

            int barCount = end.BarIndex - start.BarIndex;
            if (barCount <= 0)
                return 0;

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

            // Calculate the ideal monotonic progression for each bar
            double totalPullbackScore = 0;
            double previousExtreme = start.Value;

            foreach (DateTime dt in Helper.GetKeysRange(inputKeys, start.OpenTime, end.OpenTime))
            {
                if (dt == start.OpenTime)
                    continue;

                double currentMainExtreme = inputKeys[dt];
                double currentCounterExtreme = inputCounterKeys[dt];

                // Check if there is a pullback (movement against the main direction)
                double pullbackDistance = 0;
                if (isUp)
                {
                    // For upward movement, pullback is when low goes below previous high
                    if (currentCounterExtreme < previousExtreme)
                        pullbackDistance = previousExtreme - currentCounterExtreme;
                }
                else
                {
                    // For downward movement, pullback is when high goes above previous low
                    if (currentCounterExtreme > previousExtreme)
                        pullbackDistance = currentCounterExtreme - previousExtreme;
                }

                // Add to total pullback score (distance * 1 bar)
                totalPullbackScore += pullbackDistance;

                // Update previous extreme if we moved in the main direction
                if (isUp && currentMainExtreme > previousExtreme)
                    previousExtreme = currentMainExtreme;
                else if (!isUp && currentMainExtreme < previousExtreme)
                    previousExtreme = currentMainExtreme;
            }

            // Normalize: theoretical maximum is when each bar travels the full length in opposite direction
            double theoreticalMax = length * barCount;
            double normalizedScore = theoreticalMax > 0 ? totalPullbackScore / theoreticalMax : 0;

            return Math.Min(normalizedScore, 1.0);
        }

        /// <summary>
        /// Gets the maximum overlapse depth & distance & ratio ZigZag (from 0 to 1) & extrema polygon area/length (from 0 to 1) 
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="overlapseMaxDepthMaxLimit">Optional max value of overlapse to reduce calculations</param>
        /// <param name="rateZigzagMaxLimit">Optional max value of rate zigzag to reduce calculations</param>
        public static (double, double, double, double) GetMaxOverlapseScore(
        BarPoint start, BarPoint end, IBarsProvider barsProvider,
        double? overlapseMaxDepthMaxLimit = null, double? rateZigzagMaxLimit = null)
        {
            bool isUp = end > start;
            double length = Math.Abs(end - start);
            if (length < double.Epsilon)
                return (1, 1, 1, 1);
            int duration = Math.Abs(end.BarIndex - start.BarIndex);

            if (overlapseMaxDepthMaxLimit.HasValue)
                overlapseMaxDepthMaxLimit *= length;
            if (rateZigzagMaxLimit.HasValue)
                rateZigzagMaxLimit *= duration;

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
            Dictionary<DateTime, int> extremaBarCounters = new Dictionary<DateTime, int>();

            Point current = new(start.BarIndex, start.Value);
            var points = new List<Point> { current };
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

                    if (overlapseMaxDepthMaxLimit < localLength)
                        return (1, 1, 1, 1);


                    if (!scores.ContainsKey(dt) || scores[dt].Item1 < localLength)
                    {
                        scores[dt] = new ValueTuple<double, DateTime>(localLength, inputCounterKey);
                    }
                }

                if (!scores.TryGetValue(dt, out (double, DateTime) score))
                    continue;

                if (isUp && current.Value < currentPrice || !isUp && current.Value > currentPrice)
                {
                    current = new(barsProvider.GetIndexByTime(dt), currentPrice);
                    points.Add(current);
                }

                double localExtremum = inputCounterKeys[score.Item2];
                int extremaBarCount = 0;
                for (int i = barsProvider.GetIndexByTime(dt); i >= start.BarIndex; i--)
                {
                    if (isUp && barsProvider.GetLowPrice(i) <= localExtremum ||
                        !isUp && barsProvider.GetHighPrice(i) >= localExtremum)
                        break;
                    extremaBarCount++;

                    if (rateZigzagMaxLimit < extremaBarCount)
                        return (1, 1, 1, 1);

                }

                extremaBarCounters[dt] = extremaBarCount;
            }

            if (scores.Count <= 0) return (0, 0, 0, 0);

            current = new(end.BarIndex, end.Value);
            points.Add(current);
            foreach (KeyValuePair<DateTime, (double, DateTime)> score in scores.Reverse())
            {
                double localValue = inputCounterKeys[score.Value.Item2];
                if (isUp && localValue > current.Value || !isUp && localValue < current.Value)
                {
                    continue;
                }

                Point localCurrent = new(barsProvider.GetIndexByTime(score.Value.Item2), localValue);
                current = localCurrent;
                points.Add(localCurrent);
            }

            double area = ComputeArea(points);

            KeyValuePair<DateTime, (double, DateTime)> maxBy = scores.MaxBy(a => a.Value.Item1);
            double depth = maxBy.Value.Item1 / length;
            
            int startIndex = barsProvider.GetIndexByTime(maxBy.Key);
            int endIndex = barsProvider.GetIndexByTime(maxBy.Value.Item2);
            double distance = duration > 0 ? (endIndex - startIndex) / (double)duration : 0;
            int max = extremaBarCounters.Max(a => a.Value);
            double ratioZigZag = duration > 0 ? max / (double)duration : 0;

            double areaRelative = area / (duration * length);
            return (depth, distance, ratioZigZag, areaRelative);
        }

        public static double ComputeArea(List<Point> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0;

            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                int j = (i + 1) % polygon.Count;
                area += polygon[i].Index * polygon[j].Value - polygon[j].Index * polygon[i].Value;
            }

            return Math.Abs(area) / 2.0;
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
            double overlappedIndexLocal = 0;
            double singleCandleLocal = 0;
            overlapseIndex = overlappedIndexLocal;

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

                overlappedIndexLocal += diffLength * cdlCount / countToCompare;
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

            overlapseIndex = candles.Count > 1 ? overlappedIndexLocal : 1;
            singleCandle = singleCandleLocal;
            return profileInner;
        }
    }
}
