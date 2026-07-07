using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Estimates the zigzag <c>Period</c> (the
    /// <see cref="TradeKit.Core.Indicators.DeviationExtremumFinder"/> scale rate, where
    /// <c>deviationPercent = period × 0.01</c>) that maximises the number of trade setups
    /// for a given instrument and timeframe.
    /// <para>
    /// The period is derived from the instrument's <b>percentage volatility</b> — the
    /// median bar range as a fraction of price (in basis points) — rather than from the
    /// absolute price. Because the deviation threshold is already a percentage of price,
    /// the optimal period is <b>scale-invariant</b>: XAUUSD (~2000) and EURUSD (~1.1) with
    /// the same percentage volatility share the same optimal period. The timeframe enters
    /// only through its effect on that volatility (m15 bars move less in % than h1 bars).
    /// </para>
    /// <para>
    /// Coefficients were calibrated by <c>PeriodSweepTests</c> over the saved
    /// <c>data/</c> archive (10 files, both m15 &amp; h1, price scales from ~0.9 to ~2000):
    /// see <c>reports/period_sweep.md</c>. The impulse setup count peaks at
    /// <c>period ≈ 3.3 × medianBarBps</c> across every file and timeframe. Triangles are
    /// far rarer and resolve only at a <b>fine</b> period (their finder additionally sweeps
    /// a <c>+50</c> band upward), so the triangle base period is kept small.
    /// </para>
    /// </summary>
    public static class AutoPeriodEstimator
    {
        /// <summary>Impulse: period ≈ 3.3 × median bar range (bps). Calibrated peak.</summary>
        private const double IMPULSE_BPS_COEFFICIENT = 3.3;

        /// <summary>Triangle base period is kept fine (its finder bands +50 upward).</summary>
        private const double TRIANGLE_BPS_COEFFICIENT = 0.7;

        private const int IMPULSE_MIN_PERIOD = 8;
        private const int IMPULSE_MAX_PERIOD = 120;
        private const int TRIANGLE_MIN_PERIOD = 5;
        private const int TRIANGLE_MAX_PERIOD = 20;

        /// <summary>Bars sampled (most recent) to measure the median percentage bar range.</summary>
        private const int VOLATILITY_SAMPLE_BARS = 8000;

        /// <summary>Minimum bars required to trust the volatility estimate.</summary>
        private const int MIN_SAMPLE_BARS = 200;

        /// <summary>Fallback median bar range (bps) when history is too short to sample.</summary>
        private const double FALLBACK_BPS = 12.0;

        /// <summary>
        /// Estimates the impulse zigzag period from the instrument's percentage volatility.
        /// </summary>
        /// <param name="provider">The bars provider with loaded history.</param>
        /// <returns>The estimated period (scale rate), clamped to a sane range.</returns>
        public static int EstimateImpulsePeriod(IBarsProvider provider)
        {
            double bps = MedianBarBps(provider);
            int period = (int)Math.Round(IMPULSE_BPS_COEFFICIENT * bps);
            return Math.Clamp(period, IMPULSE_MIN_PERIOD, IMPULSE_MAX_PERIOD);
        }

        /// <summary>
        /// Estimates the triangle base zigzag period. Kept fine because triangles are
        /// small consolidations that only resolve at a small deviation; the triangle
        /// finder additionally sweeps a <c>+50</c> band above this base.
        /// </summary>
        /// <param name="provider">The bars provider with loaded history.</param>
        /// <returns>The estimated base period (scale rate), clamped to a fine range.</returns>
        public static int EstimateTrianglePeriod(IBarsProvider provider)
        {
            double bps = MedianBarBps(provider);
            int period = (int)Math.Round(TRIANGLE_BPS_COEFFICIENT * bps);
            return Math.Clamp(period, TRIANGLE_MIN_PERIOD, TRIANGLE_MAX_PERIOD);
        }

        /// <summary>
        /// Returns the median bar range as a fraction of price in basis points
        /// (<c>(High-Low)/Close × 10000</c>) over the most recent
        /// <see cref="VOLATILITY_SAMPLE_BARS"/> bars, or <see cref="FALLBACK_BPS"/> when
        /// too little history is available.
        /// </summary>
        public static double MedianBarBps(IBarsProvider provider)
        {
            int count = provider.Count;
            if (count < MIN_SAMPLE_BARS)
                return FALLBACK_BPS;

            int from = Math.Max(0, count - VOLATILITY_SAMPLE_BARS);
            var bps = new List<double>(count - from);
            for (int i = from; i < count; i++)
            {
                double close = provider.GetClosePrice(i);
                if (close <= 0)
                    continue;
                double range = provider.GetHighPrice(i) - provider.GetLowPrice(i);
                bps.Add(range / close * 10000.0);
            }

            if (bps.Count == 0)
                return FALLBACK_BPS;

            bps.Sort();
            return bps[bps.Count / 2];
        }
    }
}
