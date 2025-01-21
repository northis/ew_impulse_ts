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
                StartPeriod, EndPeriod, SmoothDegree, MinSizePercent, MaxOverlapsePercent, BarsCount);
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
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets the smooth degree of the impulse.
        /// </summary>
        [Parameter(nameof(SmoothDegree), DefaultValue = Helper.IMPULSE_MAX_SMOOTH_DEGREE, MinValue = 0.01, MaxValue = 0.4, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double SmoothDegree { get; set; }
        #endregion
    }
}
