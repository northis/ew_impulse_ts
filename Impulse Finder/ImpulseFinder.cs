using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="cAlgo.API.Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinder : Indicator
    {
        /// <summary>
        /// Gets or sets the allowance to impulse recognition in percents (major).
        /// </summary>
        [Parameter("DeviationPercentMajor", DefaultValue = 0.1, MinValue = 0.01)]
        public double DeviationPercentMajor { get; set; }

        /// <summary>
        /// Gets or sets the allowance to impulse recognition in percents (minor).
        /// </summary>
        [Parameter("DeviationPercentMinor", DefaultValue = 0.05, MinValue = 0.01)]
        public double DeviationPercentMinor { get; set; }

        /// <summary>
        /// Gets or sets the allowance for the correction harmony (2nd and 4th waves).
        /// </summary>
        [Parameter("DeviationPercentCorrection", DefaultValue = 250, MinValue = 1)]
        public double DeviationPercentCorrection { get; set; }

        /// <summary>
        /// Gets or sets the analyze depth - how many minor time frames should be taken into account. <seealso cref="TimeFrameHelper"/>
        /// </summary>
        [Parameter("AnalyzeDepth", DefaultValue = 1, MinValue = 0)]
        public int AnalyzeDepth { get; set; }

       /// <summary>
       /// Gets or sets the amount of bars that should be analyzed
       /// </summary>
        [Parameter("AnalyzeBarsCount", DefaultValue = 1000, MinValue = 10)]
        public int AnalyzeBarsCount { get; set; }

        private string StartSetupLineChartName =>
            "StartSetupLine" + Bars.OpenTimes.Last(1);

        private string EndSetupLineChartName =>
            "EndSetupLine" + Bars.OpenTimes.Last(1);

        private string EnterChartName => "Enter" + Bars.OpenTimes.Last(1);

        private string StopChartName => "SL" + Bars.OpenTimes.Last(1);

        private string ProfitChartName => "TP" + Bars.OpenTimes.Last(1);

        private SetupFinder m_SetupFinder;
        private List<IBarsProvider> m_BarsProviders;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_BarsProviders =
                BarsProviderFactory.CreateCTraderBarsProviders(AnalyzeBarsCount, TimeFrame, AnalyzeDepth, MarketData,
                    Bars);
            m_SetupFinder = new SetupFinder(
                DeviationPercentMajor,
                DeviationPercentMinor,
                DeviationPercentCorrection,
                AnalyzeDepth,
                m_BarsProviders);
            m_SetupFinder.OnEnter += OnEnter;
            m_SetupFinder.OnStopLoss += OnStopLoss;
            m_SetupFinder.OnTakeProfit += OnTakeProfit;
        }

        protected override void OnDestroy()
        {
            m_SetupFinder.OnEnter -= OnEnter;
            m_SetupFinder.OnStopLoss -= OnStopLoss;
            m_SetupFinder.OnTakeProfit -= OnTakeProfit;
            base.OnDestroy();
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            Chart.DrawIcon(StopChartName, ChartIconType.Star, e.Level.Index
                , e.Level.Price, Color.Red);
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            Chart.DrawIcon(ProfitChartName, ChartIconType.Star, e.Level.Index, e.Level.Price, Color.Green);
        }

        private void OnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            Chart.DrawTrendLine(StartSetupLineChartName, e.TakeProfit.Index, e.TakeProfit.Price, e.Level.Index, e.Level.Price, Color.Gray);
            Chart.DrawTrendLine(EndSetupLineChartName, e.StopLoss.Index, e.StopLoss.Price, e.Level.Index, e.Level.Price, Color.Gray);
            Chart.DrawIcon(EnterChartName, ChartIconType.Star, e.Level.Index, e.Level.Price, Color.White);
        }

        private bool m_SavedFileTest = false;

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            if (Bars.Count - index > AnalyzeBarsCount)
            {
                // We won't analyze more bars
                return;
            }

            m_SetupFinder.CheckSetup(index);
            if (!IsLastBar)
            {
                return;
            }

            if (m_SavedFileTest)
            {
                return;
            }

            // Here we want to save the market data to the file.
            // The code below is for testing purposes only.
            m_SavedFileTest = true;
            var jsonBarKeeper = new JsonBarKeeper();
            jsonBarKeeper.Save(m_BarsProviders, SymbolName);
        }
    }
}
