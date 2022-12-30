using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class LevelEventArgs : System.EventArgs
    {
        public LevelEventArgs(BarPoint level, BarPoint fromLevel)
        {
            Level = level;
            FromLevel = fromLevel;
        }

        public BarPoint Level { get; }
        public BarPoint FromLevel { get; }
    }
}
