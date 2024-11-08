using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class ClosedPositionEventArgs : OpenedPositionEventArgs
    {
        public ClosedPositionEventArgs(PositionClosedState state, IPosition position):base(position)
        {
            State = state;
        }

        public PositionClosedState State { get; }
    }
}
