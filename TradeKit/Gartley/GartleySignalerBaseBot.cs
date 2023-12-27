using System;
using System.Collections.Generic;
using System.Diagnostics;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Indicators;
using Shape = Plotly.NET.LayoutObjects.Shape;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;

namespace TradeKit.Gartley
{
    public class GartleySignalerBaseBot : BaseRobot<GartleySetupFinder, GartleySignalEventArgs>
    {
        private const string BOT_NAME = "GartleySignalerRobot";
        private const string DIVERGENCE_NAME = "Div";
        private const string SVG_PATH_TEMPLATE = "M {0} L {1} L {2} L {3} L {4} L {2} L {0} Z";

        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);
        private const int LINE_WIDTH = 3;
        private readonly Color m_BearColorFill = Color.fromARGB(80, 240, 128, 128);
        private readonly Color m_BullColorFill = Color.fromARGB(80, 128, 240, 128);
        private readonly Color m_BearColorBorder = Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColorBorder = Color.fromARGB(240, 128, 240, 128);

        #region Input parameters

        /// <summary>
        /// Gets or sets a value indicating whether we should set the parameters automatically.
        /// </summary>
        [Parameter(nameof(UseAutoSettings), DefaultValue = true)]
        public bool UseAutoSettings { get; set; }

        ///// <summary>
        ///// Gets or sets a value indicating whether we should filter by flat part of the trend.
        ///// </summary>
        //[Parameter(nameof(UseFlatFilter), DefaultValue = true, Group = Helper.GARTLEY_SETTINGS_NAME)]
        //public bool UseFlatFilter { get; set; }

        /// <summary>
        /// Gets or sets the value how deep should we analyze the candles.
        /// </summary>
        [Parameter(nameof(BarDepthCount), DefaultValue = Helper.GARTLEY_BARS_COUNT, MinValue = 10, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }

        /// <summary>
        /// Gets or sets the percent of the allowance for the relations calculation.
        /// </summary>
        [Parameter(nameof(BarAllowancePercent), DefaultValue = Helper.GARTLEY_CANDLE_ALLOWANCE_PERCENT, MinValue = 1, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarAllowancePercent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.GARTLEY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseGartley), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseGartley { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BUTTERFLY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseButterfly), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseButterfly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        [Parameter(nameof(UseShark), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseShark { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCrab), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseBat), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseAltBat), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseAltBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCypher), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseCypher { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseDeepCrab), DefaultValue = true, Group = Helper.GARTLEY_GROUP_NAME)]
        public bool UseDeepCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter(nameof(UseDivergences), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseDivergences { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseTrendOnly { get; set; }

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX)]
        public double BreakEvenRatio { get; set; }
        #endregion

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
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        private string SvgPathFromGartleyItem(GartleyItem gartley)
        {
            string path = string.Format(SVG_PATH_TEMPLATE, 
                gartley.ItemX.ToSvgPoint(), 
                gartley.ItemA.ToSvgPoint(),
                gartley.ItemB.ToSvgPoint(), 
                gartley.ItemC.ToSvgPoint(), 
                gartley.ItemD.ToSvgPoint());
            return path;
        }

        /// <summary>
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="candlestickChart">The main chart with candles.</param>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="chartDateTimes">Date times for bars got from the broker.</param>
        protected override void OnDrawChart(
            GenericChart.GenericChart candlestickChart,
            GartleySignalEventArgs signalEventArgs, 
            IBarsProvider barProvider,
            List<DateTime> chartDateTimes)
        {
            GartleyItem gartley = signalEventArgs.GartleyItem;
            bool isBull = gartley.ItemX < gartley.ItemA;
            
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;
            Shape patternPath = Shape.init(StyleParam.ShapeType.SvgPath,
                X0: gartley.ItemX.OpenTime.ToFSharp(),
                Y0: gartley.ItemX.Value.ToFSharp(),
                X1: gartley.ItemD.OpenTime.ToFSharp(),
                Y1: gartley.ItemD.Value.ToFSharp(),
                Path: SvgPathFromGartleyItem(gartley), 
                Fillcolor: colorFill, 
                Line: Line.init(Color: colorFill));
            candlestickChart.WithShape(patternPath, true);
            
            double levelStart = barProvider.GetClosePrice(gartley.ItemD.BarIndex);
            GetSetupEndRender(gartley.ItemD.OpenTime, barProvider.TimeFrame, 
                out DateTime setupStart, out DateTime setupEnd);

            Shape tp1 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit1);
            candlestickChart.WithShape(tp1, true);
            Shape tp2 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit2);
            candlestickChart.WithShape(tp2, true);
            Shape sl = GetSetupRectangle(
                setupStart, setupEnd, m_SlColor, levelStart, gartley.StopLoss);
            candlestickChart.WithShape(sl, true);
            
            if (signalEventArgs.DivergenceStart is not null)
            {
                Shape div = GetLine(signalEventArgs.DivergenceStart, gartley.ItemD, ChartGenerator.WHITE_COLOR, LINE_WIDTH);
                candlestickChart.WithShape(div, true);
                candlestickChart.WithAnnotation(GetAnnotation(
                        signalEventArgs.DivergenceStart, gartley.ItemD, ChartGenerator.WHITE_COLOR, DIVERGENCE_NAME, chartDateTimes),
                    true);
            }

            void AddLine(BarPoint b1, BarPoint b2, double ratio)
            {
                Shape line = GetLine(b1, b2, colorBorder, LINE_WIDTH);
                candlestickChart.WithShape(line, true);

                candlestickChart.WithAnnotation(GetAnnotation(
                    b1, b2, colorBorder, ratio.Ratio(), chartDateTimes), true);
            }
            
            AddLine(gartley.ItemA, gartley.ItemC, gartley.AtoC);
            AddLine(gartley.ItemB, gartley.ItemD, gartley.BtoD);
            AddLine(gartley.ItemX, gartley.ItemD, gartley.XtoD);

            if (gartley.XtoB != 0)
                AddLine(gartley.ItemX, gartley.ItemB, gartley.XtoB);

            double patternBottom = isBull ? Math.Min(gartley.ItemX.Value, gartley.ItemD.Value):
                    Math.Min(gartley.ItemA.Value, gartley.ItemC.Value);

            candlestickChart.WithAnnotation(ChartGenerator.GetAnnotation(
                    gartley.ItemD.OpenTime, patternBottom, ChartGenerator.BLACK_COLOR, CHART_FONT_HEADER, colorBorder,
                    gartley.PatternType.Format()),
                true);
        }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override IBarsProvider GetBarsProvider(Bars bars, Symbol symbolEntity)
        {
            var cTraderBarsProvider = new CTraderBarsProvider(bars, symbolEntity);
            return cTraderBarsProvider;
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override GartleySetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            IBarsProvider cTraderBarsProvider = GetBarsProvider(bars, symbolEntity);
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();

            MacdCrossOverIndicator macdCrossover = Indicators.GetIndicator<MacdCrossOverIndicator>(bars, Helper.MACD_LONG_CYCLE, Helper.MACD_SHORT_CYCLE, Helper.MACD_SIGNAL_PERIODS);
            
            SuperTrendItem superTrendItem = null;
            if (UseTrendOnly || UseAutoSettings)
            {
                superTrendItem = SuperTrendItem.Create(bars.TimeFrame, this, symbolEntity.Name);
            }

            double? breakEvenRatio = null;
            if (BreakEvenRatio > 0)
                breakEvenRatio = BreakEvenRatio;

            var setupFinder = new GartleySetupFinder(
                cTraderBarsProvider, symbolEntity, 
                BarAllowancePercent, BarDepthCount, UseDivergences, UseAutoSettings,
                superTrendItem, patternTypes, macdCrossover, breakEvenRatio);

            return setupFinder;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="!:TK" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(GartleySetupFinder setupFinder, GartleySignalEventArgs signal)
        {
            return false;
        }
    }
}
