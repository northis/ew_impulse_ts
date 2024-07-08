using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    public class PriceSpeedChecker
    {
        private readonly int m_Period;
        private readonly IBarsProvider m_BarsProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="PriceSpeedChecker"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="period">The period.</param>
        public PriceSpeedChecker(IBarsProvider barsProvider, int period)
        {
            m_Period = period;
            m_BarsProvider = barsProvider;
            Values = new SortedDictionary<int, BarPoint>(new DescendingComparer<int>());
        }

        /// <summary>
        /// Gets the speed.
        /// </summary>
        public double Speed { get; private set; }

        /// <summary>
        /// Gets the collection of values.
        /// Descending order is used!
        /// </summary>
        public SortedDictionary<int, BarPoint> Values { get; }

        /// <summary>
        /// Calculates the specified index price speed.
        /// </summary>
        /// <param name="indexBar">The index.</param>
        /// <param name="currentPrice">The current price.</param>
        public void Calculate(int indexBar, double? currentPrice = null)
        {
            currentPrice ??= m_BarsProvider.GetClosePrice(indexBar);

            int barsAgo = Math.Min(indexBar, m_Period);

            double agoPrice = m_BarsProvider.GetClosePrice(indexBar - barsAgo);
            double speed = 100 * (currentPrice.Value - agoPrice) / agoPrice;
            Values[indexBar] = new BarPoint(speed, indexBar, m_BarsProvider);
            Speed = speed;
        }

        class DescendingComparer<T> : IComparer<T> where T : IComparable<T>
        {
            public int Compare(T x, T y)
            {
                if (y == null)
                {
                    return 0;
                }

                return y.CompareTo(x);
            }
        }
    }
}