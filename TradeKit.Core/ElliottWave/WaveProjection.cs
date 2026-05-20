using System.Collections.Generic;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Represents a single Fibonacci-based price projection for an active/upcoming wave.
    /// </summary>
    public record WaveProjection(
        /// <summary>Target price level.</summary>
        double Price,
        /// <summary>Projected bar index where this level may be reached (trendline-based).</summary>
        int BarIndex,
        /// <summary>Fibonacci ratio label (e.g. "0.618", "1.618").</summary>
        string RatioLabel,
        /// <summary>Weight [0,1] from the Fibonacci map (higher = more typical).</summary>
        double Weight,
        /// <summary>Name of the projected wave (e.g. "3", "c").</summary>
        string WaveName);

    /// <summary>
    /// A cluster zone where multiple projections from different levels converge.
    /// </summary>
    public record ClusterZone(
        /// <summary>Lower boundary of the cluster.</summary>
        double PriceLow,
        /// <summary>Upper boundary of the cluster.</summary>
        double PriceHigh,
        /// <summary>Sum of weights from contributing projections.</summary>
        double CombinedWeight,
        /// <summary>Projected bar range [from, to].</summary>
        int BarFrom,
        int BarTo,
        /// <summary>Contributing projection labels for display.</summary>
        string[] Labels);

    /// <summary>
    /// Full prediction result combining the best partial model with its projections.
    /// </summary>
    public class PredictionResult
    {
        /// <summary>Best-scoring partial or complete model.</summary>
        public ExactParsedNode Model { get; set; }

        /// <summary>Individual price projections for the active segment.</summary>
        public List<WaveProjection> Projections { get; set; } = new();

        /// <summary>Cluster zones where multiple Fibonacci levels converge.</summary>
        public List<ClusterZone> Clusters { get; set; } = new();

        /// <summary>Adjusted score incorporating partial-fit penalty.</summary>
        public double AdjustedScore { get; set; }
    }
}
