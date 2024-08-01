namespace TradeKit.Core.PriceAction
{
    /// <summary>
    /// Basic PA params
    /// </summary>
    public record PriceActionParams(
        double BreakEvenRatio,
        bool UseTrendOnly,
        bool UseHammer,
        bool PinBar,
        bool OuterBar,
        bool OuterBarBodies,
        bool InnerBar,
        bool DoubleInnerBar,
        bool Ppr,
        bool CPpr,
        bool Rails,
        bool PprIb,
        bool UseStrengthBar)
    {
        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        public double BreakEvenRatio { get; set; } = BreakEvenRatio;

        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        public bool UseTrendOnly { get; set; } = UseTrendOnly;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.HAMMER"/> and <see cref="CandlePatternType.INVERTED_HAMMER"/> patterns.
        /// </summary>
        public bool UseHammer { get; set; } = UseHammer;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PIN_BAR"/> and <see cref="CandlePatternType.DOWN_PIN_BAR"/> patterns.
        /// </summary>
        public bool PinBar { get; set; } = PinBar;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR"/> and <see cref="CandlePatternType.DOWN_OUTER_BAR"/> patterns.
        /// </summary>
        public bool OuterBar { get; set; } = OuterBar;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR_BODIES"/> and <see cref="CandlePatternType.DOWN_OUTER_BAR_BODIES"/> patterns.
        /// </summary>
        public bool OuterBarBodies { get; set; } = OuterBarBodies;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_INNER_BAR"/> and <see cref="CandlePatternType.DOWN_INNER_BAR"/> patterns.
        /// </summary>
        public bool InnerBar { get; set; } = InnerBar;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_DOUBLE_INNER_BAR"/> and <see cref="CandlePatternType.DOWN_DOUBLE_INNER_BAR"/> patterns.
        /// </summary>
        public bool DoubleInnerBar { get; set; } = DoubleInnerBar;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PPR"/> and <see cref="CandlePatternType.DOWN_PPR"/> patterns.
        /// </summary>
        public bool Ppr { get; set; } = Ppr;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_CPPR"/> and <see cref="CandlePatternType.DOWN_CPPR"/> patterns.
        /// </summary>
        public bool CPpr { get; set; } = CPpr;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_RAILS"/> and <see cref="CandlePatternType.DOWN_RAILS"/> pattern.
        /// </summary>
        public bool Rails { get; set; } = Rails;

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PPR_IB"/> and <see cref="CandlePatternType.DOWN_PPR_IB"/> pattern.
        /// </summary>
        public bool PprIb { get; set; } = PprIb;

        /// <summary>
        /// Gets or sets a value indicating whether we should show only patterns with "the strength bar".
        /// </summary>
        public bool UseStrengthBar { get; set; } = UseStrengthBar;
    }
}
