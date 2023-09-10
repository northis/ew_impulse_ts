using System;
using System.Collections.Generic;
using System.Diagnostics;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.EventArgs;
using Color = Plotly.NET.Color;
using Shape = Plotly.NET.LayoutObjects.Shape;

namespace TradeKit.PriceAction
{
    public class PriceActionSignalerBaseBot : 
        BaseRobot<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private const string BOT_NAME = "PriceActionRobot";

        private readonly Color m_BearColor = 
            Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColor = 
            Color.fromARGB(240, 144, 238, 144);
        private readonly Color m_PatternBearColor = 
            Color.fromARGB(96, 240, 128, 128);
        private readonly Color m_PatternBullColor = 
            Color.fromARGB(96, 144, 238, 144);
        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);

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
        /// Gets the bars provider.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override IBarsProvider GetBarsProvider(Bars bars, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars, symbolEntity);
            return barsProvider;
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override PriceActionSetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            IBarsProvider barsProvider = GetBarsProvider(bars, symbolEntity);
            HashSet<CandlePatternType> patternTypes = GetPatternsType();

            SuperTrendItem superTrendItem = null;
            if (UseTrendOnly)
                superTrendItem = SuperTrendItem.Create(bars.TimeFrame, this, symbolEntity.Name);

            double? breakEvenRatio = null;
            if (BreakEvenRatio > 0)
                breakEvenRatio = BreakEvenRatio;

            var setupFinder = new PriceActionSetupFinder(
                barsProvider, symbolEntity, UseStrengthBar, superTrendItem, patternTypes, breakEvenRatio);

            return setupFinder;
        }

        /// <summary>
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="candlestickChart">The main chart with candles.</param>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="chartDateTimes">Date times for bars got from the broker.</param>
        protected override void OnDrawChart(GenericChart.GenericChart candlestickChart, PriceActionSignalEventArgs signalEventArgs,
            IBarsProvider barProvider, List<DateTime> chartDateTimes)
        {
            CandlesResult pattern = signalEventArgs.ResultPattern;
            signalEventArgs.ResultPattern.GetDrawRectangle(barProvider,
                out int startIndex, out _, out double max, out double min);

            Color colorPattern = pattern.IsBull ? m_PatternBullColor : m_PatternBearColor;
            DateTime setupStart = barProvider.GetOpenTime(startIndex);
            DateTime setupEnd = signalEventArgs.Level.OpenTime;
            Shape patternRectangle = GetSetupRectangle(setupStart, setupEnd, colorPattern, max, min);
            candlestickChart.WithShape(patternRectangle, true);

            Color colorText = pattern.IsBull ? m_BullColor : m_BearColor;
            DateTime slIndex = barProvider.GetOpenTime(pattern.StopLossBarIndex);

            Annotation label = GetAnnotation(slIndex, pattern.StopLoss,
                colorText, CHART_FONT_HEADER, BlackColor, pattern.Type.Format().Replace(" ",""),
                pattern.IsBull ? StyleParam.YAnchorPosition.Top : StyleParam.YAnchorPosition.Bottom);
            
            candlestickChart.WithAnnotation(label, true);

            GetSetupEndRender(
                signalEventArgs.Level.OpenTime, barProvider.TimeFrame, 
                out DateTime realStart,
                out DateTime realEnd);

            double startPrice = signalEventArgs.ResultPattern.LimitPrice ?? signalEventArgs.Level.Value;
            Shape tp = GetSetupRectangle(realStart, realEnd, m_TpColor,
                startPrice, signalEventArgs.TakeProfit.Value);
            candlestickChart.WithShape(tp, true);
            Shape sl = GetSetupRectangle(realStart, realEnd, m_SlColor,
                startPrice, signalEventArgs.StopLoss.Value);
            candlestickChart.WithShape(sl, true);
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
