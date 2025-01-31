using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Impulse
{
    public abstract class ImpulseCTraderBaseRobot<T> : 
        CTraderBaseRobot<T, ImpulseSetupFinder, ImpulseSignalEventArgs> 
        where T : BaseAlgoRobot<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected ImpulseParams GetImpulseParams()
        {
            return new ImpulseParams(
                StartPeriod, EndPeriod, HeterogeneityPercent, HeterogeneityMaxPercent, MinSizePercent, MaxOverlapsePercent, MaxOverlapseLengthPercent, BarsCount);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the start period.
        /// </summary>
        [Parameter(nameof(StartPeriod), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int StartPeriod { get; set; }

        /// <summary>
        /// Gets or sets the end period.
        /// </summary>
        [Parameter(nameof(EndPeriod), DefaultValue = Helper.MAX_IMPULSE_PERIOD, MinValue = 1, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int EndPeriod { get; set; }

        /// <summary>
        /// Gets or sets the minimum size of the impulse in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = Helper.MIN_SIZE_PERCENT, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the minimum size of the impulse in percent.
        /// </summary>
        [Parameter(nameof(MaxOverlapsePercent), DefaultValue = Helper.MIN_OVERLAPSE_PERCENT, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxOverlapsePercent { get; set; }

        /// <summary>
        /// Gets or sets the maximum length of the impulse in percent of the entire impulse.
        /// </summary>
        [Parameter(nameof(MaxOverlapseLengthPercent), DefaultValue = Helper.MAX_OVERLAPSE_LENGTH_PERCENT, MinValue = 0.01, MaxValue = 90, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxOverlapseLengthPercent { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets the degree of not-smooth of the impulse.
        /// </summary>
        [Parameter(nameof(HeterogeneityPercent), DefaultValue = Helper.IMPULSE_HETEROGENEITY_DEGREE_PERCENT, MinValue = 0.5, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.05)]
        public double HeterogeneityPercent { get; set; }

        /// <summary>
        /// Gets or sets the max value of not-smooth of the impulse.
        /// </summary>
        [Parameter(nameof(HeterogeneityMaxPercent), DefaultValue = Helper.IMPULSE_MAX_HETEROGENEITY_DEGREE_PERCENT, MinValue = 0.5, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.05)]
        public double HeterogeneityMaxPercent { get; set; }
        #endregion
    }
}
