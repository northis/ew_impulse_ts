using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Determines whether a price movement between two bar points
    /// represents a smooth impulse based on shape and correction analysis.
    /// A smooth impulse is a movement with no significant corrections where candles
    /// are tightly packed, forming either a straight line or a hysteresis loop shape.
    /// </summary>
    public static class SmoothImpulseClassifier
    {
        /// <summary>
        /// Maximum allowed correction (drawdown) depth as a fraction of the total movement.
        /// </summary>
        private const double MAX_CORRECTION_DEPTH = 0.35;

        /// <summary>
        /// Maximum allowed path length to straight-line distance ratio.
        /// Values close to 1.0 mean perfectly smooth; higher values indicate choppiness.
        /// </summary>
        private const double MAX_PATH_EFFICIENCY_RATIO = 2.0;

        /// <summary>
        /// For line shape: minimum price contribution of each third of the movement.
        /// </summary>
        private const double LINE_THIRD_MIN_CONTRIBUTION = 0.15;

        /// <summary>
        /// For line shape: maximum price contribution of each third of the movement.
        /// </summary>
        private const double LINE_THIRD_MAX_CONTRIBUTION = 0.55;

        /// <summary>
        /// For hysteresis shape: minimum price contribution of the middle third.
        /// </summary>
        private const double HYSTERESIS_MIDDLE_MIN_CONTRIBUTION = 0.45;

        /// <summary>
        /// For hysteresis shape: maximum allowed ratio between the larger and smaller edge thirds.
        /// </summary>
        private const double HYSTERESIS_EDGE_MAX_RATIO = 3.0;

        /// <summary>
        /// Minimum drawdown fraction (relative to total movement) to count as a correction episode.
        /// </summary>
        private const double ZIGZAG_EPISODE_THRESHOLD = 0.12;

        /// <summary>
        /// Maximum number of distinct correction episodes allowed for a smooth impulse.
        /// </summary>
        private const int MAX_CORRECTION_EPISODES = 1;

        /// <summary>
        /// Maximum allowed ratio of per-bar roughness between the two halves of the movement.
        /// </summary>
        private const double MAX_ROUGHNESS_RATIO = 3.5;

        /// <summary>
        /// Minimum per-bar roughness (normalized by total movement) required to trigger the ratio check.
        /// </summary>
        private const double MIN_ROUGHNESS_FOR_RATIO_CHECK = 0.02;

        /// <summary>
        /// Determines whether the movement between the specified start and end points is a smooth impulse.
        /// A smooth impulse has no significant corrections, candles are tightly packed,
        /// and the shape is either a straight line (uniform progress) or a hysteresis loop
        /// (slow start, fast middle, slow end).
        /// </summary>
        /// <param name="start">The start point of the movement.</param>
        /// <param name="end">The end point of the movement.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <returns><c>true</c> if the movement is a smooth impulse; otherwise, <c>false</c>.</returns>
        public static bool IsSmoothImpulse(BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            int barCount = end.BarIndex - start.BarIndex;
            if (barCount < 3)
                return false;

            bool isUp = end.Value > start.Value;
            double totalMovement = Math.Abs(end.Value - start.Value);
            if (totalMovement < double.Epsilon)
                return false;

            // Start and end must be the extremes of the entire movement (no truncation in wave 5)
            if (!AreExtremesValid(start, end, barsProvider, isUp, totalMovement))
                return false;

            // Reject if there is an unclosed price gap in the movement
            if (MovementStatistic.HasUnclosedGap(start, end, barsProvider))
                return false;

            // No significant corrections allowed (max drawdown within movement)
            double correctionDepth = GetMaxCorrectionDepth(start, end, barsProvider, isUp, totalMovement);
            if (correctionDepth > MAX_CORRECTION_DEPTH)
                return false;

            // Candles should be tightly packed — path efficiency close to 1.0
            double pathEfficiency = GetPathEfficiencyRatio(start, end, barsProvider);
            if (pathEfficiency > MAX_PATH_EFFICIENCY_RATIO)
                return false;

            // Reject zigzag-like patterns with multiple correction episodes
            if (IsZigzagLike(start, end, barsProvider, isUp, totalMovement))
                return false;

            // Reject movements with unevenly distributed corrections
            if (HasUnevenCorrections(start, end, barsProvider, isUp, totalMovement))
                return false;

            // Determine shape type: straight line or hysteresis loop
            (double firstThird, double secondThird, double thirdThird) =
                GetThirdsContribution(start, end, barsProvider, isUp, totalMovement);

            return IsLineShape(firstThird, secondThird, thirdThird) ||
                   IsHysteresisShape(firstThird, secondThird, thirdThird);
        }

        /// <summary>
        /// Verifies that the start and end points are the true extremes of the movement
        /// (no candle exceeds the start-end price range). Rejects truncated wave 5 scenarios.
        /// </summary>
        private static bool AreExtremesValid(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            bool isUp, double totalMovement)
        {
            double tolerance = totalMovement * 0.001;

            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                double high = barsProvider.GetHighPrice(i);
                double low = barsProvider.GetLowPrice(i);

                if (isUp)
                {
                    if (low < start.Value - tolerance || high > end.Value + tolerance)
                        return false;
                }
                else
                {
                    if (high > start.Value + tolerance || low < end.Value - tolerance)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Computes the maximum drawdown from the running extreme, normalized by total movement.
        /// A low value means the movement has no significant pullbacks.
        /// </summary>
        private static double GetMaxCorrectionDepth(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            bool isUp, double totalMovement)
        {
            double runningExtreme = start.Value;
            double maxDrawdown = 0;

            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                double high = barsProvider.GetHighPrice(i);
                double low = barsProvider.GetLowPrice(i);

                if (isUp)
                {
                    runningExtreme = Math.Max(runningExtreme, high);
                    maxDrawdown = Math.Max(maxDrawdown, runningExtreme - low);
                }
                else
                {
                    runningExtreme = Math.Min(runningExtreme, low);
                    maxDrawdown = Math.Max(maxDrawdown, high - runningExtreme);
                }
            }

            return maxDrawdown / totalMovement;
        }

        /// <summary>
        /// Computes the ratio of the total close-to-close path length to the straight-line distance.
        /// Values close to 1.0 indicate a smooth, directional movement; higher values indicate choppiness.
        /// </summary>
        private static double GetPathEfficiencyRatio(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            double straightDistance = Math.Abs(end.Value - start.Value);
            if (straightDistance < double.Epsilon)
                return double.MaxValue;

            double pathLength = 0;
            double prevPrice = start.Value;

            for (int i = start.BarIndex + 1; i <= end.BarIndex; i++)
            {
                double currPrice = i < end.BarIndex
                    ? barsProvider.GetClosePrice(i)
                    : end.Value;
                pathLength += Math.Abs(currPrice - prevPrice);
                prevPrice = currPrice;
            }

            return pathLength / straightDistance;
        }

        /// <summary>
        /// Computes the fraction of total price progress contributed by each third of the movement
        /// (divided by bar count into three equal segments).
        /// </summary>
        private static (double first, double second, double third) GetThirdsContribution(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            bool isUp, double totalMovement)
        {
            int startIndex = start.BarIndex;
            int barCount = end.BarIndex - startIndex;

            int firstBoundaryIndex = startIndex + barCount / 3;
            int secondBoundaryIndex = startIndex + 2 * barCount / 3;

            double firstBoundaryPrice = barsProvider.GetClosePrice(firstBoundaryIndex);
            double secondBoundaryPrice = barsProvider.GetClosePrice(secondBoundaryIndex);

            double sign = isUp ? 1.0 : -1.0;
            double firstProgress = sign * (firstBoundaryPrice - start.Value) / totalMovement;
            double secondProgress = sign * (secondBoundaryPrice - firstBoundaryPrice) / totalMovement;
            double thirdProgress = sign * (end.Value - secondBoundaryPrice) / totalMovement;

            return (firstProgress, secondProgress, thirdProgress);
        }

        /// <summary>
        /// Checks whether the thirds distribution matches a straight line pattern
        /// (approximately equal contribution from each third).
        /// </summary>
        private static bool IsLineShape(double first, double second, double third)
        {
            return first >= LINE_THIRD_MIN_CONTRIBUTION && first <= LINE_THIRD_MAX_CONTRIBUTION &&
                   second >= LINE_THIRD_MIN_CONTRIBUTION && second <= LINE_THIRD_MAX_CONTRIBUTION &&
                   third >= LINE_THIRD_MIN_CONTRIBUTION && third <= LINE_THIRD_MAX_CONTRIBUTION;
        }

        /// <summary>
        /// Checks whether the thirds distribution matches a hysteresis loop pattern
        /// (slow start, fast middle, slow end with comparable edge thirds).
        /// </summary>
        private static bool IsHysteresisShape(double first, double second, double third)
        {
            if (second < HYSTERESIS_MIDDLE_MIN_CONTRIBUTION)
                return false;

            if (first < 0 || third < 0)
                return false;

            double edgeMin = Math.Min(first, third);
            double edgeMax = Math.Max(first, third);

            if (edgeMin < double.Epsilon)
                return edgeMax < 0.1;

            return edgeMax / edgeMin <= HYSTERESIS_EDGE_MAX_RATIO;
        }

        /// <summary>
        /// Detects zigzag-like patterns by counting the number of distinct correction episodes
        /// where the drawdown from the running extreme exceeds a threshold.
        /// </summary>
        private static bool IsZigzagLike(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            bool isUp, double totalMovement)
        {
            int episodes = 0;
            bool inCorrection = false;
            double runningExtreme = start.Value;

            for (int i = start.BarIndex; i <= end.BarIndex; i++)
            {
                double high = barsProvider.GetHighPrice(i);
                double low = barsProvider.GetLowPrice(i);

                double mainPrice = isUp ? high : low;
                double counterPrice = isUp ? low : high;

                if (isUp ? mainPrice > runningExtreme : mainPrice < runningExtreme)
                    runningExtreme = mainPrice;

                double drawdown = Math.Abs(runningExtreme - counterPrice) / totalMovement;

                if (drawdown > ZIGZAG_EPISODE_THRESHOLD)
                {
                    if (!inCorrection)
                    {
                        episodes++;
                        inCorrection = true;
                    }
                }
                else
                {
                    inCorrection = false;
                }
            }

            return episodes > MAX_CORRECTION_EPISODES;
        }

        /// <summary>
        /// Checks whether corrections are unevenly distributed across the movement
        /// by comparing the per-bar roughness of the first and second halves.
        /// </summary>
        private static bool HasUnevenCorrections(
            BarPoint start, BarPoint end, IBarsProvider barsProvider,
            bool isUp, double totalMovement)
        {
            int startIndex = start.BarIndex;
            int endIndex = end.BarIndex;
            int barCount = endIndex - startIndex;

            if (barCount < 6)
                return false;

            int midIndex = startIndex + barCount / 2;

            double firstHalfRoughness = GetSegmentRoughness(startIndex, midIndex, barsProvider, isUp);
            double secondHalfRoughness = GetSegmentRoughness(midIndex, endIndex, barsProvider, isUp);

            int firstLen = midIndex - startIndex;
            int secondLen = endIndex - midIndex;

            double firstNorm = firstLen > 0 ? firstHalfRoughness / (firstLen * totalMovement) : 0;
            double secondNorm = secondLen > 0 ? secondHalfRoughness / (secondLen * totalMovement) : 0;

            double maxNorm = Math.Max(firstNorm, secondNorm);
            double minNorm = Math.Min(firstNorm, secondNorm);

            if (maxNorm < MIN_ROUGHNESS_FOR_RATIO_CHECK)
                return false;

            if (minNorm < double.Epsilon)
                return maxNorm > MIN_ROUGHNESS_FOR_RATIO_CHECK;

            return maxNorm / minNorm > MAX_ROUGHNESS_RATIO;
        }

        /// <summary>
        /// Computes the total counter-direction movement (roughness) within a bar segment.
        /// </summary>
        private static double GetSegmentRoughness(
            int fromIndex, int toIndex, IBarsProvider barsProvider, bool isUp)
        {
            double roughness = 0;

            for (int i = fromIndex + 1; i <= toIndex; i++)
            {
                double prevClose = barsProvider.GetClosePrice(i - 1);
                double currClose = barsProvider.GetClosePrice(i);
                double change = currClose - prevClose;

                if (isUp ? change < 0 : change > 0)
                    roughness += Math.Abs(change);
            }

            return roughness;
        }
    }

}
