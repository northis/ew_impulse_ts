namespace TradeKit.PriceAction
{
    public record CandlePatternSettings(
        bool IsBull, int StopLossBarIndex, short BarsCount, int? LimitPriceBarIndex = null);
}
