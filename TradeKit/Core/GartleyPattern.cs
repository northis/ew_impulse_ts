namespace TradeKit.Core
{
    public record GartleyPattern(
        double[] XBValues,
        double[] XDValues,
        double[] BDValues,
        double[] ACValues,
        GartleySetupType SetupType);
}
