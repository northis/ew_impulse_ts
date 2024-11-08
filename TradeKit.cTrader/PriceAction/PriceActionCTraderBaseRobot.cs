using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.PriceAction;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.PriceAction
{
    public abstract class PriceActionCTraderBaseRobot<T> 
        : CTraderBaseRobot<T, PriceActionSetupFinder, PriceActionSignalEventArgs>
        where T : BaseAlgoRobot<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        /// <summary>
        /// Joins the Price Action-specific parameters into one record.
        /// </summary>
        protected PriceActionParams GetPriceActionParams()
        {
            return new PriceActionParams(
                BreakEvenRatio, UseTrendOnly, UseHammer, PinBar, OuterBar, OuterBarBodies, InnerBar, DoubleInnerBar,
                Ppr, CPpr, Rails, PprIb, UseStrengthBar);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN,
            MaxValue = Helper.BREAKEVEN_MAX)]
        public double BreakEvenRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = true)]
        public bool UseTrendOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.HAMMER"/> and <see cref="CandlePatternType.INVERTED_HAMMER"/> patterns.
        /// </summary>
        [Parameter("Hammer", DefaultValue = false)]
        public bool UseHammer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PIN_BAR"/> and <see cref="CandlePatternType.DOWN_PIN_BAR"/> patterns.
        /// </summary>
        [Parameter("Pin Bar", DefaultValue = false)]
        public bool PinBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR"/> and <see cref="CandlePatternType.DOWN_OUTER_BAR"/> patterns.
        /// </summary>
        [Parameter("Outer Bar", DefaultValue = false)]
        public bool OuterBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR_BODIES"/> and <see cref="CandlePatternType.DOWN_OUTER_BAR_BODIES"/> patterns.
        /// </summary>
        [Parameter("Outer Bar Body", DefaultValue = false)]
        public bool OuterBarBodies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_INNER_BAR"/> and <see cref="CandlePatternType.DOWN_INNER_BAR"/> patterns.
        /// </summary>
        [Parameter("Inner Bar", DefaultValue = false)]
        public bool InnerBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_DOUBLE_INNER_BAR"/> and <see cref="CandlePatternType.DOWN_DOUBLE_INNER_BAR"/> patterns.
        /// </summary>
        [Parameter("Double Inner Bar", DefaultValue = false)]
        public bool DoubleInnerBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PPR"/> and <see cref="CandlePatternType.DOWN_PPR"/> patterns.
        /// </summary>
        [Parameter("PPR", DefaultValue = false)]
        public bool Ppr { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_CPPR"/> and <see cref="CandlePatternType.DOWN_CPPR"/> patterns.
        /// </summary>
        [Parameter("CPPR", DefaultValue = false)]
        public bool CPpr { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_RAILS"/> and <see cref="CandlePatternType.DOWN_RAILS"/> pattern.
        /// </summary>
        [Parameter("Rails", DefaultValue = true)]
        public bool Rails { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PPR_IB"/> and <see cref="CandlePatternType.DOWN_PPR_IB"/> pattern.
        /// </summary>
        [Parameter("PPR+Inner Bar", DefaultValue = true)]
        public bool PprIb { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should show only patterns with "the strength bar".
        /// </summary>
        [Parameter("Use strength bar", DefaultValue = false)]
        public bool UseStrengthBar { get; set; }

        #endregion
    }
}
