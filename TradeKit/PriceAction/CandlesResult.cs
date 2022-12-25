namespace TradeKit.PriceAction
{
    public record CandlesResult(CandlePatternType Type, bool IsBull, double StopLoss, int BarIndex);
}
