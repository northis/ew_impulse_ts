using Plotly.NET;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using static Plotly.NET.StyleParam;
using Color = Plotly.NET.Color;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Base algo robot for contracting-diagonal setups (see DIAGONAL.md). Unlike
    /// <see cref="ElliottWaveBaseAlgoRobot"/> it works with the plain
    /// <see cref="EventArgs.ElliottWaveSignalEventArgs"/>: the whole 0-1-2-3-4-5 skeleton
    /// is already in <c>WavePoints</c>, so no impulse-specific model is needed.
    /// </summary>
    public abstract class DiagonalBaseAlgoRobot
        : BaseAlgoRobot<DiagonalSetupFinder, EventArgs.ElliottWaveSignalEventArgs>
    {
        private const string BOT_NAME = "DiagonalRobot";

        private static readonly string[] WAVE_NOTATIONS = { "0", "1", "2", "3", "4", "5" };

        protected DiagonalBaseAlgoRobot(ITradeManager tradeManager, IStorageManager storageManager,
            RobotParams robotParams, bool isBackTesting, string symbolName, string timeFrameName)
            : base(tradeManager, storageManager, robotParams, isBackTesting, symbolName,
                timeFrameName, true, true)
        {
        }

        public override string GetBotName()
        {
            return BOT_NAME;
        }

        protected override void OnDrawChart(
            GenericChart candlestickChart,
            EventArgs.ElliottWaveSignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            List<DateTime> chartDateTimes)
        {
            for (int i = 0; i < signalEventArgs.WavePoints.Length; i++)
            {
                BarPoint bp = signalEventArgs.WavePoints[i];
                if (bp == null)
                    continue;

                string notation = i < WAVE_NOTATIONS.Length
                    ? WAVE_NOTATIONS[i]
                    : i.ToString();

                var ann = ChartGenerator.GetAnnotation(bp.OpenTime, bp.Value,
                    ChartGenerator.SEMI_WHITE_COLOR, 16, Color.fromARGB(0, 0, 0, 0), notation);
                candlestickChart.WithAnnotation(ann);
            }
        }

        /// <summary>
        /// Draws the TP/SL levels, the 0-1-2-3-4-5 zigzag and both trendlines of the wedge.
        /// </summary>
        protected override GenericChart[] GetAdditionalChartLayers(
            EventArgs.ElliottWaveSignalEventArgs signalEventArgs, DateTime lastOpenDateTime)
        {
            double sl = signalEventArgs.StopLoss.Value;
            double tp = signalEventArgs.TakeProfit.Value;
            DateTime startView = signalEventArgs.StartViewBarTime;

            var result = new List<GenericChart>
            {
                Chart2D.Chart.Line<DateTime, double, string>(
                    new Tuple<DateTime, double>[] { new(startView, tp), new(lastOpenDateTime, tp) },
                    LineColor: ChartGenerator.LONG_COLOR.ToFSharp(),
                    ShowLegend: false.ToFSharp(),
                    LineDash: DrawingStyle.Dash.ToFSharp()),
                Chart2D.Chart.Line<DateTime, double, string>(
                    new Tuple<DateTime, double>[] { new(startView, sl), new(lastOpenDateTime, sl) },
                    LineColor: ChartGenerator.SHORT_COLOR.ToFSharp(),
                    ShowLegend: false.ToFSharp(),
                    LineDash: DrawingStyle.Dash.ToFSharp())
            };

            BarPoint[] wp = signalEventArgs.WavePoints;
            for (int i = 1; i < wp.Length; i++)
            {
                if (wp[i - 1] == null || wp[i] == null)
                    continue;

                result.Add(Chart2D.Chart.Line<DateTime, double, string>(
                    new Tuple<DateTime, double>[]
                    {
                        new(wp[i - 1].OpenTime, wp[i - 1].Value),
                        new(wp[i].OpenTime, wp[i].Value)
                    },
                    LineColor: ChartGenerator.WHITE_COLOR.ToFSharp(),
                    ShowLegend: false.ToFSharp(),
                    LineDash: DrawingStyle.Dot.ToFSharp()));
            }

            if (wp.Length >= 5)
            {
                AddTrendLine(result, wp[1], wp[3]);
                AddTrendLine(result, wp[2], wp[4]);
            }

            return result.ToArray();
        }

        private static void AddTrendLine(List<GenericChart> target, BarPoint from, BarPoint to)
        {
            if (from == null || to == null)
                return;

            target.Add(Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[]
                {
                    new(from.OpenTime, from.Value), new(to.OpenTime, to.Value)
                },
                LineColor: ChartGenerator.SEMI_WHITE_COLOR.ToFSharp(),
                ShowLegend: false.ToFSharp()));
        }

        protected override bool IsOvernightTrade(
            EventArgs.ElliottWaveSignalEventArgs signal, DiagonalSetupFinder setupFinder)
        {
            IBarsProvider bp = setupFinder.BarsProvider;
            DateTime setupStart = signal.StopLoss.OpenTime;
            DateTime setupEnd = signal.Level.OpenTime +
                                TimeFrameHelper.TimeFrames[bp.TimeFrame.Name].TimeSpan;
            Logger.Write(
                $"A risky signal, the setup contains a trade session change: {bp.BarSymbol}, {setupFinder.TimeFrame}, {setupStart:s}-{setupEnd:s}");

            return HasTradeBreakInside(setupStart, setupEnd, setupFinder.Symbol);
        }

        protected override bool HasSameSetupActive(
            DiagonalSetupFinder setupFinder, EventArgs.ElliottWaveSignalEventArgs signal)
        {
            EventArgs.ElliottWaveSignalEventArgs current = setupFinder.CurrentSignalEventArgs;
            return current != null &&
                   Math.Abs(current.StopLoss.Value - signal.StopLoss.Value) < double.Epsilon &&
                   Math.Abs(current.TakeProfit.Value - signal.TakeProfit.Value) < double.Epsilon;
        }
    }
}
