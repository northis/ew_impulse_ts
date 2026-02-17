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

            // No significant corrections allowed (max drawdown within movement)
            double correctionDepth = GetMaxCorrectionDepth(start, end, barsProvider, isUp, totalMovement);
            if (correctionDepth > MAX_CORRECTION_DEPTH)
                return false;

            // Candles should be tightly packed — path efficiency close to 1.0
            double pathEfficiency = GetPathEfficiencyRatio(start, end, barsProvider);
            if (pathEfficiency > MAX_PATH_EFFICIENCY_RATIO)
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
    }

}
