using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace TradeKit.Core
{
    public class MacdBarProvider: CTraderBarsProvider
    {
        private readonly MacdHistogram m_Histogram;

        /// <summary>
        /// Initializes a new instance of the <see cref="MacdBarProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="histogram">The MACD histogram indicator.</param>
        /// <param name="symbolEntity">The symbol.</param>
        public MacdBarProvider(Bars bars, MacdHistogram histogram, Symbol symbolEntity)
            :base(bars,symbolEntity)
        {
            m_Histogram = histogram;
        }

        private double GetValue(int index)
        {
            return m_Histogram.Histogram[index];
        }

        public override double GetLowPrice(int index)
        {
           return GetValue(index);
        }

        public override double GetHighPrice(int index)
        {
            return GetValue(index);
        }

        public override double GetOpenPrice(int index)
        {
            return GetValue(index);
        }

        public override double GetClosePrice(int index)
        {
            return GetValue(index);
        }

        public override double GetMaxBodyPrice(int index)
        {
            return GetValue(index);
        }

        public override double GetMinBodyPrice(int index)
        {
            return GetValue(index);
        }
    }
}
