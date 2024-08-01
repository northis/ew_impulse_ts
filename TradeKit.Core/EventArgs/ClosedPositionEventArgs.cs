using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class ClosedPositionEventArgs : System.EventArgs
    {
        public ClosedPositionEventArgs(PositionClosedState state)
        {
            State = state;
        }

        public PositionClosedState State { get; }


    }
}
