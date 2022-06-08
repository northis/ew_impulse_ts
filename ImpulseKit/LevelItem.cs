namespace TradeKit
{
    public class LevelItem
    {
        public LevelItem(double price, int index)
        {
            Price = price;
            Index = index;
        }

        public double Price { get; }
        public int Index { get; }
    }
}
