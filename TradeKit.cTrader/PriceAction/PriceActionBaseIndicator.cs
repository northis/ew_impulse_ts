using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.PriceAction;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.PriceAction
{
    /// <summary>
    /// This indicator can find Price Action candle patterns
    /// </summary>
    /// <seealso cref="BaseIndicator&lt;PriceActionSetupFinder, PriceActionSignalEventArgs&gt;" />
    public class PriceActionBaseIndicator : BaseIndicator<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private CTraderBarsProvider m_BarsProvider;
        private Color m_BearColor;
        private Color m_BullColor;
        private Color m_PatternBearColor;
        private Color m_PatternBullColor;
        private Color m_SlColor;
        private Color m_TpColor;
        private const int LINE_WIDTH = 1;
        private const int SETUP_WIDTH = 3;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_BearColor = Color.FromHex("#F0F08080");
            m_BullColor = Color.FromHex("#F090EE90");
            m_PatternBearColor = Color.FromHex("#60F08080");
            m_PatternBullColor = Color.FromHex("#6090EE90");
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            
            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);

            HashSet<CandlePatternType> patternTypes = GetPatternsType();

            SuperTrendItem superTrendItem = null;
            if (UseTrendOnly)
                superTrendItem = SuperTrendItem.Create(TimeFrame.ToITimeFrame(), m_BarsProvider);

            double? breakEvenRatio = null;
            if (BreakEvenRatio > 0)
                breakEvenRatio = BreakEvenRatio;

            var setupFinder = new PriceActionSetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), UseStrengthBar, superTrendItem, patternTypes, breakEvenRatio);
            Subscribe(setupFinder);
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
        /// Gets or sets a value indicating whether we fill the patterns with color.
        /// </summary>
        [Parameter("Fill with color", DefaultValue = true)]
        public bool FillWithColor { get; set; }

        private HashSet<CandlePatternType> GetPatternsType()
        {
            var res = new HashSet<CandlePatternType>();
            if (UseHammer)
            {
                res.Add(CandlePatternType.HAMMER);
                res.Add(CandlePatternType.INVERTED_HAMMER);
            }

            if (PinBar)
            {
                res.Add(CandlePatternType.UP_PIN_BAR);
                res.Add(CandlePatternType.DOWN_PIN_BAR);
            }

            if (OuterBar)
            {
                res.Add(CandlePatternType.UP_OUTER_BAR);
                res.Add(CandlePatternType.DOWN_OUTER_BAR);
            }

            if (OuterBarBodies)
            {
                res.Add(CandlePatternType.UP_OUTER_BAR_BODIES);
                res.Add(CandlePatternType.DOWN_OUTER_BAR_BODIES);
            }

            if (InnerBar)
            {
                res.Add(CandlePatternType.UP_INNER_BAR);
                res.Add(CandlePatternType.DOWN_INNER_BAR);
            }

            if (DoubleInnerBar)
            {
                res.Add(CandlePatternType.UP_DOUBLE_INNER_BAR);
                res.Add(CandlePatternType.DOWN_DOUBLE_INNER_BAR);
            }

            if (Ppr)
            {
                res.Add(CandlePatternType.UP_PPR);
                res.Add(CandlePatternType.DOWN_PPR);
            }

            if (Rails)
            {
                res.Add(CandlePatternType.UP_RAILS);
                res.Add(CandlePatternType.DOWN_RAILS);
            }

            if (PprIb)
            {
                res.Add(CandlePatternType.UP_PPR_IB);
                res.Add(CandlePatternType.DOWN_PPR_IB);
            }

            if (CPpr)
            {
                res.Add(CandlePatternType.UP_CPPR);
                res.Add(CandlePatternType.DOWN_CPPR);
            }

            return res;
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            DateTime dt = Bars[levelIndex].OpenTime;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");

            string type = e.HasBreakeven ? "Breakeven" : "SL";
            Logger.Write($"{type} hit! Price:{priceFmt} ({dt:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            DateTime dt = Bars[levelIndex].OpenTime;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({dt:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, PriceActionSignalEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            string name = $"{levelIndex}{e.ResultPattern.GetHashCode()}";
            Color color = e.ResultPattern.IsBull ? m_BullColor : m_BearColor;

            Chart.DrawText($"PA{name}", e.ResultPattern.Type.Format(),
                    e.ResultPattern.StopLossBarIndex, e.ResultPattern.StopLoss, color)
                .ChartTextAlign(!e.ResultPattern.IsBull);

            if (FillWithColor)
            {
                e.ResultPattern.GetDrawRectangle(m_BarsProvider,
                    out int startIndex, out int endIndex, out double max, out double min);
                Color patternColor = e.ResultPattern.IsBull ? m_PatternBullColor : m_PatternBearColor;
                Chart.DrawRectangle($"F{name}", 
                        startIndex, min, endIndex, max, patternColor, LINE_WIDTH)
                    .SetFilled();
            }

            if (ShowSetups)
            {
                Chart.DrawRectangle($"SL{name}", levelIndex, e.Level.Value, levelIndex + SETUP_WIDTH,
                        e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP{name}", levelIndex, e.Level.Value, levelIndex + SETUP_WIDTH,
                        e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                    .SetFilled();
            }
        }
    }
}
