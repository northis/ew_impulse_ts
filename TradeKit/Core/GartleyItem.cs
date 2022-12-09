namespace TradeKit.Core
{
    public record GartleyItem(
        LevelItem ItemX,
        LevelItem ItemA,
        LevelItem ItemB,
        LevelItem ItemC,
        LevelItem ItemD,
        double StopLoss,
        double TakeProfit1,
        double TakeProfit2,
        double XtoD,
        double AtoC,
        double BtoD,
        double XtoB = 0);
}
