using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Gartley;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Gartley
{
    public abstract class GartleyCTraderBaseRobot<T> : 
        CTraderBaseRobot<T, GartleySetupFinder, GartleySignalEventArgs> 
        where T : BaseAlgoRobot<GartleySetupFinder, GartleySignalEventArgs>
    {
        /// <summary>
        /// Joins the Gartley-specific parameters into one record.
        /// </summary>
        protected GartleyParams GetGartleyParams()
        {
            return new GartleyParams(
                BarDepthCount, 
                Accuracy, 
                true, 
                true, 
                true, 
                true, 
                true,
                true, 
                true, 
                true, 
                UseDivergences,
                UseCandlePatterns,
                UseTrendOnly,
                MoreThanOnePatternToReact,
                BreakEvenRatio,
                MinPatternSizeBars,
                TakeProfitRatio,
                StopLossRatio,
                Period,
                BollingerPeriod,
                BollingerStandardDeviation);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the value how deep we should analyze the candles.
        /// </summary>
        [Parameter(nameof(BarDepthCount), DefaultValue = Helper.GARTLEY_BARS_COUNT, MinValue = 10, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }

        /// <summary>
        /// Gets or sets the final accuracy.
        /// </summary>
        [Parameter(nameof(Accuracy), DefaultValue = Helper.GARTLEY_ACCURACY, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double Accuracy { get; set; }
        
        /// <summary>
        /// Gets or sets the ratio used to calculate the take profit level in the Gartley pattern strategy.
        /// </summary>
        [Parameter("Take profit ratio", DefaultValue = Helper.GARTLEY_TP_RATIO, MinValue = 0.1, MaxValue = Helper.GARTLEY_TP2_RATIO, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double TakeProfitRatio { get; set; }

        /// <summary>
        /// Gets or sets the stop-loss ratio, which determines the proportional distance at which a stop-loss is set
        /// relative to the calculated pattern level. This value is configurable within a range and is used to
        /// manage risk during trades.
        /// </summary>
        [Parameter("Stop loss ratio", DefaultValue = Helper.GARTLEY_SL_RATIO, MinValue = 0.01, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double StopLossRatio { get; set; }
        
        /// <summary>
        /// Gets or sets the pivot (zigzag) period.
        /// </summary>
        [Parameter("Pivot period", DefaultValue = Helper.GARTLEY_MIN_PERIOD, MinValue = 1, MaxValue = 230, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets the period used for calculating the Bollinger Bands.
        /// </summary>
        [Parameter("Bollinger period", DefaultValue = 40, MinValue = 2, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME, Step = 5)]
        public int BollingerPeriod { get; set; }

        /// <summary>
        /// Gets or sets the standard deviation value used for Bollinger Bands calculation.
        /// </summary>
        [Parameter("Bollinger standard deviation", DefaultValue = 4, MinValue = 1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public int BollingerStandardDeviation { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter(nameof(UseDivergences), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseDivergences { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use candle patterns (Price Action).
        /// </summary>
        [Parameter(nameof(UseCandlePatterns), DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseCandlePatterns { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseTrendOnly { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use more than one pattern to react.
        /// </summary>
        [Parameter("Use second pattern conformation", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool MoreThanOnePatternToReact { get; set; }

        /// <summary>
        /// Gets or sets a breakeven level. Use 0 to disable
        /// </summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX, Group = Helper.TRADE_SETTINGS_NAME)]
        public double BreakEvenRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum pattern size in bars.
        /// </summary>
        [Parameter(nameof(MinPatternSizeBars), DefaultValue = 50, MinValue = 5, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MinPatternSizeBars { get; set; }
        #endregion
    }
}
