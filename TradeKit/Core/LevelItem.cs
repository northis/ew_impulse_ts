using TradeKit.AlgoBase;

namespace TradeKit.Core
{
    public record LevelItem(double Price, int? Index = null)
    {
        public static LevelItem FromBarPoint(BarPoint bp)
        {
            return new LevelItem(bp.Value, bp.BarIndex);
        }
    };
}
