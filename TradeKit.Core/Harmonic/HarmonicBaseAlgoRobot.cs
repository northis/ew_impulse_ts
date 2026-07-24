using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using Shape = Plotly.NET.LayoutObjects.Shape;
using Color = Plotly.NET.Color;
using Line = Plotly.NET.Line;

namespace TradeKit.Core.Harmonic
{
    /// <summary>
    /// Base robot logic for the harmonic XABCD setups: chart rendering and result export.
    /// </summary>
    public abstract class HarmonicBaseAlgoRobot : BaseAlgoRobot<HarmonicSetupFinder, HarmonicSignalEventArgs>
    {
        private const string BOT_NAME = "HarmonicSignalerRobot";
        private const string SVG_PATH_TEMPLATE = "M {0} L {1} L {2} L {3} L {4} L {2} L {0} Z";
        private const int LINE_WIDTH = 3;

        private readonly bool m_ShowPattern;
        private readonly string m_PathToSave =
            $"{BOT_NAME}-{DateTime.UtcNow:s}.csv".Replace(":", "_");

        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);
        private readonly Color m_BearColorFill = Color.fromARGB(80, 240, 128, 128);
        private readonly Color m_BullColorFill = Color.fromARGB(80, 128, 240, 128);
        private readonly Color m_BearColorBorder = Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColorBorder = Color.fromARGB(240, 128, 240, 128);

        /// <summary>
        /// Initializes a new instance of the <see cref="HarmonicBaseAlgoRobot"/> class.
        /// </summary>
        /// <param name="tradeManager">The trade manager.</param>
        /// <param name="storageManager">The storage manager.</param>
        /// <param name="robotParams">The common robot parameters.</param>
        /// <param name="harmonicParams">The harmonic search and setup settings.</param>
        /// <param name="isBackTesting">Back-testing flag.</param>
        /// <param name="symbolName">The symbol name.</param>
        /// <param name="timeFrameName">The time frame name.</param>
        /// <param name="showPattern">Draw the whole XABCD figure, not only the setup levels.</param>
        protected HarmonicBaseAlgoRobot(
            ITradeManager tradeManager,
            IStorageManager storageManager,
            RobotParams robotParams,
            HarmonicParams harmonicParams,
            bool isBackTesting,
            string symbolName,
            string timeFrameName,
            bool showPattern = true)
            : base(tradeManager, storageManager, robotParams, isBackTesting, symbolName,
                timeFrameName, false, false)
        {
            m_ShowPattern = showPattern;
            HarmonicParams = harmonicParams;
        }

        /// <summary>
        /// Gets the harmonic search and setup settings.
        /// </summary>
        protected HarmonicParams HarmonicParams { get; }

        /// <inheritdoc/>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <inheritdoc/>
        protected override DateTime GetStartViewDate(HarmonicSignalEventArgs signalEventArgs)
        {
            return m_ShowPattern
                ? base.GetStartViewDate(signalEventArgs)
                : signalEventArgs.HarmonicItem.ItemD.OpenTime;
        }

        /// <inheritdoc/>
        protected override void OnDrawChart(
            GenericChart candlestickChart,
            HarmonicSignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            List<DateTime> chartDateTimes)
        {
            HarmonicItem item = signalEventArgs.HarmonicItem;
            bool isBull = item.IsBull;
            double levelStart = signalEventArgs.Level.Value;

            GetSetupEndRender(item.ItemD.OpenTime, barProvider.TimeFrame,
                out DateTime setupStart, out DateTime setupEnd);

            candlestickChart.WithShape(GetSetupRectangle(
                setupStart, setupEnd, m_TpColor, levelStart, signalEventArgs.TakeProfit.Value));
            candlestickChart.WithShape(GetSetupRectangle(
                setupStart, setupEnd, m_SlColor, levelStart, signalEventArgs.StopLoss.Value));

            if (!m_ShowPattern)
                return;

            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            candlestickChart.WithShape(Shape.init(
                ShapeType: StyleParam.ShapeType.SvgPath,
                X0: item.ItemX.OpenTime.ToFSharp(),
                Y0: item.ItemX.Value.ToFSharp(),
                X1: item.ItemD.OpenTime.ToFSharp(),
                Y1: item.ItemD.Value.ToFSharp(),
                Path: string.Format(SVG_PATH_TEMPLATE,
                    item.ItemX.ToSvgPoint(), item.ItemA.ToSvgPoint(), item.ItemB.ToSvgPoint(),
                    item.ItemC.ToSvgPoint(), item.ItemD.ToSvgPoint()),
                FillColor: colorFill,
                Line: Line.init(Color: colorFill)));

            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;

            AddLine(item.ItemA, item.ItemC, item.Score.BcToAbRatio);
            AddLine(item.ItemX, item.ItemB, item.Score.AbToXaRatio);
            if (item.Score.FinalRatio.HasValue)
                AddLine(item.ItemX, item.ItemD, item.Score.FinalRatio.Value);

            double patternBottom = isBull
                ? Math.Min(item.ItemX.Value, item.ItemD.Value)
                : Math.Min(item.ItemA.Value, item.ItemC.Value);

            candlestickChart.WithAnnotation(ChartGenerator.GetAnnotation(
                item.ItemD.OpenTime, patternBottom, ChartGenerator.BLACK_COLOR,
                CHART_FONT_HEADER, colorBorder,
                $"{item.PatternType} {item.Score.Total * 100:F1}"));

            void AddLine(BarPoint from, BarPoint to, double ratio)
            {
                candlestickChart.WithShape(GetLine(from, to, colorBorder, LINE_WIDTH));
                candlestickChart.WithAnnotation(GetAnnotation(
                    from, to, colorBorder, ratio.Ratio(), chartDateTimes));
            }
        }

        /// <inheritdoc/>
        protected override void OnResultForManualAnalysis(
            HarmonicSignalEventArgs signalEventArgs,
            HarmonicSetupFinder sf,
            bool tradeResult)
        {
            AppendResult(signalEventArgs, tradeResult, sf.Symbol.Name, sf.TimeFrame.ShortName);
        }

        /// <inheritdoc/>
        protected override void OnSaveRawChartDataForManualAnalysis(
            ChartDataSource chartDataSource,
            HarmonicSignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            string dirPath,
            bool tradeResult,
            Rangebreak[] rangebreaks = null)
        {
            AppendResult(signalEventArgs, tradeResult,
                barProvider.BarSymbol.Name, barProvider.TimeFrame.ShortName);
        }

        /// <inheritdoc/>
        protected override bool HasSameSetupActive(
            HarmonicSetupFinder setupFinder, HarmonicSignalEventArgs signal)
        {
            return false;
        }

        private void AppendResult(
            HarmonicSignalEventArgs signalEventArgs, bool tradeResult,
            string symbolName, string timeFrameName)
        {
            HarmonicItem item = signalEventArgs.HarmonicItem;
            string csvFilePath = Path.Join(Helper.DirectoryToSaveResults, m_PathToSave);
            string resultToSave =
                $"{item.ItemD.OpenTime:s};{item.PatternType};{(item.IsBull ? "bull" : "bear")};" +
                $"{item.Score.AbToXaRatio:0.###};{item.Score.BcToAbRatio:0.###};" +
                $"{item.Score.CdToBcRatio:0.###};{item.Score.FinalRatio:0.###};" +
                $"{item.Score.Total:0.####};{signalEventArgs.RiskReward:0.###};" +
                $"{(tradeResult ? "+" : "-")};{symbolName};{timeFrameName}";

            File.AppendAllLines(csvFilePath, new[] { resultToSave });
        }
    }
}
