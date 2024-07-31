using cAlgo.API;
using TradeKit.Core.Common;

namespace TradeKit.Core
{
    internal class CTraderTimeFrame : TimeFrameBase
    {
        public TimeFrame CTimeFrame { get; }

        public CTraderTimeFrame(TimeFrame tf) : base(tf.Name, tf.ShortName)
        {
            CTimeFrame = tf;
        }
    }
}
