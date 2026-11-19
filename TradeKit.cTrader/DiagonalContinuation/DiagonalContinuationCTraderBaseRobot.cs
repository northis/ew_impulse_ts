using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.DiagonalContinuation
{
    /// <summary>
    /// Base cTrader robot for the "diagonal as the entry impulse" continuation trade
    /// (DIAGONAL.md §9.43). The default parameters are the best h1 exact-corridor cell:
    /// V3 dynamic re-price, k=0.5, target P0 + 1.272D, stop P0, LEADING only.
    /// </summary>
    public abstract class DiagonalContinuationCTraderBaseRobot<T> :
        CTraderBaseRobot<T, DiagonalContinuationSetupFinder, ElliottWaveSignalEventArgs>
        where T : BaseAlgoRobot<DiagonalContinuationSetupFinder, ElliottWaveSignalEventArgs>
    {
        protected EWParams GetEWParams()
        {
            return new EWParams(Period, MinSizePercent, BarsCount);
        }

        /// <summary>
        /// The exact corridor used by the h1 research: W2/W1 >= 0.5 and W4/W3 &lt;= 0.382,
        /// with the beauty gates off (the same configuration as
        /// <c>DiagonalGeometryResearchTests.EXACT_CORRIDOR</c>).
        /// </summary>
        protected DiagonalParams GetDiagonalParams()
        {
            return new DiagonalParams(
                TakeProfitRatio: 1.0,
                TakeProfitMode: DiagonalTakeProfitMode.DIAGONAL_RETRACE,
                MinConvergence: 0,
                MaxConvergence: 0,
                RequireInsideWedge: false,
                MaxSpillAreaRatio: 1.0,
                RequireWave5Ratio: false,
                RequireWave4Ratio: false,
                InitialMode: InitialMode,
                MinWave3Penetration: 0.03,
                MaxWaveDurationRatio: 8.0,
                RetraceAction: DiagonalRetraceAction.NONE,
                MinRiskRewardRatio: 0,
                Wave3RetraceRatio: 0,
                MinWave4Wave2Level: 0,
                RequireWave4Shorter: true,
                RequireWave2Shorter: false,
                MinWave2Retrace: MinWave2Retrace,
                MaxWave2Retrace: 0,
                MaxWave5SpillRatio: 0,
                MinWave4Wave2DurationRatio: 0,
                MinWave3Wave1DurationRatio: 0,
                MinWave2Wave1DurationRatio: 0,
                RequireThickWaveEnds: false,
                ThinMarketGuard: null,
                TakeProfitRetraceRatio: 0.236,
                StopWave3Ratio: 1.0,
                MinWave4RetraceW3: 0,
                MaxWave4RetraceW3: MaxWave4RetraceW3,
                TriggerMode: DiagonalTriggerMode.WAVE3_BREAK,
                Wave5SpillMode: DiagonalWave5SpillMode.ALL,
                Wave5SpillThreshold: 0.005,
                MinSignalRiskRewardRatio: 0);
        }

        protected DiagonalContinuationParams GetContinuationParams()
        {
            return new DiagonalContinuationParams(
                Variant: DiagonalContinuationVariant.DYNAMIC_REPRICE,
                EnterRatio: EnterRatio,
                TargetKind: TargetKind,
                TargetRatio: TargetRatio,
                ConfirmRatio: 0.10,
                StopBuffer: StopBuffer,
                Ladder: false,
                LadderTp2Ratio: 1.618,
                MaxHoldBars: MaxHoldBars,
                MaxWaitBars: MaxWaitBars,
                CostRate: 0);
        }

        #region Input parameters

        [Parameter(nameof(MinSizePercent), DefaultValue = 0.1, MinValue = 0.01,
            MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        [Parameter(nameof(Period), DefaultValue = 0, MinValue = 0, MaxValue = 200,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        [Parameter(nameof(BarsCount), DefaultValue = 10, MinValue = 3, MaxValue = 200,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        [Parameter("Initial diagonal", DefaultValue = DiagonalInitialMode.LEADING,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalInitialMode InitialMode { get; set; }

        [Parameter("Min W2 retrace", DefaultValue = 0.5, MinValue = 0, MaxValue = 1,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave2Retrace { get; set; }

        [Parameter("Max W4/W3", DefaultValue = 0.382, MinValue = 0, MaxValue = 1,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave4RetraceW3 { get; set; }

        [Parameter("Entry retrace k", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 0.9,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public double EnterRatio { get; set; }

        [Parameter("Target anchor", DefaultValue = DiagonalContinuationTargetKind.DIAGONAL_START,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalContinuationTargetKind TargetKind { get; set; }

        [Parameter("TP x D", DefaultValue = 1.272, MinValue = 0.5, MaxValue = 3.0,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public double TargetRatio { get; set; }

        [Parameter("Stop buffer x D", DefaultValue = 0.0, MinValue = 0, MaxValue = 0.2,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public double StopBuffer { get; set; }

        [Parameter("Max wait bars", DefaultValue = 300, MinValue = 10, MaxValue = 2000,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public int MaxWaitBars { get; set; }

        [Parameter("Max hold bars", DefaultValue = 500, MinValue = 10, MaxValue = 5000,
            Group = Helper.TRADE_SETTINGS_NAME)]
        public int MaxHoldBars { get; set; }

        /// <summary>
        /// The continuation edge was found only on h1; m15 is negative. Keep this on unless
        /// you deliberately want to test another timeframe.
        /// </summary>
        [Parameter("H1 only", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireHour1 { get; set; }

        #endregion
    }
}
