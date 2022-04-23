using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="cAlgo.API.Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
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
        public int DeviationPercentCorrection { get; set; }

        /// <summary>
        /// Gets or sets the analyze depth - how many minor time frames should be taken into account. <seealso cref="TimeFrameHelper"/>
        /// </summary>
        [Parameter("AnalyzeDepth", DefaultValue = 2, MinValue = 1)]
        public int AnalyzeDepth { get; set; }

        private ExtremumFinder m_ExtremumFinder;
        private SetupFinder m_SetupFinder;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SetupFinder = new SetupFinder(null, null);
            m_SetupFinder.OnEnter += SetupFinderOnEnter;
            m_SetupFinder.OnStopLoss += SetupFinderOnStopLoss;
            m_SetupFinder.OnTakeProfit += SetupFinderOnTakeProfit;
            m_ExtremumFinder = new ExtremumFinder(DeviationPercentMajor);
        }

        protected override void OnDestroy()
        {
            m_SetupFinder.OnEnter -= SetupFinderOnEnter;
            m_SetupFinder.OnStopLoss -= SetupFinderOnStopLoss;
            m_SetupFinder.OnTakeProfit -= SetupFinderOnTakeProfit;
            base.OnDestroy();
        }

        private void SetupFinderOnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            Chart.DrawIcon(StopChartName, ChartIconType.Star, e.Level.Index
                , e.Level.Price, Color.Red);
        }

        private void SetupFinderOnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            Chart.DrawIcon(ProfitChartName, ChartIconType.Star, e.Level.Index, e.Level.Price, Color.Green);
        }

        private void SetupFinderOnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            Chart.DrawTrendLine(StartSetupLineChartName, e.TakeProfit.Index, e.TakeProfit.Price, e.Level.Index, e.Level.Price, Color.Gray);
            Chart.DrawTrendLine(EndSetupLineChartName, e.StopLoss.Index, e.StopLoss.Price, e.Level.Index, e.Level.Price, Color.Gray);
            Chart.DrawIcon(EnterChartName, ChartIconType.Star, e.Level.Index, e.Level.Price, Color.White);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            m_ExtremumFinder.Calculate(index, Bars);
            m_SetupFinder.CheckSetup(index);
        }

        private string StartSetupLineChartName => 
            "StartSetupLine" + Bars.OpenTimes.Last(1);

        private string EndSetupLineChartName => 
            "EndSetupLine" + Bars.OpenTimes.Last(1);

        private string EnterChartName => "Enter" + Bars.OpenTimes.Last(1);

        private string StopChartName => "SL" + Bars.OpenTimes.Last(1);

        private string ProfitChartName => "TP" + Bars.OpenTimes.Last(1);
    }
}
