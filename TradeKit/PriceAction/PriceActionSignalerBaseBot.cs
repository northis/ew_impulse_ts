using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.PriceAction
{
    public class PriceActionSignalerBaseBot : BaseRobot<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private const string BOT_NAME = "PriceActionRobot";

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX)]
        public double BreakEvenRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = false)]
        public bool UseTrendOnly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.HAMMER"/> and <see cref="CandlePatternType.INVERTED_HAMMER"/> patterns.
        /// </summary>
        [Parameter("Hammer", DefaultValue = false)]
        public bool UseHammer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PIN_BAR"/> and <see cref="CandlePatternType.DOWN_PIN_BAR"/> patterns.
        /// </summary>
        [Parameter("Pin Bar", DefaultValue = true)]
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
        [Parameter("PPR", DefaultValue = true)]
        public bool Ppr { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_CPPR"/> and <see cref="CandlePatternType.DOWN_CPPR"/> patterns.
        /// </summary>
        [Parameter("CPPR", DefaultValue = true)]
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
        [Parameter("Use strength bar", DefaultValue = true)]
        public bool UseStrengthBar { get; set; }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override PriceActionSetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="!:TK" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(PriceActionSetupFinder setupFinder, PriceActionSignalEventArgs signal)
        {
            return false;
        }
    }
}
