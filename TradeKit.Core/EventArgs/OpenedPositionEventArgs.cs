using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class OpenedPositionEventArgs : System.EventArgs
    {
        public OpenedPositionEventArgs(IPosition position)
        {
            Position = position;
        }

        public IPosition Position { get; }
    }
}
