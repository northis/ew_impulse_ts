using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class ClosedPositionEventArgs : System.EventArgs
    {
        public ClosedPositionEventArgs(PositionClosedState state, IPosition position)
        {
            State = state;
            Position = position;
        }

        public PositionClosedState State { get; }
        public IPosition Position { get; }
    }
}
