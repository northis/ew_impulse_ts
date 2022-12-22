namespace TradeKit.Gartley
{
    public record GartleyPattern(
        GartleyPatternType PatternType,
        double[] XBValues,
        double[] XDValues,
        double[] BDValues,
        double[] ACValues,
        GartleySetupType SetupType = GartleySetupType.AD);
}
