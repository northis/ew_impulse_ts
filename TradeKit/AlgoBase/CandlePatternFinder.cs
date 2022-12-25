using System;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Finds candle-based patters
    /// </summary>
    public class CandlePatternFinder
    {
        private readonly IBarsProvider m_BarsProvider;
        private const double HAMMER_RATIO = 0.7;

        /// <summary>
        /// Initializes a new instance of the <see cref="CandlePatternFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        public CandlePatternFinder(IBarsProvider barsProvider)
        {
            m_BarsProvider = barsProvider;
        }

        /// <summary>
        /// Determines whether the candle of the specified index is a Hammer pattern.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>
        ///   <c>true</c> if the candle of the specified index is a Hammer pattern; otherwise, <c>false</c>.
        /// </returns>
        public bool IsHammer(int index)
        {
            bool isHammer = IsCandlePattern(index,
                (c, r) => c.O < c.C && c.O < c.H - r && c.C < c.H - r);
            return isHammer;
        }

        /// <summary>
        /// Determines whether the candle of the specified index is a Inverted Hammer pattern.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>
        ///   <c>true</c> if the candle of the specified index is a Inverted Hammer pattern; otherwise, <c>false</c>.
        /// </returns>
        public bool IsInvertedHammer(int index)
        {
            bool isInvertedHammer = IsCandlePattern(index,
                (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r);
            return isInvertedHammer;
        }

        /// <summary>
        /// Gets the candle from index passed.
        /// </summary>
        /// <param name="index">The index.</param>
        private Candle GetCandle(int index)
        {
            return new Candle(m_BarsProvider.GetOpenPrice(index),
                m_BarsProvider.GetHighPrice(index),
                m_BarsProvider.GetLowPrice(index),
                m_BarsProvider.GetClosePrice(index));
        }

        /// <summary>
        /// Determines whether the candle of the specified index is (a part) of a candle pattern.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="expression">The expression to calculate - candle record and ratio.</param>
        /// <returns>
        ///   <c>true</c> if it is; otherwise, <c>false</c>.
        /// </returns>
        private bool IsCandlePattern(int index, Func<Candle, double, bool> expression)
        {
            Candle c = GetCandle(index);
            double range = c.H - c.L;
            if (range < 0)
                return false;
            double ratio = range * HAMMER_RATIO;
            bool isPattern = expression(c, ratio);
            return isPattern;
        }
    }
}
