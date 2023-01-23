using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Indicators;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    public class GartleyFinderBaseIndicator :
        BaseIndicator<GartleySetupFinder, GartleySignalEventArgs>
    {
        private GartleySetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private Color m_SlColor;
        private Color m_TpColor;
        private Color m_BearColorFill;
        private Color m_BullColorFill;
        private Color m_BearColorBorder;
        private Color m_BullColorBorder;
        private const int SETUP_WIDTH = 3;
        private const int LINE_WIDTH = 1;
        private const int DIV_LINE_WIDTH = 3;
        private const int TREND_RATIO = 1;

        /// <summary>
        /// Gets or sets the value how deep should we analyze the candles.
        /// </summary>
        [Parameter(nameof(BarDepthCount), DefaultValue = Helper.GARTLEY_BARS_COUNT, MinValue = 10, MaxValue = 1000)]
        public int BarDepthCount { get; set; }

        /// <summary>
        /// Gets or sets the percent of the allowance for the relations calculation.
        /// </summary>
        [Parameter(nameof(BarAllowancePercent), DefaultValue = Helper.GARTLEY_CANDLE_ALLOWANCE_PERCENT, MinValue = 1, MaxValue = 50)]
        public int BarAllowancePercent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.GARTLEY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseGartley), DefaultValue = true)]
        public bool UseGartley { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BUTTERFLY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseButterfly), DefaultValue = true)]
        public bool UseButterfly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        [Parameter(nameof(UseShark), DefaultValue = true)]
        public bool UseShark { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCrab), DefaultValue = true)]
        public bool UseCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseBat), DefaultValue = true)]
        public bool UseBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseAltBat), DefaultValue = true)]
        public bool UseAltBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCypher), DefaultValue = true)]
        public bool UseCypher { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseDeepCrab), DefaultValue = true)]
        public bool UseDeepCrab { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should hide ratio values on patterns.
        /// </summary>
        [Parameter(nameof(HideRatio), DefaultValue = true)]
        public bool HideRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should show divergences with the patterns.
        /// </summary>
        [Parameter(nameof(ShowDivergences), DefaultValue = true)]
        public bool ShowDivergences { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter(nameof(UseDivergences), DefaultValue = false)]
        public bool UseDivergences { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = true)]
        public bool UseTrendOnly { get; set; }
        
        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX)]
        public double BreakEvenRatio { get; set; }

        private HashSet<GartleyPatternType> GetPatternsType()
        {
            var res = new HashSet<GartleyPatternType>();
            if (UseGartley)
                res.Add(GartleyPatternType.GARTLEY);
            if (UseButterfly)
                res.Add(GartleyPatternType.BUTTERFLY);
            if (UseShark)
                res.Add(GartleyPatternType.SHARK);
            if (UseCrab)
                res.Add(GartleyPatternType.CRAB);
            if (UseBat)
                res.Add(GartleyPatternType.BAT);
            if (UseAltBat)
                res.Add(GartleyPatternType.ALT_BAT);
            if (UseCypher)
                res.Add(GartleyPatternType.CYPHER);
            if (UseDeepCrab)
                res.Add(GartleyPatternType.DEEP_CRAB);

            return res;
        }
        
        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            m_BearColorFill = Color.FromHex("#50F08080");
            m_BullColorFill = Color.FromHex("#5090EE90");
            m_BearColorBorder = Color.FromHex("#F0F08080");
            m_BullColorBorder = Color.FromHex("#F090EE90");

            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();

            MacdCrossOverIndicator macdCrossover = UseDivergences || ShowDivergences
                ? Indicators.GetIndicator<MacdCrossOverIndicator>(Bars, Helper.MACD_LONG_CYCLE, Helper.MACD_SHORT_CYCLE,
                    Helper.MACD_SIGNAL_PERIODS)
                : null;

            SuperTrendItem superTrendItem = null;
            if (UseTrendOnly)
                superTrendItem = SuperTrendItem.Create(TimeFrame, this, TREND_RATIO, m_BarsProvider);

            double? breakEvenRatio = null;
            if (BreakEvenRatio > 0)
                breakEvenRatio = BreakEvenRatio;

            m_SetupFinder = new GartleySetupFinder(m_BarsProvider, Symbol, BarAllowancePercent,
                BarDepthCount, UseDivergences, 0, superTrendItem, patternTypes, macdCrossover, breakEvenRatio);
            Subscribe(m_SetupFinder);
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, GartleySignalEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            int indexX = e.GartleyItem.ItemX.BarIndex;
            int indexA = e.GartleyItem.ItemA.BarIndex;
            int indexB = e.GartleyItem.ItemB.BarIndex;
            int indexC = e.GartleyItem.ItemC.BarIndex;
            int indexD = e.GartleyItem.ItemD.BarIndex;
            if (indexX == 0 || indexA == 0 || indexB == 0 || indexC == 0 || indexD == 0)
                return;

            string name = $"{levelIndex}{e.GartleyItem.GetHashCode()}";
            double valueX = e.GartleyItem.ItemX.Value;
            double valueA = e.GartleyItem.ItemA.Value;
            double valueB = e.GartleyItem.ItemB.Value;
            double valueC = e.GartleyItem.ItemC.Value;
            double valueD = e.GartleyItem.ItemD.Value;

            bool isBull = valueX < valueA;
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;
            
            ChartTriangle p1 = 
            Chart.DrawTriangle($"P1{name}", indexX, valueX, indexA, valueA, indexB, valueB, colorFill, 0);
            p1.IsFilled = true;

            ChartTriangle p2 = 
            Chart.DrawTriangle($"P2{name}", indexB, valueB, indexC, valueC, indexD, valueD, colorFill, 0);
            p2.IsFilled = true;

            string percent = HideRatio ? string.Empty : $" ({e.GartleyItem.AccuracyPercent}%)";
            string header =
                $"{(isBull ? "Bullish" : "Bearish")} {e.GartleyItem.PatternType.Format()}{percent}";

            string xdRatio = HideRatio
                ? string.Empty
                : $"{Environment.NewLine}{e.GartleyItem.XtoDActual.Ratio()} ({e.GartleyItem.XtoD.Ratio()})";
            Chart.DrawTrendLine($"XD{name}", indexX, valueX, indexD, valueD, colorBorder, LINE_WIDTH)
                .TextForLine(Chart, $"{header}{xdRatio}",
                    !isBull, indexX, indexD)
                .IsHidden = HideRatio;

            if (!HideRatio)
            {
                Chart.DrawText($"XText{name}", "X", indexX, valueX, colorBorder)
                    .ChartTextAlign(!isBull);
                Chart.DrawText($"AText{name}", "A", indexA, valueA, colorBorder)
                    .ChartTextAlign(isBull);
                Chart.DrawText($"BText{name}", "B", indexB, valueB, colorBorder)
                    .ChartTextAlign(!isBull);
                Chart.DrawText($"CText{name}", "C", indexC, valueC, colorBorder)
                    .ChartTextAlign(isBull);
                Chart.DrawText($"DText{name}", "D", indexD, valueD, colorBorder)
                    .ChartTextAlign(!isBull);

                ChartTrendLine xbLine =
                    Chart.DrawTrendLine($"XB{name}", indexX, valueX, indexB, valueB, colorBorder, LINE_WIDTH);
                string xbLevel = e.GartleyItem.XtoB > 0
                    ? $" ({e.GartleyItem.XtoB.Ratio()})"
                    : string.Empty;
                xbLine.TextForLine(Chart, $"{e.GartleyItem.XtoBActual.Ratio()}{xbLevel}", !true, indexX, indexB);


                ChartTrendLine bdLine = Chart.DrawTrendLine(
                    $"BD{name}", indexB, valueB, indexD, valueD, colorBorder, LINE_WIDTH);

                bdLine.TextForLine(Chart, $"{e.GartleyItem.BtoDActual.Ratio()} ({e.GartleyItem.BtoD.Ratio()})",
                    true, indexB, indexD);


                ChartTrendLine acLine =
                    Chart.DrawTrendLine($"AC{name}", indexA, valueA, indexC, valueC, colorBorder, LINE_WIDTH);

                acLine.TextForLine(Chart, $"{e.GartleyItem.AtoCActual.Ratio()} ({e.GartleyItem.AtoC.Ratio()})",
                    isBull, indexA, indexC);
            }

            double closeD = m_BarsProvider.GetClosePrice(indexD);

            Chart.DrawRectangle($"SL{name}", indexD, closeD, indexD + SETUP_WIDTH,
                    e.GartleyItem.StopLoss, m_SlColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"TP1{name}", indexD, closeD, indexD + SETUP_WIDTH,
                    e.GartleyItem.TakeProfit1, m_TpColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"TP2{name}", indexD, closeD, indexD + SETUP_WIDTH,
                    e.GartleyItem.TakeProfit2, m_TpColor, LINE_WIDTH)
                .SetFilled();

            BarPoint div = e.DivergenceStart;
            if (ShowDivergences && div is not null)
            {
                Chart.DrawTrendLine($"Div{name}", div.BarIndex, div.Value, indexD, valueD, colorBorder, DIV_LINE_WIDTH);
            }

            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }
    }
}
