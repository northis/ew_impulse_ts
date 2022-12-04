namespace TradeKit.Core
{
    public record GartleyItem(
        LevelItem ItemX,
        LevelItem ItemA,
        LevelItem ItemB,
        LevelItem ItemC,
        LevelItem ItemD,
        LevelItem StopLoss,
        LevelItem TakeProfit1,
        LevelItem TakeProfit2,
        double XtoD,
        double AtoC,
        double BtoD,
        double XtoB = 0)
    {
      
    }
}
