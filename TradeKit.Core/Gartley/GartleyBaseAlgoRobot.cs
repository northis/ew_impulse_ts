﻿using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using Shape = Plotly.NET.LayoutObjects.Shape;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;

namespace TradeKit.Core.Gartley
{
    public abstract class GartleyBaseAlgoRobot : BaseAlgoRobot<GartleySetupFinder, GartleySignalEventArgs>
    {
        private readonly GartleyParams m_GartleyParams;
        private const string BOT_NAME = "GartleySignalerRobot";
        private const string DIVERGENCE_NAME = "Div";
        private const string SVG_PATH_TEMPLATE = "M {0} L {1} L {2} L {3} L {4} L {2} L {0} Z";

        private readonly string m_PathToSave = 
            $"{BOT_NAME}-{DateTime.UtcNow:s}.csv".Replace(":", "_");
        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);
        private const int LINE_WIDTH = 3;
        private readonly Color m_BearColorFill = Color.fromARGB(80, 240, 128, 128);
        private readonly Color m_BullColorFill = Color.fromARGB(80, 128, 240, 128);
        private readonly Color m_BearColorBorder = Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColorBorder = Color.fromARGB(240, 128, 240, 128);

        protected GartleyBaseAlgoRobot(ITradeManager tradeManager, RobotParams robotParams, GartleyParams gartleyParams, bool isBackTesting, string symbolName, string timeFrameName) : base(tradeManager, robotParams, isBackTesting, symbolName, timeFrameName)
        {
            m_GartleyParams = gartleyParams;
        }

        protected HashSet<GartleyPatternType> GetPatternsType()
        {
            var res = new HashSet<GartleyPatternType>();
            if (m_GartleyParams.UseGartley)
                res.Add(GartleyPatternType.GARTLEY);
            if (m_GartleyParams.UseButterfly)
                res.Add(GartleyPatternType.BUTTERFLY);
            if (m_GartleyParams.UseShark)
                res.Add(GartleyPatternType.SHARK);
            if (m_GartleyParams.UseCrab)
                res.Add(GartleyPatternType.CRAB);
            if (m_GartleyParams.UseBat)
                res.Add(GartleyPatternType.BAT);
            if (m_GartleyParams.UseAltBat)
                res.Add(GartleyPatternType.ALT_BAT);
            if (m_GartleyParams.UseCypher)
                res.Add(GartleyPatternType.CYPHER);
            if (m_GartleyParams.UseDeepCrab)
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
            candlestickChart.WithShape(patternPath);
            
            double levelStart = barProvider.GetClosePrice(gartley.ItemD.BarIndex);
            GetSetupEndRender(gartley.ItemD.OpenTime, barProvider.TimeFrame, 
                out DateTime setupStart, out DateTime setupEnd);

            Shape tp1 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit1);
            candlestickChart.WithShape(tp1);
            Shape tp2 = GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, gartley.TakeProfit2);
            candlestickChart.WithShape(tp2);
            Shape sl = GetSetupRectangle(
                setupStart, setupEnd, m_SlColor, levelStart, gartley.StopLoss);
            candlestickChart.WithShape(sl);
            
            if (signalEventArgs.DivergenceStart is not null)
            {
                Shape div = GetLine(signalEventArgs.DivergenceStart, gartley.ItemD, ChartGenerator.WHITE_COLOR, LINE_WIDTH);
                candlestickChart.WithShape(div);
                candlestickChart.WithAnnotation(GetAnnotation(
                        signalEventArgs.DivergenceStart, gartley.ItemD, ChartGenerator.WHITE_COLOR, DIVERGENCE_NAME, chartDateTimes));
            }

            void AddLine(BarPoint b1, BarPoint b2, double ratio)
            {
                Shape line = GetLine(b1, b2, colorBorder, LINE_WIDTH);
                candlestickChart.WithShape(line);

                candlestickChart.WithAnnotation(GetAnnotation(
                    b1, b2, colorBorder, ratio.Ratio(), chartDateTimes));
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
                    gartley.PatternType.Format()));
        }

        /// <summary>
        /// <inheritdoc />
        /// </summary>
        protected override void OnResultForManualAnalysis(
            GartleySignalEventArgs signalEventArgs,
            GartleySetupFinder sf,
            bool tradeResult)
        {
            string csvFilePath = Path.Join(Helper.DirectoryToSaveResults, m_PathToSave);
            GartleyItem g = signalEventArgs.GartleyItem;
            string resultToSave =
                $"{g.ItemD.OpenTime:s};{g.PatternType};{g.XtoB:0.###};{g.AtoC:0.###};{g.BtoD:0.###};{g.XtoD:0.###};{(tradeResult ? "+" : "-")};{g.AccuracyPercent};{sf.Symbol.Name};{sf.TimeFrame.ShortName}";

            File.AppendAllLines(csvFilePath, new[] { resultToSave });
        }

        /// <summary>
        /// <inheritdoc />
        /// </summary>
        protected override void OnSaveRawChartDataForManualAnalysis(
            ChartDataSource chartDataSource,
            GartleySignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            string dirPath,
            bool tradeResult,
            Rangebreak[] rangebreaks = null)
        {
            string csvFilePath = Path.Join(Helper.DirectoryToSaveResults, m_PathToSave);
            GartleyItem g = signalEventArgs.GartleyItem;
            string resultToSave =
                $"{g.ItemD.OpenTime:s};{g.PatternType};{g.XtoB:##.###};{g.AtoC:##.###};{g.BtoD:##.###};{g.XtoD:##.###};{(tradeResult ? "+" : "-")}";

            File.AppendAllLines(csvFilePath, new [] { resultToSave });
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
