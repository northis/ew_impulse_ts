using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Gartley;
using TradeKit.Core.Indicators;
using TradeKit.Core.PriceAction;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Gartley
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

        /// <summary>
        /// Gets or sets a value indicating whether we should show ratio values on patterns.
        /// </summary>
        [Parameter("Show ratio values", DefaultValue = false, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should show divergences with the patterns.
        /// </summary>
        [Parameter("Show divergences", DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowDivergences { get; set; }

        /// <summary>
        /// Gets or sets the value how deep should we analyze the candles.
        /// </summary>
        [Parameter("Bar depth count", DefaultValue = Helper.GARTLEY_BARS_COUNT, MinValue = 10, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }
        
        /// <summary>
        /// Gets or sets the final accuracy.
        /// </summary>
        [Parameter("Accuracy", DefaultValue = Helper.GARTLEY_ACCURACY, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double Accuracy { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.GARTLEY"/> pattern.
        /// </summary>
        [Parameter("Gartley", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseGartley { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BUTTERFLY"/> pattern.
        /// </summary>
        [Parameter("Butterfly", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseButterfly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        [Parameter("Shark", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseShark { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        [Parameter("Crab", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        [Parameter("Bat", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        [Parameter("Alternative Bat", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseAltBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        [Parameter("Cypher", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseCypher { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        [Parameter("Deep Crab", DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseDeepCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter("Use divergences", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseDivergences { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use candle patterns (Price Action) with the patterns.
        /// </summary>
        [Parameter("Use candle patterns", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseCandlePatterns { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter("Trend only patterns", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseTrendOnly { get; set; }

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

            AwesomeOscillatorFinder ao = UseDivergences || ShowDivergences
                ? new AwesomeOscillatorFinder(m_BarsProvider)
                : null;

            ZoneAlligatorFinder zoneAlligator = null;
            if (UseTrendOnly)
                zoneAlligator = new ZoneAlligatorFinder(m_BarsProvider);

            CandlePatternFinder cpf = UseCandlePatterns
                ? new CandlePatternFinder(m_BarsProvider, false,
                    new HashSet<CandlePatternType>(CandlePatternFinder.GetPatternsForFiltering()))
                : null;

            m_SetupFinder = new GartleySetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), Accuracy,
                BarDepthCount, UseDivergences, zoneAlligator, patternTypes, ao, cpf);
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

            string percent = ShowRatio ? $" ({e.GartleyItem.AccuracyPercent}%)" : string.Empty;
            string header =
                $"{(isBull ? "Bullish" : "Bearish")} {e.GartleyItem.PatternType.Format()}{percent}";

            string xdRatio = ShowRatio
                ? $"{Environment.NewLine}{e.GartleyItem.XtoDActual.Ratio()} ({e.GartleyItem.XtoD.Ratio()})"
                : string.Empty;
            Chart.DrawTrendLine($"XD{name}", indexX, valueX, indexD, valueD,
                    colorBorder, ShowRatio ? LINE_WIDTH : 0)
                .TextForLine(Chart, $"{header}{xdRatio}", !isBull, indexX, indexD);

            if (ShowRatio)
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

            if (ShowSetups)
            {
                double levelValue = e.Level.Value;

                Chart.DrawRectangle($"SL{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP1{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.GartleyItem.TakeProfit1, m_TpColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP2{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.GartleyItem.TakeProfit2, m_TpColor, LINE_WIDTH)
                    .SetFilled();
            }

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
