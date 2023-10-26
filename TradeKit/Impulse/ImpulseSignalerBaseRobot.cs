using System;
using cAlgo.API;
using cAlgo.API.Internals;
using Plotly.NET;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Impulse
{
    public class ImpulseSignalerBaseRobot : BaseRobot<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private const string BOT_NAME = "ImpulseSignalerRobot";
        private const string IMPULSE_SETTINGS = "⚡ImpulseSettings";
        private readonly Plotly.NET.Color m_ShortColor = Plotly.NET.Color.fromHex("#EF5350");
        private readonly Plotly.NET.Color m_LongColor = Plotly.NET.Color.fromHex("#26A69A");

        [Parameter(nameof(ProfileThresholdTimes), DefaultValue = Helper.IMPULSE_PROFILE_THRESHOLD_TIMES, Group = IMPULSE_SETTINGS, Step = 0.1, MinValue = 0.1, MaxValue = 1)]
        public double ProfileThresholdTimes { get; set; }

        [Parameter(nameof(ProfilePeaksDistanceTimes), DefaultValue = Helper.IMPULSE_PROFILE_PEAKS_DISTANCE_TIMES, Group = IMPULSE_SETTINGS, Step = 0.1, MinValue = 0.1, MaxValue = 1)]
        public double ProfilePeaksDistanceTimes { get; set; }

        [Parameter(nameof(ProfilePeaksDifferenceTimes), DefaultValue = Helper.IMPULSE_PROFILE_PEAKS_DIFFERENCE_TIMES, Group = IMPULSE_SETTINGS, Step = 0.1, MinValue = 1)]
        public double ProfilePeaksDifferenceTimes { get; set; }

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
                LineColor: m_LongColor.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: StyleParam.DrawingStyle.Dash.ToFSharp());
            GenericChart.GenericChart slLine = Chart2D.Chart.Line<DateTime, double, string>(
                new Tuple<DateTime, double>[] { new(startView, sl), new(lastOpenDateTime, sl) },
                LineColor: m_ShortColor.ToFSharp(),
                ShowLegend: false.ToFSharp(),
                LineDash: StyleParam.DrawingStyle.Dash.ToFSharp());

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
            var sf = new ImpulseSetupFinder(barsProvider, ProfileThresholdTimes, ProfilePeaksDistanceTimes, ProfilePeaksDifferenceTimes);
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
    }
}