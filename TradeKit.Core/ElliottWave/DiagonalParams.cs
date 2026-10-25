using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Diagonal-specific params on top of <see cref="EWParams"/> (see DIAGONAL.md §8).
    /// </summary>
    public record DiagonalParams(
        double TakeProfitRatio,
        DiagonalTakeProfitMode TakeProfitMode,
        double MinConvergence,
        double MaxConvergence,
        bool RequireInsideWedge,
        double MaxSpillAreaRatio,
        bool RequireWave5Ratio,
        bool RequireWave4Ratio,
        DiagonalInitialMode InitialMode,
        double MinWave3Penetration,
        double MaxWaveDurationRatio,
        DiagonalRetraceAction RetraceAction,
        double MinRiskRewardRatio,
        double Wave3RetraceRatio,
        double MinWave4Wave2Level,
        bool RequireWave4Shorter,
        bool RequireWave2Shorter,
        double MinWave2Retrace,
        double MaxWave2Retrace,
        double MaxWave5SpillRatio,
        double MinWave4Wave2DurationRatio,
        double MinWave3Wave1DurationRatio,
        double MinWave2Wave1DurationRatio,
        bool RequireThickWaveEnds,
        ThinMarketGuardParams ThinMarketGuard,
        double TakeProfitRetraceRatio = 0.236,
        double StopWave3Ratio = 1.0,
        double MinWave4RetraceW3 = 0.382,
        double MaxWave4RetraceW3 = 0,
        DiagonalTriggerMode TriggerMode = DiagonalTriggerMode.WAVE3_BREAK,
        DiagonalWave5SpillMode Wave5SpillMode = DiagonalWave5SpillMode.ALL,
        double Wave5SpillThreshold = 0.005,
        double MinSignalRiskRewardRatio = 0)
    {
    }
}
