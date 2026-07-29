using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Harmonic
{
    /// <summary>
    /// Exposes the harmonic input parameters to a cTrader robot without duplicating any
    /// calculation logic.
    /// </summary>
    public abstract class HarmonicCTraderBaseRobot<T> :
        CTraderBaseRobot<T, HarmonicSetupFinder, HarmonicSignalEventArgs>
        where T : BaseAlgoRobot<HarmonicSetupFinder, HarmonicSignalEventArgs>
    {
        /// <summary>
        /// Joins the harmonic-specific parameters into the algorithm settings.
        /// </summary>
        protected HarmonicParams GetHarmonicParams()
        {
            return HarmonicParamsMapper.Create(new HarmonicInputs
            {
                UseGartley = UseGartley,
                UseBat = UseBat,
                UseButterfly = UseButterfly,
                UseCrab = UseCrab,
                UseShark = UseShark,
                UseCypher = UseCypher,
                BarDepthCount = BarDepthCount,
                MinPivotPeriod = MinPivotPeriod,
                MaxPivotPeriod = MaxPivotPeriod,
                DConfirmationBars = DConfirmationBars,
                FibErrorPercent = FibErrorPercent,
                LegAsymmetryPercent = LegAsymmetryPercent,
                MinimumScore = MinimumScore,
                UseSecondTarget = UseSecondTarget,
                TakeProfit1Mode = TakeProfit1Mode,
                TakeProfit1Ratio = TakeProfit1Ratio,
                TakeProfit2Mode = TakeProfit2Mode,
                TakeProfit2Ratio = TakeProfit2Ratio,
                TargetAnchor = TargetAnchor,
                StopMode = StopMode,
                StopPercent = StopPercent,
                MinimumRiskReward = MinimumRiskReward,
                MinPatternSizeBars = MinPatternSizeBars,
                UseDivergences = UseDivergences,
                UseTrendOnly = UseTrendOnly,
                UseCandlePatterns = UseCandlePatterns,
                UseRsi = UseRsi,
                BreakEvenRatio = BreakEvenRatio
            });
        }

        #region Input parameters

        /// <summary>Gets or sets a value indicating whether Gartley patterns are searched.</summary>
        [Parameter("Gartley", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseGartley { get; set; }

        /// <summary>Gets or sets a value indicating whether Bat patterns are searched.</summary>
        [Parameter("Bat", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseBat { get; set; }

        /// <summary>Gets or sets a value indicating whether Butterfly patterns are searched.</summary>
        [Parameter("Butterfly", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseButterfly { get; set; }

        /// <summary>Gets or sets a value indicating whether Crab patterns are searched.</summary>
        [Parameter("Crab", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseCrab { get; set; }

        /// <summary>Gets or sets a value indicating whether Shark patterns are searched.</summary>
        [Parameter("Shark", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseShark { get; set; }

        /// <summary>Gets or sets a value indicating whether Cypher patterns are searched.</summary>
        [Parameter("Cypher", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseCypher { get; set; }

        /// <summary>Gets or sets how many bars back the search may look.</summary>
        [Parameter(nameof(BarDepthCount), DefaultValue = 500, MinValue = 50, MaxValue = 5000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }

        /// <summary>Gets or sets the smallest pivot period.</summary>
        [Parameter(nameof(MinPivotPeriod), DefaultValue = 3, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MinPivotPeriod { get; set; }

        /// <summary>Gets or sets the largest pivot period.</summary>
        [Parameter(nameof(MaxPivotPeriod), DefaultValue = 20, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MaxPivotPeriod { get; set; }

        /// <summary>Gets or sets the trailing bars required to confirm the point D.</summary>
        [Parameter(nameof(DConfirmationBars), DefaultValue = 1, MinValue = 0, MaxValue = 20, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public int DConfirmationBars { get; set; }

        /// <summary>Gets or sets the allowed Fibonacci ratio error, in percent.</summary>
        [Parameter(nameof(FibErrorPercent), DefaultValue = 15, MinValue = 0, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double FibErrorPercent { get; set; }

        /// <summary>Gets or sets the allowed leg duration asymmetry, in percent.</summary>
        [Parameter(nameof(LegAsymmetryPercent), DefaultValue = 250, MinValue = 0, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME, Step = 10)]
        public double LegAsymmetryPercent { get; set; }

        /// <summary>Gets or sets the minimum total score, from 0 to 1.</summary>
        [Parameter(nameof(MinimumScore), DefaultValue = 0.9, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double MinimumScore { get; set; }

        /// <summary>Gets or sets a value indicating whether the second target is the working TP.</summary>
        [Parameter(nameof(UseSecondTarget), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseSecondTarget { get; set; }

        /// <summary>Gets or sets what the first target is measured against.</summary>
        [Parameter(nameof(TakeProfit1Mode), DefaultValue = HarmonicTargetMode.MODEL_DEFAULT, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetMode TakeProfit1Mode { get; set; }

        /// <summary>Gets or sets the ratio of the first target.</summary>
        [Parameter(nameof(TakeProfit1Ratio), DefaultValue = HarmonicFib.F618, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double TakeProfit1Ratio { get; set; }

        /// <summary>Gets or sets what the second target is measured against.</summary>
        [Parameter(nameof(TakeProfit2Mode), DefaultValue = HarmonicTargetMode.MODEL_DEFAULT, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetMode TakeProfit2Mode { get; set; }

        /// <summary>Gets or sets the ratio of the second target.</summary>
        [Parameter(nameof(TakeProfit2Ratio), DefaultValue = HarmonicFib.F1272, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double TakeProfit2Ratio { get; set; }

        /// <summary>Gets or sets the price the relative targets are projected from.</summary>
        [Parameter(nameof(TargetAnchor), DefaultValue = HarmonicTargetAnchor.POINT_D, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetAnchor TargetAnchor { get; set; }

        /// <summary>Gets or sets the stop loss mode.</summary>
        [Parameter(nameof(StopMode), DefaultValue = HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicStopMode StopMode { get; set; }

        /// <summary>Gets or sets the stop loss percent.</summary>
        [Parameter(nameof(StopPercent), DefaultValue = 75, MinValue = 0, MaxValue = 500, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double StopPercent { get; set; }

        /// <summary>Gets or sets the minimum risk/reward ratio.</summary>
        [Parameter(nameof(MinimumRiskReward), DefaultValue = 0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.1)]
        public double MinimumRiskReward { get; set; }

        /// <summary>Gets or sets the minimum X-to-D duration, in bars.</summary>
        [Parameter(nameof(MinPatternSizeBars), DefaultValue = 0, MinValue = 0, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MinPatternSizeBars { get; set; }

        /// <summary>Gets or sets a value indicating whether a divergence is required.</summary>
        [Parameter(nameof(UseDivergences), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseDivergences { get; set; }

        /// <summary>Gets or sets a value indicating whether only trend-aligned patterns are used.</summary>
        [Parameter(nameof(UseTrendOnly), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseTrendOnly { get; set; }

        /// <summary>Gets or sets a value indicating whether a candle pattern is required.</summary>
        [Parameter(nameof(UseCandlePatterns), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseCandlePatterns { get; set; }

        /// <summary>Gets or sets a value indicating whether an RSI extreme is required.</summary>
        [Parameter(nameof(UseRsi), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseRsi { get; set; }

        /// <summary>Gets or sets the breakeven level. Use 0 to disable.</summary>
        [Parameter(nameof(BreakEvenRatio), DefaultValue = 0, MinValue = Helper.BREAKEVEN_MIN, MaxValue = Helper.BREAKEVEN_MAX, Group = Helper.TRADE_SETTINGS_NAME)]
        public double BreakEvenRatio { get; set; }

        #endregion
    }
}
