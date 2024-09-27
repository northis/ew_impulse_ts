namespace TradeKit.Core.Gartley
{
    /// <summary>
    /// Basic Gartley params
    /// </summary>
    public record GartleyParams(
        int BarDepthCount,
        double Accuracy,
        bool UseGartley,
        bool UseButterfly,
        bool UseShark,
        bool UseCrab,
        bool UseBat,
        bool UseAltBat,
        bool UseCypher,
        bool UseDeepCrab,
        bool UseDivergences,
        bool UseCandlePatterns,
        bool UseTrendOnly,
        double BreakEvenRatio)
    {

        /// <summary>
        /// Gets or sets the value how deep should we analyze the candles.
        /// </summary>
        public int BarDepthCount { get; set; } = BarDepthCount;

        /// <summary>
        /// Gets or sets the final accuracy.
        /// </summary>
        public double Accuracy { get; set; } = Accuracy;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.GARTLEY"/> pattern.
        /// </summary>
        public bool UseGartley { get; set; } = UseGartley;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BUTTERFLY"/> pattern.
        /// </summary>
        public bool UseButterfly { get; set; } = UseButterfly;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        public bool UseShark { get; set; } = UseShark;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        public bool UseCrab { get; set; } = UseCrab;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        public bool UseBat { get; set; } = UseBat;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        public bool UseAltBat { get; set; } = UseAltBat;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        public bool UseCypher { get; set; } = UseCypher;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        public bool UseDeepCrab { get; set; } = UseDeepCrab;

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        public bool UseDivergences { get; set; } = UseDivergences;

        /// <summary>
        /// Gets or sets a value indicating whether we should use candle patterns (Price Action).
        /// </summary>
        public bool UseCandlePatterns { get; set; } = UseCandlePatterns;

        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        public bool UseTrendOnly { get; set; } = UseTrendOnly;

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        public double BreakEvenRatio { get; set; } = BreakEvenRatio;
    }
}
