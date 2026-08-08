namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Diagonal-specific params on top of <see cref="EWParams"/> (see DIAGONAL.md §8).
    /// </summary>
    public record DiagonalParams(
        double TakeProfitRatio,
        DiagonalTakeProfitMode TakeProfitMode,
        double MinConvergence,
        bool RequireInsideWedge,
        double MaxSpillAreaRatio,
        bool RequireWave5Ratio,
        bool RequireWave4Ratio,
        bool RequireInitialMovement)
    {
    }
}
