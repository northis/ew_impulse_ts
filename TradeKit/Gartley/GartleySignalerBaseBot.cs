using System;
using System.Collections.Generic;
using System.Diagnostics;
using cAlgo.API;
using cAlgo.API.Internals;
using Microsoft.FSharp.Core;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.EventArgs;
using Shape = Plotly.NET.LayoutObjects.Shape;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;

namespace TradeKit.Gartley
{
    public class GartleySignalerBaseBot : BaseRobot<GartleySetupFinder, GartleySignalEventArgs>
    {
        private const string BOT_NAME = "GartleySignalerRobot";
        private const string SVG_PATH_TEMPLATE = "M {0} L {1} L {2} L {3} L {4} L {2} L {0} Z";

        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);
        private const int SETUP_MIN_WIDTH = 3;
        private const int LINE_WIDTH = 3;
        private readonly Color m_DivColor = Color.fromARGB(240, 240, 240, 240);
        private readonly Color m_BearColorFill = Color.fromARGB(80, 240, 128, 128);
        private readonly Color m_BullColorFill = Color.fromARGB(80, 128, 240, 128);
        private readonly Color m_BearColorBorder = Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColorBorder = Color.fromARGB(240, 128, 240, 128);

        #region Input parameters

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
        [Parameter(nameof(UseButterfly), DefaultValue = false)]
        public bool UseButterfly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        [Parameter(nameof(UseShark), DefaultValue = false)]
        public bool UseShark { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCrab), DefaultValue = false)]
        public bool UseCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseBat), DefaultValue = false)]
        public bool UseBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseAltBat), DefaultValue = false)]
        public bool UseAltBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCypher), DefaultValue = false)]
        public bool UseCypher { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseDeepCrab), DefaultValue = false)]
        public bool UseDeepCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter(nameof(UseDivergences), DefaultValue = false)]
        public bool UseDivergences { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should filter signals by accuracy.
        /// </summary>
        [Parameter(nameof(FilterByAccuracy), DefaultValue = 0, MaxValue = 100)]
        public int FilterByAccuracy { get; set; }

        /// <summary>
        /// Gets or sets MACD Crossover long cycle (bars).
        /// </summary>
        [Parameter(nameof(MACDLongCycle), DefaultValue = Helper.MACD_LONG_CYCLE)]
        public int MACDLongCycle { get; set; }

        /// <summary>
        /// Gets or sets MACD Crossover short cycle (bars).
        /// </summary>
        [Parameter(nameof(MACDShortCycle), DefaultValue = Helper.MACD_SHORT_CYCLE)]
        public int MACDShortCycle { get; set; }

        /// <summary>
        /// Gets or sets MACD Crossover signal periods (bars).
        /// </summary>
        [Parameter(nameof(MACDSignalPeriods), DefaultValue = Helper.MACD_SIGNAL_PERIODS)]
        public int MACDSignalPeriods { get; set; }

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

        private Shape GetLine(BarPoint bp1, BarPoint bp2, Color color, double width = 1)
        {
            Shape line = Shape.init(StyleParam.ShapeType.Line.ToFSharp(),
                X0: bp1.OpenTime.ToFSharp(),
                Y0: bp1.Value.ToFSharp(),
                X1: bp2.OpenTime.ToFSharp(),
                Y1: bp2.Value.ToFSharp(),
                Fillcolor: color.ToFSharp(),
                Line: Line.init(Color: color, Width: width.ToFSharp()));
            return line;
        }

        private Annotation GetAnnotation(BarPoint bp1, BarPoint bp2, Color color, string text)
        {
            DateTime x = bp1.OpenTime + (bp2.OpenTime - bp1.OpenTime) / 2;
            double y = bp1.Value + (bp2.Value - bp1.Value) / 2;

            FSharpOption<double> doubleDef = 1d.ToFSharp();
            FSharpOption<int> intDef = 1.ToFSharp();
            FSharpOption<bool> falseF = false.ToFSharp();
            Color transparent = Color.fromARGB(0, 0, 0, 0);
            Annotation annotation = Annotation.init(
                X: x.ToFSharp(),
                Y: y.ToFSharp(),
                ArrowColor: color,
                ArrowHead: StyleParam.ArrowHead.Square,
                ArrowSide: StyleParam.ArrowSide.None,
                ArrowSize: doubleDef,
                AX: doubleDef,
                AXRef: doubleDef,
                AY: doubleDef,
                AYRef: doubleDef,
                BGColor: transparent,
                BorderColor: color,
                BorderPad: intDef,
                BorderWidth: intDef,
                CaptureEvents: falseF,
                ClickToShow: StyleParam.ClickToShow.False,
                Font: Font.init(Size: 14),
                Height: 10,
                HoverLabel: Hoverlabel.init(),
                HoverText: text,
                Name: text,
                Opacity: 1d,
                ShowArrow: falseF,
                StandOff: intDef,
                StartArrowHead: intDef,
                StartArrowSize: doubleDef,
                StartStandOff: intDef,
                TemplateItemName: text,
                Text: text,
                TextAngle: doubleDef,
                VAlign: StyleParam.VerticalAlign.Middle,
                Visible: falseF,
                Width: 100,
                XAnchor: StyleParam.XAnchorPosition.Auto,
                XClick: doubleDef,
                XRef: doubleDef,
                XShift: intDef,
                YAnchor: StyleParam.YAnchorPosition.Auto,
                YClick: doubleDef,
                YRef: doubleDef,
                YShift: intDef,
                Align: StyleParam.AnnotationAlignment.Center.ToFSharp());
            return annotation;
        }

        private Shape GetSetupRectangle(
            DateTime setupStart, DateTime setupEnd, Color color, double levelStart, double levelEnd)
        {
            Shape shape = Shape.init(StyleParam.ShapeType.Rectangle.ToFSharp(),
                X0: setupStart.ToFSharp(),
                Y0: levelStart.ToFSharp(),
                X1: setupEnd.ToFSharp(),
                Y1: levelEnd.ToFSharp(),
                Fillcolor: color.ToFSharp(),
                Line: Line.init(Color: color));
            
            return shape;
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
        /// <param name="setupFinder">The setup finder.</param>
        protected override void OnDrawChart(
            GenericChart.GenericChart candlestickChart,
            GartleySignalEventArgs signalEventArgs, 
            GartleySetupFinder setupFinder)
        {
            GartleyItem gartley = signalEventArgs.GartleyItem;
            bool isBull = gartley.ItemX < gartley.ItemA;
            
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;
            Shape patternPath = Shape.init(StyleParam.ShapeType.SvgPath.ToFSharp(),
                X0: gartley.ItemX.OpenTime.ToFSharp(),
                Y0: gartley.ItemX.Value.ToFSharp(),
                X1: gartley.ItemD.OpenTime.ToFSharp(),
                Y1: gartley.ItemD.Value.ToFSharp(),
                Path: SvgPathFromGartleyItem(gartley).ToFSharp(), 
                Fillcolor: colorFill.ToFSharp(), 
                Line: Line.init(Color: colorFill));
            candlestickChart.WithShape(patternPath, true.ToFSharp());

            TimeSpan timeFramePeriod = TimeFrameHelper.TimeFrames[setupFinder.TimeFrame].TimeSpan;
            DateTime setupStart = gartley.ItemD.OpenTime.Add(timeFramePeriod);
            double levelStart = setupFinder.BarsProvider.GetClosePrice(gartley.ItemD.BarIndex);
            DateTime setupEnd = setupFinder.BarsProvider.GetLastBarOpenTime()
                .Add(timeFramePeriod * SETUP_MIN_WIDTH);

            Shape tp1 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit1);
            candlestickChart.WithShape(tp1, true.ToFSharp());
            Shape tp2 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit2);
            candlestickChart.WithShape(tp2, true.ToFSharp());
            Shape sl = GetSetupRectangle(
                setupStart, setupEnd, m_SlColor, levelStart, gartley.StopLoss);
            candlestickChart.WithShape(sl, true.ToFSharp());

            if (signalEventArgs.DivergenceStart is null)
                return;

            Shape div = GetLine(signalEventArgs.DivergenceStart, gartley.ItemD, m_DivColor, LINE_WIDTH);
            candlestickChart.WithShape(div, true.ToFSharp());

            Shape aC = GetLine(gartley.ItemA, gartley.ItemC, colorBorder, LINE_WIDTH);
            candlestickChart.WithShape(aC, true.ToFSharp());
            candlestickChart.WithAnnotation(GetAnnotation(gartley.ItemA, gartley.ItemC, colorBorder,
                gartley.AtoCActual.Ratio()), true.ToFSharp());

            if (gartley.XtoB != 0)
            {
                Shape xB = GetLine(gartley.ItemX, gartley.ItemB, colorBorder, LINE_WIDTH);
                candlestickChart.WithShape(xB, true.ToFSharp());
            }

            Shape bD = GetLine(gartley.ItemB, gartley.ItemD, colorBorder, LINE_WIDTH);
            candlestickChart.WithShape(bD, true.ToFSharp());

            Shape xD = GetLine(gartley.ItemX, gartley.ItemD, colorBorder, LINE_WIDTH);
            candlestickChart.WithShape(xD, true.ToFSharp());
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override GartleySetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            var cTraderBarsProvider = new CTraderBarsProvider(bars, symbolEntity);
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();

            MacdCrossOverIndicator macdCrossover = UseDivergences
                ? Indicators.GetIndicator<MacdCrossOverIndicator>(bars, MACDLongCycle, MACDShortCycle, MACDSignalPeriods)
                : null;

            var setupFinder = new GartleySetupFinder(
                cTraderBarsProvider, symbolEntity, BarAllowancePercent, BarDepthCount, UseDivergences, FilterByAccuracy,
                patternTypes, macdCrossover);

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
