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
                Period, EnterRatio, TakeRatio, BreakEvenRatio, MaxZigzagPercent, MaxOverlapseLengthPercent,
                HeterogeneityMaxPercent, MinSizePercent, AreaPercent, BarsCount);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the minimum size of the impulse in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = 0.3, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the zigzag period.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// How deep we should go until enter.
        /// </summary>
        [Parameter(nameof(EnterRatio), DefaultValue = 0.35, MinValue = 0.1, MaxValue = 0.95, Group = Helper.TRADE_SETTINGS_NAME)]
        public double EnterRatio { get; set; }

        /// <summary>
        /// How far we should go until take profit.
        /// </summary>
        [Parameter(nameof(TakeRatio), DefaultValue = 1.6, MinValue = 0.8, MaxValue = 4.236, Group = Helper.TRADE_SETTINGS_NAME)]
        public double TakeRatio { get; set; }

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0.0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX, Group = Helper.TRADE_SETTINGS_NAME)]
        public double BreakEvenRatio { get; set; }

        /// <summary>
        /// Gets or sets the maximum percent of the zigzag degree (how far the pullbacks can go from the main movement, in percents of the total bars).
        /// </summary>
        [Parameter(nameof(MaxZigzagPercent), DefaultValue = Helper.MAX_ZIGZAG_DEGREE_PERCENT, MinValue = 1, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxZigzagPercent { get; set; }

        [Parameter(nameof(AreaPercent), DefaultValue = 20, MinValue = 0, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double AreaPercent { get; set; }

        /// <summary>
        /// Gets or sets the maximum length of the impulse in percent of the entire impulse.
        /// </summary>
        [Parameter(nameof(MaxOverlapseLengthPercent), DefaultValue = Helper.MAX_OVERLAPSE_LENGTH_PERCENT, MinValue = 0.01, MaxValue = 90, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxOverlapseLengthPercent { get; set; }

        /// <summary>
        /// Gets or sets the max value of not-smooth of the impulse.
        /// </summary>
        [Parameter(nameof(HeterogeneityMaxPercent), DefaultValue = Helper.IMPULSE_MAX_HETEROGENEITY_DEGREE_PERCENT, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double HeterogeneityMaxPercent { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 500, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }
        #endregion
    }
}
