using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Rate;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Rate
{
    public abstract class RateCTraderBaseRobot : CTraderBaseRobot
    {
        /// <summary>
        /// Joins the Rate strategy-specific parameters into one record.
        /// </summary>
        protected RateParams GetRateParams()
        {
            return new RateParams(MaxBarSpeed, MinBarSpeed, SpeedPercent, SpeedTpSlRatio, TradeVolume);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the maximum bar speed.
        /// </summary>
        [Parameter(nameof(MaxBarSpeed), DefaultValue = Helper.MAX_BAR_SPEED_DEFAULT)]
        public int MaxBarSpeed { get; set; }

        /// <summary>
        /// Gets or sets the minimum bar speed.
        /// </summary>
        [Parameter(nameof(MinBarSpeed), DefaultValue = Helper.MIN_BAR_SPEED_DEFAULT)]
        public int MinBarSpeed { get; set; }

        /// <summary>
        /// Gets or sets the speed percent.
        /// </summary>
        [Parameter(nameof(SpeedPercent), DefaultValue = Helper.TRIGGER_SPEED_PERCENT)]
        public double SpeedPercent { get; set; }

        /// <summary>
        /// Gets or sets the speed tp/sl ratio.
        /// </summary>
        [Parameter(nameof(SpeedTpSlRatio), DefaultValue = Helper.SPEED_TP_SL_RATIO)]
        public double SpeedTpSlRatio { get; set; }

        /// <summary>
        /// Gets or sets the trade volume.
        /// </summary>
        [Parameter(nameof(TradeVolume), MinValue = 0, MaxValue = 1000, DefaultValue = 0)]
        public int TradeVolume { get; set; }
        #endregion
    }
}
