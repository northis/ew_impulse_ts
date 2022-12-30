using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using TradeKit.Core;
using TradeKit.EventArgs;
using Shape = Plotly.NET.LayoutObjects.Shape;
using Color = Plotly.NET.Color;

namespace TradeKit.Gartley
{
    public class GartleySignalerBaseBot : BaseRobot<GartleySetupFinder, GartleySignalEventArgs>
    {
        private const string BOT_NAME = "GartleySignalerRobot";
        private const string SVG_PATH_TEMPLATE = "M {0} L {1} L {2} L {3} L {4} L {2} L {0} Z";

        private Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private Color m_TpColor = Color.fromARGB(80, 0, 240, 0);
        private readonly Color m_BearColorFill = Color.fromARGB(80, 240, 128, 128);
        private readonly Color m_BullColorFill = Color.fromARGB(80, 128, 240, 128);
        private Color m_BearColorBorder = Color.fromARGB(240, 240, 128, 128);
        private Color m_BullColorBorder = Color.fromARGB(240, 128, 240, 128);

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

        private Shape GetLine(BarPoint bp1, BarPoint bp2, Color color)
        {
            //GenericChart.Figure.create(new FSharpList<Trace>())
            Shape line = Shape.init(StyleParam.ShapeType.Line.ToFSharp(),
                X0: bp1.OpenTime.ToFSharp(),
                Y0: bp1.Value.ToFSharp(),
                X1: bp2.OpenTime.ToFSharp(),
                Y1: bp2.Value.ToFSharp(),
                Fillcolor: color.ToFSharp());
            return line;
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
        /// <param name="lastOpenDateTime">The last open date time.</param>
        protected override GenericChart.GenericChart[] GetAdditionalChartLayers(
            GenericChart.GenericChart candlestickChart,
            GartleySignalEventArgs signalEventArgs, DateTime lastOpenDateTime)
        {
            GenericChart.GenericChart[] charts =
                base.GetAdditionalChartLayers(candlestickChart, signalEventArgs, lastOpenDateTime);

            GartleyItem gartley = signalEventArgs.GartleyItem;
            bool isBull = gartley.ItemX < gartley.ItemA;

            Color color = isBull ? m_BullColorFill : m_BearColorFill;
            Shape path = Shape.init(StyleParam.ShapeType.SvgPath.ToFSharp(),
                X0: gartley.ItemX.OpenTime.ToFSharp(),
                Y0: gartley.ItemX.Value.ToFSharp(),
                X1: gartley.ItemD.OpenTime.ToFSharp(),
                Y1: gartley.ItemD.Value.ToFSharp(),
                Path: SvgPathFromGartleyItem(gartley).ToFSharp(),
                Fillcolor: color.ToFSharp());

            //Shape bx = GetLine(gartley.ItemB, gartley.ItemX, color);
            //candlestickChart.WithShape(new[] {xa, ab, bx, bc, cd, db }, true.ToFSharp());
            candlestickChart.WithShape(path, true.ToFSharp());
            return charts;
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
