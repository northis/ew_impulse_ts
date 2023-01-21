using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.PriceAction
{
    public class PriceActionSignalerBaseBot : 
        BaseRobot<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private const string BOT_NAME = "PriceActionRobot";
        private const int TREND_RATIO = 1;

        private readonly Plotly.NET.Color m_BearColor = 
            Plotly.NET.Color.fromARGB(240, 240, 128, 128);
        private readonly Plotly.NET.Color m_BullColor = 
            Plotly.NET.Color.fromARGB(240, 144, 238, 144);
        private readonly Plotly.NET.Color m_PatternBearColor = 
            Plotly.NET.Color.fromARGB(96, 240, 128, 128);
        private readonly Plotly.NET.Color m_PatternBullColor = 
            Plotly.NET.Color.fromARGB(96, 144, 238, 144);
        private readonly Plotly.NET.Color m_SlColor = Plotly.NET.Color.fromARGB(80, 240, 0, 0);
        private readonly Plotly.NET.Color m_TpColor = Plotly.NET.Color.fromARGB(80, 0, 240, 0);

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
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override PriceActionSetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(Bars, Symbol);
            HashSet<CandlePatternType> patternTypes = GetPatternsType();

            SuperTrendItem superTrendItem = null;
            if (UseTrendOnly)
                superTrendItem = SuperTrendItem.Create(TimeFrame, this, TREND_RATIO, barsProvider);

            double? breakEvenRatio = null;
            if (BreakEvenRatio > 0)
                breakEvenRatio = BreakEvenRatio;

            var setupFinder = new PriceActionSetupFinder(
                barsProvider, Symbol, UseStrengthBar, superTrendItem, patternTypes, breakEvenRatio);

            return setupFinder;
        }

        protected override void OnDrawChart(GenericChart.GenericChart candlestickChart, PriceActionSignalEventArgs signalEventArgs,
            PriceActionSetupFinder setupFinder, List<DateTime> chartDateTimes)
        {
            //TODO

            //int levelIndex = e.Level.BarIndex;
            //string name = $"{levelIndex}{e.ResultPattern.GetHashCode()}";
            //Color color = e.ResultPattern.IsBull ? m_BullColor : m_BearColor;

            //Chart.DrawText($"PA{name}", e.ResultPattern.Type.Format(),
            //        e.ResultPattern.StopLossBarIndex, e.ResultPattern.StopLoss, color)
            //    .ChartTextAlign(!e.ResultPattern.IsBull);

            //if (FillWithColor)
            //{
            //    int startIndex = e.ResultPattern.BarIndex - e.ResultPattern.BarsCount + 1;

            //    double max = double.MinValue;// yes, the price can be negative
            //    double min = double.MaxValue;
            //    for (int i = startIndex; i <= e.ResultPattern.BarIndex; i++)
            //    {
            //        max = Math.Max(m_BarsProvider.GetHighPrice(i), max);
            //        min = Math.Min(m_BarsProvider.GetLowPrice(i), min);
            //    }

            //    Color patternColor = e.ResultPattern.IsBull ? m_PatternBullColor : m_PatternBearColor;
            //    Chart.DrawRectangle($"F{name}", startIndex - 1, min, e.ResultPattern.BarIndex + 1,
            //            max, patternColor, LINE_WIDTH)
            //        .SetFilled();
            //}

            //if (ShowSetups)
            //{
            //    Chart.DrawRectangle($"SL{name}", levelIndex, e.Level.Value, levelIndex + SETUP_WIDTH,
            //            e.StopLoss.Value, m_SlColor, LINE_WIDTH)
            //        .SetFilled();
            //    Chart.DrawRectangle($"TP{name}", levelIndex, e.Level.Value, levelIndex + SETUP_WIDTH,
            //            e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
            //        .SetFilled();
            //}

            //Shape tp1 = GetSetupRectangle(
            //    setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit1);
            //candlestickChart.WithShape(tp1, true);
            //Shape tp2 = GetSetupRectangle(
            //    setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit2);
            //candlestickChart.WithShape(tp2, true);
            //Shape sl = GetSetupRectangle(
            //    setupStart, setupEnd, m_SlColor, levelStart, gartley.StopLoss);
            //candlestickChart.WithShape(sl, true);
            //candlestickChart.WithAnnotation(GetAnnotation(
            //    b1, b2, colorBorder, ratio.Ratio(), chartDateTimes), true);
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="!:TK" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            PriceActionSetupFinder setupFinder, PriceActionSignalEventArgs signal)
        {
            return false;
        }
    }
}
