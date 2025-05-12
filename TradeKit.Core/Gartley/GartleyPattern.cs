namespace TradeKit.Core.Gartley
{
    public record GartleyPattern(
        GartleyPatternType PatternType,
        double[] XBValues,
        double[] XDValues,
        double[] BDValues,
        double[] ACValues,
        double[] CEValues = null,
        GartleySetupType SetupType = GartleySetupType.AD);
}
