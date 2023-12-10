using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.EventArgs;
using static Plotly.NET.StyleParam;

namespace TradeKit.Impulse
{
    public class ImpulseSignalerBaseRobot : BaseRobot<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";
        private const string IMPULSE_SETTINGS = "⚡ImpulseSettings";
        protected const string CHART_FILE_NAME = "img.03";

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="lastOpenDateTime">The last open date time.</param>
        protected override GenericChart.GenericChart[] GetAdditionalChartLayers(
            ImpulseSignalEventArgs signalEventArgs, DateTime lastOpenDateTime)
        {
            double sl = signalEventArgs.StopLoss.Value;
            double tp = signalEventArgs.TakeProfit.Value;
            DateTime startView = signalEventArgs.StartViewBarTime;
            GenericChart.GenericChart tpLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, tp), new(lastOpenDateTime, tp) },
                LineColor: ShortColor.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: DrawingStyle.Dash.ToFSharp());
            GenericChart.GenericChart slLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, sl), new(lastOpenDateTime, sl) },
                LineColor: LongColor.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: DrawingStyle.Dash.ToFSharp());

            return new[] {tpLine, slLine};
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
        /// Creates the setup finder.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override ImpulseSetupFinder CreateSetupFinder(Bars bars, Symbol symbolEntity)
        {
            var barsProvider = GetBarsProvider(bars, symbolEntity);
            var barProvidersFactory = new BarProvidersFactory(Symbol, MarketData);
            var sf = new ImpulseSetupFinder(barsProvider, barProvidersFactory);
            return sf;
        }

        /// <summary>
        /// Determines whether <see cref="signal"/> and <see cref="setupFinder"/> can contain an overnight signal.
        /// </summary>
        /// <param name="signal">The signal.</param>
        /// <param name="setupFinder">The setup finder.</param>
        protected override bool IsOvernightTrade(
            ImpulseSignalEventArgs signal, ImpulseSetupFinder setupFinder)
        {
            IBarsProvider bp = setupFinder.BarsProvider; 
            DateTime setupStart = signal.StopLoss.OpenTime;
            DateTime setupEnd = signal.Level.OpenTime + TimeFrameHelper.TimeFrames[bp.TimeFrame].TimeSpan;
            Logger.Write(
                $"A risky signal, the setup contains a trade session change: {bp.Symbol}, {setupFinder.TimeFrame}, {setupStart:s}-{setupEnd:s}");

            return HasTradeBreakInside(setupStart, setupEnd, setupFinder.Symbol);
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="finder"></param>
        /// <param name="signal">The <see cref="SignalEventArgs" /> instance containing the event data.</param>
        /// <returns>
        ///   <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            ImpulseSetupFinder finder, ImpulseSignalEventArgs signal)
        {
            if (Math.Abs(finder.SetupStartPrice - signal.StopLoss.Value) < double.Epsilon &&
                Math.Abs(finder.SetupEndPrice - signal.TakeProfit.Value) < double.Epsilon)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the wave point.
        /// We want to match index/DT/value from one TF to another.
        /// </summary>
        /// <param name="bp">The bp (bigger TF).</param>
        /// <param name="index">The index (smaller TF).</param>
        /// <param name="date">The dt.</param>
        /// <param name="high">The high.</param>
        /// <param name="low">The low.</param>
        /// <returns>True, if the wave point has been found.</returns>
        private bool FindWavePoint(
            BarPoint bp, int index, DateTime date, double high, double low)
        {
            return date >= bp.OpenTime &&
                   index < 0 &&
                   (Math.Abs(high - bp.Value) < double.Epsilon ||
                    Math.Abs(low - bp.Value) < double.Epsilon);
        }

        protected override void OnSaveRawChartDataForManualAnalysis(
            ChartDataSource chartDataSource, 
            ImpulseSignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            string dirPath,
            bool tradeResult)
        {
            int barsCount = chartDataSource.D.Length;
            JsonCandleExport[] candlesForExport = new JsonCandleExport[barsCount];

            int startIndex = -1;
            int entryIndex = -1;
            int endIndex = -1;

            BarPoint startWave = signalEventArgs.Waves[0];
            BarPoint endWave = signalEventArgs.Waves[^1];
            BarPoint entry = signalEventArgs.Level;

            for (int i = 0; i < barsCount; i++)
            {
                int barIndex = chartDataSource.FirstValueBarIndex + i;
                DateTime date = chartDataSource.D[i];
                double high = chartDataSource.H[i];
                double low = chartDataSource.L[i];
                candlesForExport[i] = new JsonCandleExport
                {
                    Open = chartDataSource.O[i],
                    Close = chartDataSource.C[i],
                    BarIndex = barIndex,
                    H = high,
                    L = low,
                    OpenDate = date
                };

                if (FindWavePoint(startWave, startIndex, date, high, low)) startIndex = i;
                if (FindWavePoint(endWave, endIndex, date, high, low)) endIndex = i;
                if (FindWavePoint(entry, entryIndex, date, high, low)) entryIndex = i;
            }

            if (startIndex < 0 || endIndex < 0)
            {
                Logger.Write("Cannot extract impulse");
                return;
            }
            
            GenericChart.GenericChart candlestickChart = Chart2D.Chart.Candlestick
                <double, double, double, double, DateTime, string>(
                    chartDataSource.O[startIndex..endIndex],
                    chartDataSource.H[startIndex..endIndex],
                    chartDataSource.L[startIndex..endIndex],
                    chartDataSource.C[startIndex..endIndex],
                    chartDataSource.D[startIndex..endIndex],
                    IncreasingColor: LongColor.ToFSharp(),
                    DecreasingColor: ShortColor.ToFSharp(),
                    Name: barProvider.Symbol.Name,
            ShowLegend: false);

            GenericChart.GenericChart resultChart = Plotly.NET.Chart.Combine(
                    Array.Empty<GenericChart.GenericChart>().Concat(new[] { candlestickChart }))
                .WithXAxisRangeSlider(RangeSlider.init(Visible: false))
                .WithConfig(Config.init(
                    StaticPlot: true,
                    Responsive: false))
                .WithLayout(Layout.init<string>(
                    PlotBGColor: BlackColor,
                    PaperBGColor: BlackColor,
                    Font: Font.init(Color: WhiteColor)))
                .WithLayoutGrid(LayoutGrid.init(
                    Rows: 0,
                    Columns: 0,
                    XGap: 0d,
                    YGap: 0d))
                .WithXAxis(LinearAxis.init<DateTime, DateTime, DateTime, DateTime, DateTime, DateTime>(GridColor: SemiWhiteColor, ShowGrid: true))
                .WithYAxis(LinearAxis.init<DateTime, DateTime, DateTime, DateTime, DateTime, DateTime>(
                    GridColor: SemiWhiteColor, ShowGrid: true))
                .WithYAxisStyle(Side: Side.Right, title: null);

            string jpgFilePath = Path.Join(dirPath, CHART_FILE_NAME);
            resultChart.SavePNG(jpgFilePath, null, CHART_WIDTH, CHART_HEIGHT);

            var exportStat = new JsonSymbolStatExport
            {
                Symbol = barProvider.Symbol.Name,
                Entry = signalEventArgs.Level.Value,
                EntryIndex = entryIndex,
                Stop = signalEventArgs.StopLoss.Value,
                Take = signalEventArgs.TakeProfit.Value,
                StartIndex = startIndex,
                FinishIndex = endIndex,
                TimeFrame = barProvider.TimeFrame.ShortName,
                Result = tradeResult,
                Accuracy = barProvider.Symbol.Digits
            };

            string jsonFilePath = Path.Join(dirPath, Helper.JSON_STAT_FILE_NAME);
            string json = JsonConvert.SerializeObject(exportStat, Formatting.None);
            File.WriteAllText(jsonFilePath, json);

            var exportData = new JsonSymbolDataExport
            {
                Candles = candlesForExport
            };

            jsonFilePath = Path.Join(dirPath, Helper.JSON_DATA_FILE_NAME);
            json = JsonConvert.SerializeObject(exportData, Formatting.None);
            File.WriteAllText(jsonFilePath, json);

        }
    }
}