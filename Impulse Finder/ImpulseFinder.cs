using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinder : Indicator
    {
        [Output("EnterPrices", LineColor = "Transparent")]
        public IndicatorDataSeries EnterPrices { get; set; }

        [Output("TakeProfits", LineColor = "Transparent")]
        public IndicatorDataSeries TakeProfits { get; set; }

        [Output("StopLosses", LineColor = "Transparent")]
        public IndicatorDataSeries StopLosses { get; set; }

        /// <summary>
        /// Gets or sets the allowance to impulse recognition in percents.
        /// </summary>
        [Parameter("DeviationPercent", DefaultValue = Helper.DEVIATION_DEF, MinValue = Helper.DEVIATION_MIN, MaxValue = Helper.DEVIATION_MAX)]
        public double DeviationPercent { get; set; }

        /// <summary>
        /// Gets or sets the allowance for the correction harmony (2nd and 4th waves).
        /// </summary>
        [Parameter("DeviationPercentCorrection", DefaultValue = Helper.PERCENT_CORRECTION_DEF, MinValue = Helper.PERCENT_CORRECTION_MIN, MaxValue = Helper.PERCENT_CORRECTION_MAX)]
        public double DeviationPercentCorrection { get; set; }
        
        private string StartSetupLineChartName =>
            "StartSetupLine" + Bars.OpenTimes.Last(1);
        private string ImpulseLineName =>
            "ImpulseLine" + Bars.OpenTimes.Last(1);

        private string EndSetupLineChartName =>
            "EndSetupLine" + Bars.OpenTimes.Last(1);

        private string EnterChartName => "Enter" + Bars.OpenTimes.Last(1);

        private string StopChartName => "SL" + Bars.OpenTimes.Last(1);

        private string ProfitChartName => "TP" + Bars.OpenTimes.Last(1);

        private IBarsProvider m_BarsProvider;

        private const double MAJOR_TF_RATIO = 5;

        /// <summary>
        /// Gets the main setup finder
        /// </summary>
        public SetupFinder SetupFinder { get; private set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }

            TimeFrame majorTf =
                TimeFrameHelper.GetNextTimeFrame(TimeFrame, MAJOR_TF_RATIO);
            m_BarsMajor = MarketData.GetBars(majorTf);
            var majorBarsProvider = new CTraderBarsProvider(m_BarsMajor, MarketData);
            m_BarsProvider = new CTraderBarsProvider(Bars, MarketData);
            m_BarsProvider.LoadBars();
            majorBarsProvider.LoadBars();

            SetupFinder = new SetupFinder(
                DeviationPercent,
                DeviationPercentCorrection,
                m_BarsProvider,
                majorBarsProvider);
            SetupFinder.OnEnter += OnEnter;
            SetupFinder.OnStopLoss += OnStopLoss;
            SetupFinder.OnTakeProfit += OnTakeProfit;
        }

        protected override void OnDestroy()
        {
            SetupFinder.OnEnter -= OnEnter;
            SetupFinder.OnStopLoss -= OnStopLoss;
            SetupFinder.OnTakeProfit -= OnTakeProfit;
            base.OnDestroy();
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawIcon(StopChartName, ChartIconType.Star, levelIndex
                , e.Level.Price, Color.Red);
            Print($"SL hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawIcon(ProfitChartName, ChartIconType.Star, levelIndex, e.Level.Price, Color.Green);
            Print($"TP hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
        }

        private void OnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            int levelIndex = e.Level.Index;
            int tpIndex = e.TakeProfit.Index;
            int slIndex = e.StopLoss.Index;

            Chart.DrawTrendLine(StartSetupLineChartName, tpIndex, e.TakeProfit.Price, levelIndex, e.Level.Price, Color.Gray);
            Chart.DrawTrendLine(EndSetupLineChartName, slIndex, e.StopLoss.Price, levelIndex, e.Level.Price, Color.Gray);
            Chart.DrawIcon(EnterChartName, ChartIconType.Star, levelIndex, e.Level.Price, Color.White);
            if (e.Waves is { Length: > 0 })
            {
                Extremum start = e.Waves[0];
                Extremum[] rest = e.Waves[1..];
                for (var index = 0; index < rest.Length; index++)
                {
                    Extremum wave = rest[index];
                    int startIndex = m_BarsProvider.GetIndexByTime(start.OpenTime);
                    int endIndex = m_BarsProvider.GetIndexByTime(wave.OpenTime);
                    Chart.DrawTrendLine($"{ImpulseLineName}{index}", 
                        startIndex, start.Value, endIndex, wave.Value, Color.LightBlue);
                    start = wave;
                }
            }

            EnterPrices[levelIndex] = e.Level.Price;
            TakeProfits[levelIndex] = e.TakeProfit.Price;
            StopLosses[levelIndex] = e.StopLoss.Price;
            Print($"New setup found! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
        }

        private bool m_SavedFileTest = false;
        private Bars m_BarsMajor;


        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            try
            {
                SetupFinder.CheckSetup(index);
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debugger.Launch();
                Print(ex.Message);
            }

            if (!IsLastBar)
            {
                return;
            }

            if (m_SavedFileTest)
            {
                return;
            }

            m_SavedFileTest = true;
            
            Print($"History calculation is completed, index {index}");
            // Here we want to save the market data to the file.
            // The code below is for testing purposes only.
            //m_SavedFileTest = true;
            //var jsonBarKeeper = new JsonBarKeeper();
            //jsonBarKeeper.Save(m_BarsProviders, SymbolName);
        }
    }
}
