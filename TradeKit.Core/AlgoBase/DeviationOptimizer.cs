using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Determines the optimal <c>deviationPercent</c> for <see cref="SimpleExtremumFinder"/>
    /// by finding the saturation point — the smallest deviation where further decrease
    /// no longer increases the number of zigzag segments.
    /// </summary>
    public class DeviationOptimizer
    {
        private const double DEFAULT_INITIAL_STEP = 0.5;
        private const double DEFAULT_REFINE_FACTOR = 0.5;
        private const int DEFAULT_MIN_ITERATIONS = 5;

        private readonly IBarsProvider m_BarsProvider;
        private readonly int m_StartIndex;
        private readonly int m_EndIndex;
        private readonly bool m_IsUpDirection;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviationOptimizer"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider with loaded candle data.</param>
        /// <param name="startIndex">Start bar index of the range to analyze.</param>
        /// <param name="endIndex">End bar index of the range to analyze.</param>
        /// <param name="isUpDirection">Initial zigzag direction (typically !isUp for the overall move).</param>
        public DeviationOptimizer(
            IBarsProvider barsProvider,
            int startIndex,
            int endIndex,
            bool isUpDirection)
        {
            m_BarsProvider = barsProvider;
            m_StartIndex = startIndex;
            m_EndIndex = endIndex;
            m_IsUpDirection = isUpDirection;
        }

        /// <summary>
        /// Gets the number of extremum points (not segments) for a given deviation percent.
        /// </summary>
        /// <param name="deviationPercent">The deviation percent threshold.</param>
        /// <returns>Number of extremum points found.</returns>
        public int GetExtremumCount(double deviationPercent)
        {
            var finder = new SimpleExtremumFinder(deviationPercent, m_BarsProvider, m_IsUpDirection);
            finder.Calculate(m_StartIndex, m_EndIndex);
            return finder.ToExtremaList().Count;
        }

        /// <summary>
        /// Finds the optimal deviation percent — the saturation point where further
        /// decrease no longer increases the number of zigzag segments.
        /// </summary>
        /// <param name="maxDeviation">
        /// Upper bound for binary search. If null, automatically determined as the deviation
        /// that produces exactly 2 extremum points (one segment).
        /// </param>
        /// <param name="minDeviation">
        /// Lower bound for deviation search. Default is 0.001 (very small moves).
        /// </param>
        /// <param name="stableSteps">
        /// Number of consecutive steps with the same segment count required to declare saturation.
        /// Default is 3.
        /// </param>
        /// <returns>
        /// The optimal deviation percent at the saturation point, or <paramref name="minDeviation"/>
        /// if no saturation is found.
        /// </returns>
        public double FindOptimalDeviation(
            double? maxDeviation = null,
            double minDeviation = 0.001,
            int stableSteps = 3)
        {
            // Step 1: find the upper bound (deviation producing minimal segments)
            double upper = maxDeviation ?? FindUpperBound();
            if (upper <= minDeviation)
                return minDeviation;

            // Step 2: sweep from upper to lower, tracking segment count
            // Use logarithmic steps for even coverage across orders of magnitude
            int totalSteps = Math.Max(20, (int)(Math.Log(upper / minDeviation) / Math.Log(1.2)));
            double ratio = Math.Pow(minDeviation / upper, 1.0 / totalSteps);

            int previousCount = GetExtremumCount(upper);
            double previousDeviation = upper;
            int stableCounter = 0;
            double saturationDeviation = upper;

            for (int step = 1; step <= totalSteps; step++)
            {
                double dev = upper * Math.Pow(ratio, step);
                if (dev < minDeviation)
                    dev = minDeviation;

                int count = GetExtremumCount(dev);

                if (count > previousCount)
                {
                    // Segment count increased — reset stability counter
                    stableCounter = 0;
                    saturationDeviation = dev;
                    previousCount = count;
                }
                else
                {
                    // Segment count did not increase
                    stableCounter++;
                    if (stableCounter >= stableSteps)
                    {
                        // Saturation reached: return the last deviation that increased count
                        return saturationDeviation;
                    }
                }

                previousDeviation = dev;
                if (dev <= minDeviation)
                    break;
            }

            return saturationDeviation;
        }

        /// <summary>
        /// Finds the deviation percent that produces exactly 2 extremum points
        /// (a single segment from start to end). Uses exponential growth from a small value.
        /// </summary>
        public double FindUpperBound()
        {
            // Start from a reasonable value and increase until only 2 points remain
            double dev = 0.1;
            const double maxUpperBound = 50.0;

            while (dev <= maxUpperBound)
            {
                int count = GetExtremumCount(dev);
                if (count <= 2)
                    return dev;
                dev *= 2.0;
            }

            return maxUpperBound;
        }
    }
}
