using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Json;
using TradeKit.ML;
using static Plotly.NET.StyleParam;
using Color = Plotly.NET.Color;

namespace TradeKit.Impulse
{
    public class ImpulseSignalerBaseRobot : BaseRobot<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";
        
        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        protected override void OnDrawChart(GenericChart.GenericChart candlestickChart, ImpulseSignalEventArgs signalEventArgs, IBarsProvider barProvider,
            List<DateTime> chartDateTimes)
        {
            string[] waveNotations = PatternGeneration.PatternGenerator.ModelRules[ElliottModelType.IMPULSE].Models
                .Keys.ToArray();
            
            for (int i = 0; i < signalEventArgs.WavePoints.Length; i++)
            {
                string notation = waveNotations[i];
                BarPoint bp = signalEventArgs.WavePoints[i];
                var ann = ChartGenerator.GetAnnotation(bp.OpenTime, bp.Value, ChartGenerator.SEMI_WHITE_COLOR, 16,
                    Color.fromARGB(0, 0, 0, 0), notation);
                candlestickChart.WithAnnotation(ann);
            }
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

            bool useChannel = signalEventArgs.ChannelBarPoints.Length == 4;
            GenericChart.GenericChart[] result =
                new GenericChart.GenericChart[2 + (useChannel ? 2 : 0)];

            GenericChart.GenericChart tpLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, tp), new(lastOpenDateTime, tp) },
                LineColor: ChartGenerator.LONG_COLOR.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: DrawingStyle.Dash.ToFSharp());
            result[0] = tpLine;
            GenericChart.GenericChart slLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, sl), new(lastOpenDateTime, sl) },
                LineColor: ChartGenerator.SHORT_COLOR.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: DrawingStyle.Dash.ToFSharp());
            result[1] = slLine;

            if (useChannel)
            {
                BarPoint channelBottom1 = signalEventArgs.ChannelBarPoints[0];
                BarPoint channelBottom2 = signalEventArgs.ChannelBarPoints[1];
                
                result[2] = Chart2D.Chart.Line<DateTime, double, string>(
                    new Tuple<DateTime, double>[]
                    {
                        new(channelBottom1.OpenTime, channelBottom1.Value),
                        new(channelBottom2.OpenTime, channelBottom2.Value)
                    },
                    LineColor: ChartGenerator.WHITE_COLOR.ToFSharp(),
                    ShowLegend: false.ToFSharp(),
                    LineDash: DrawingStyle.Dot.ToFSharp());


                BarPoint channelTop1 = signalEventArgs.ChannelBarPoints[2];
                BarPoint channelTop2 = signalEventArgs.ChannelBarPoints[3];

                result[3] = Chart2D.Chart.Line<DateTime, double, string>(
                    new Tuple<DateTime, double>[]
                        {new(channelTop1.OpenTime, channelTop1.Value), new(channelTop2.OpenTime, channelTop2.Value)},
                    LineColor: ChartGenerator.WHITE_COLOR.ToFSharp(),
                    ShowLegend: false.ToFSharp(),
                    LineDash: DrawingStyle.Dot.ToFSharp());
            }

            return result;
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

        protected override void OnSaveRawChartDataForManualAnalysis(
            ChartDataSource chartDataSource, 
            ImpulseSignalEventArgs signalEventArgs,
            IBarsProvider barProvider,
            string dirPath,
            bool tradeResult,
            Rangebreak[] rangeBreaks = null)
        {
            int barsCount = chartDataSource.D.Length;
            if (!tradeResult || barsCount < Helper.ML_MIN_BARS_COUNT)
                return;

            var candlesForExport = new List<JsonCandleExport>();

            BarPoint startWave = signalEventArgs.Model.Wave0;
            BarPoint endWave = signalEventArgs.Model.Wave5;

            for (int i = 0; i < barsCount; i++)
            {
                DateTime date = chartDataSource.D[i];
                double high = chartDataSource.H[i];
                double low = chartDataSource.L[i];

                candlesForExport.Add(new JsonCandleExport
                {
                    O = chartDataSource.O[i],
                    C = chartDataSource.C[i],
                    H = high,
                    L = low,
                    OpenDate = date
                });
            }

            float[] vector = MachineLearning.GetModelVector(
                candlesForExport, startWave.Value, endWave.Value,
                Helper.ML_IMPULSE_VECTOR_RANK, Symbol.Digits).Item1;

            var saveToLog = new ModelInput
            {
                ClassType = (uint) ElliottModelType.IMPULSE, 
                Vector = vector
            };

            string csvFilePath = Path.Join(
                Helper.DirectoryToSaveImages, Helper.ML_CSV_STAT_FILE_NAME);
            using StreamWriter sw = new StreamWriter(csvFilePath, true);
            sw.WriteLine(saveToLog);
        }
    }
}