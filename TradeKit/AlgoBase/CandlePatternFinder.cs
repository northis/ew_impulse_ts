using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Finds candle-based patters
    /// </summary>
    public class CandlePatternFinder
    {
        private readonly IBarsProvider m_BarsProvider;
        private const double ONE_CANDLE_RATIO = 0.7;
        private const int MIN_BARS_INDEX = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="CandlePatternFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        public CandlePatternFinder(IBarsProvider barsProvider)
        {
            m_BarsProvider = barsProvider;
        }

        /// <summary>
        /// Gets the candle patterns for the specified index of the bar or empty list.
        /// </summary>
        /// <param name="barIndex">Index of the bar.</param>
        public List<CandlesResult> GetCandlePatterns(int barIndex)
        {
            var res = new List<CandlesResult>();
            if (barIndex < MIN_BARS_INDEX)
                return res;

            var candles = new Candle[MIN_BARS_INDEX + 1];
            for (int i = 0; i < candles.Length; i++)
                candles[i] = new Candle(m_BarsProvider.GetOpenPrice(i),
                    m_BarsProvider.GetHighPrice(i),
                    m_BarsProvider.GetLowPrice(i),
                    m_BarsProvider.GetClosePrice(i));

            if (IsOneCandlePattern(candles[^1],
                    (c, r) => c.O < c.C && c.O < c.H - r && c.C < c.H - r))
                res.Add(new CandlesResult(CandlePatternType.HAMMER, true, barIndex));

            if (IsOneCandlePattern(candles[^1],
                    (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r))
                res.Add(new CandlesResult(CandlePatternType.INVERTED_HAMMER, false, barIndex));

            if (IsOneCandlePattern(candles[^1],
                    (c, r) => c.O > c.C && c.O < c.H - r && c.C < c.H - r))
                res.Add(new CandlesResult(CandlePatternType.UP_REJECTION_PIN_BAR, true, barIndex));

            if (IsOneCandlePattern(candles[^1],
                    (c, r) => c.O < c.C && c.O > c.L + r && c.C > c.L + r))
                res.Add(new CandlesResult(CandlePatternType.DOWN_REJECTION_PIN_BAR, false, barIndex));
            


            return res;
        }

        /// <summary>
        /// Determines whether the candle is a candle pattern.
        /// </summary>
        /// <param name="c">The candle.</param>
        /// <param name="expression">The expression to calculate - candle record and ratio.</param>
        /// <returns>
        ///   <c>true</c> if it is; otherwise, <c>false</c>.
        /// </returns>
        private bool IsOneCandlePattern(Candle c, Func<Candle, double, bool> expression)
        {
            double range = c.H - c.L;
            if (range < 0)
                return false;
            double ratio = range * ONE_CANDLE_RATIO;
            bool isPattern = expression(c, ratio);
            return isPattern;
        }
    }
}
