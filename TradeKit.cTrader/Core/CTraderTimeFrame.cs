using cAlgo.API;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    internal class CTraderTimeFrame : TimeFrameBase
    {
        public TimeFrame CTimeFrame { get; }

        public CTraderTimeFrame(TimeFrame tf) : base(tf.Name, tf.ShortName)
        {
            CTimeFrame = tf;
        }

        protected bool Equals(CTraderTimeFrame other)
        {
            return Equals(CTimeFrame, other.CTimeFrame);
        }

        public override int GetHashCode()
        {
            return (CTimeFrame != null ? CTimeFrame.GetHashCode() : 0);
        }
    }
}
