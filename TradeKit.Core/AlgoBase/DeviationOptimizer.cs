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
        /// Finds the optimal deviation percent — the saturation knee of the
        /// count-vs-deviation curve, i.e. the largest (coarsest) deviation that still
        /// captures the meaningful swing structure before the curve flattens into
        /// per-bar noise. The full deviation range is swept (no premature early-exit on
        /// the first plateau), then the elbow is located as the point of maximum vertical
        /// distance above the straight chord of the normalised (log-deviation, count) curve.
        /// </summary>
        /// <param name="maxDeviation">
        /// Upper bound for the sweep. If null, automatically determined as the deviation
        /// that produces exactly 2 extremum points (one segment).
        /// </param>
        /// <param name="minDeviation">
        /// Lower bound for deviation search. Default is 0.001 (very small moves).
        /// </param>
        /// <returns>
        /// The deviation percent at the saturation knee, or <paramref name="minDeviation"/>
        /// when the range is degenerate.
        /// </returns>
        public double FindOptimalDeviation(
            double? maxDeviation = null,
            double minDeviation = 0.001)
        {
            // Step 1: find the upper bound (deviation producing minimal segments)
            double upper = maxDeviation ?? FindUpperBound();
            if (upper <= minDeviation)
                return minDeviation;

            // Step 2: sweep the whole range with logarithmic steps for even coverage
            // across orders of magnitude, recording the segment count at each deviation.
            int totalSteps = Math.Max(20, (int)(Math.Log(upper / minDeviation) / Math.Log(1.2)));
            double ratio = Math.Pow(minDeviation / upper, 1.0 / totalSteps);

            var devs = new double[totalSteps + 1];
            var counts = new int[totalSteps + 1];
            for (int step = 0; step <= totalSteps; step++)
            {
                double dev = step == totalSteps ? minDeviation : upper * Math.Pow(ratio, step);
                if (dev < minDeviation)
                    dev = minDeviation;
                devs[step] = dev;
                counts[step] = GetExtremumCount(dev);
            }

            return FindKnee(devs, counts);
        }

        /// <summary>
        /// Locates the saturation knee of a non-decreasing count curve sampled across
        /// decreasing deviations. The knee is the sample with the maximum vertical
        /// distance above the chord joining the first (coarsest) and last (finest)
        /// samples on the normalised (log-deviation, count) plane — the elbow where the
        /// count stops growing meaningfully and only noise pivots remain at finer scales.
        /// </summary>
        private static double FindKnee(double[] devs, int[] counts)
        {
            int n = devs.Length;
            int minCount = counts[0];
            int maxCount = counts[n - 1];

            // No structural growth (e.g. a monotonic series): the coarsest deviation
            // already captures everything there is.
            if (maxCount <= minCount)
                return devs[0];

            double x0 = Math.Log(devs[0]);
            double dx = Math.Log(devs[n - 1]) - x0; // negative (fine < coarse)
            double dy = maxCount - minCount;

            double bestDist = double.NegativeInfinity;
            int bestIdx = 0;
            for (int i = 0; i < n; i++)
            {
                double nx = (Math.Log(devs[i]) - x0) / dx; // 0 (coarse) → 1 (fine)
                double ny = (counts[i] - minCount) / dy;   // 0 → 1
                double dist = ny - nx;
                if (dist > bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            return devs[bestIdx];
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
