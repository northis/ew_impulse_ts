using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;
using TradeKit.PriceAction;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Finds candle-based patters
    /// </summary>
    public class CandlePatternFinder
    {
        private readonly IBarsProvider m_BarsProvider;
        private readonly HashSet<CandlePatternType> m_Patterns;
        private const double ONE_CANDLE_RATIO = 0.7;
        private const int MIN_BARS_INDEX = 2;

        private readonly Dictionary<CandlePatternType, CandlePatternSettings> 
            m_PatternDirectionMap = new()
        {
            {CandlePatternType.HAMMER, new CandlePatternSettings(true, 0)},
            {CandlePatternType.INVERTED_HAMMER, new CandlePatternSettings(false, 0)},
            {CandlePatternType.UP_REJECTION_PIN_BAR, new CandlePatternSettings(true, 0)},
            {CandlePatternType.DOWN_REJECTION_PIN_BAR, new CandlePatternSettings(false, 0)},
            {CandlePatternType.UP_PIN_BAR, new CandlePatternSettings(true, 1)},
            {CandlePatternType.DOWN_PIN_BAR, new CandlePatternSettings(false, 1)},
            {CandlePatternType.UP_OUTER_BAR, new CandlePatternSettings(true, 0)},
            {CandlePatternType.DOWN_OUTER_BAR, new CandlePatternSettings(false, 0)},
            {CandlePatternType.UP_OUTER_BAR_BODIES, new CandlePatternSettings(true, -1)},
            {CandlePatternType.DOWN_OUTER_BAR_BODIES, new CandlePatternSettings(false, -1)},
            {CandlePatternType.UP_INNER_BAR, new CandlePatternSettings(true, 1)},
            {CandlePatternType.DOWN_INNER_BAR, new CandlePatternSettings(false, 1)},
            {CandlePatternType.UP_PPR, new CandlePatternSettings(true, 1)},
            {CandlePatternType.DOWN_PPR, new CandlePatternSettings(false, 1)}
        };

        private readonly Dictionary<CandlePatternType, Func<Candle[], bool>>
            m_PatternExpressionMap = new()
            {
                {
                    CandlePatternType.HAMMER, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O < c.C && c.O < c.H - r && c.C < c.H - r)
                },
                {
                    CandlePatternType.INVERTED_HAMMER, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r)
                },
                {
                    CandlePatternType.UP_REJECTION_PIN_BAR, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O > c.C && c.O < c.H - r && c.C < c.H - r)
                },
                {
                    CandlePatternType.DOWN_REJECTION_PIN_BAR, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r)
                },
                {
                    CandlePatternType.UP_PIN_BAR, c =>
                    {
                        double upperPart = c[^2].H - c[^2].Length / 3;
                        double secHalf = c[^2].H - c[^2].Length / 2;
                        bool isBullishPinBar = c[^2].BodyLow >= upperPart &&
                                               c[^3].L >= secHalf && c[^2].C <= c[^3].H &&
                                               c[^1].L >= secHalf && c[^1].C > c[^2].H;
                        return isBullishPinBar;
                    }
                },
                {
                    CandlePatternType.DOWN_PIN_BAR, c =>
                    {
                        double lowerPart = c[^2].L + c[^2].Length / 3;
                        double secHalf = c[^2].L + c[^2].Length / 2;
                        bool isBearishPinBar = c[^2].BodyHigh <= lowerPart &&
                                               c[^3].H <= secHalf && c[^2].C >= c[^3].L &&
                                               c[^1].H <= secHalf && c[^1].C < c[^2].L;
                        return isBearishPinBar;
                    }
                },
                {
                    CandlePatternType.UP_OUTER_BAR, c =>
                        c[^1].O < c[^1].C && 
                        c[^2].O > c[^1].C && 
                        c[^2].H < c[^1].H && 
                        c[^2].L > c[^1].L
                },
                {
                    CandlePatternType.DOWN_OUTER_BAR, c =>
                        c[^1].O > c[^1].C &&
                        c[^2].O < c[^1].C &&
                        c[^2].H < c[^1].H && 
                        c[^2].L > c[^1].L
                },
                {
                    CandlePatternType.UP_OUTER_BAR_BODIES, c =>
                        c[^1].O < c[^1].C &&
                        c[^2].BodyHigh <= c[^1].BodyHigh && 
                        c[^2].BodyLow >= c[^1].BodyLow
                },
                {
                    CandlePatternType.DOWN_OUTER_BAR_BODIES, c =>
                        c[^1].O > c[^1].C &&
                        c[^2].BodyHigh <= c[^1].BodyHigh && 
                        c[^2].BodyLow >= c[^1].BodyLow
                },
                {
                    CandlePatternType.UP_INNER_BAR, c =>
                        c[^1].O < c[^1].C &&
                        c[^2].H > c[^1].H &&
                        c[^2].L < c[^1].L &&
                        c[^2].C > c[^1].O &&
                        c[^2].O <= c[^1].O
                },
                {
                    CandlePatternType.DOWN_INNER_BAR, c =>
                        c[^1].O > c[^1].C &&
                        c[^2].H > c[^1].H &&
                        c[^2].L < c[^1].L &&
                        c[^2].C >= c[^1].O &&
                        c[^2].O < c[^1].O
                },
                {
                    CandlePatternType.UP_PPR, c =>
                        c[^3].O > c[^3].C &&
                        c[^2].O > c[^2].C &&
                        c[^3].H > c[^2].H &&
                        c[^1].O < c[^1].C &&
                        c[^1].L > c[^2].L &&
                        c[^3].L > c[^2].L
                },
                {
                    CandlePatternType.DOWN_PPR, c =>
                        c[^3].O < c[^3].C &&
                        c[^2].O < c[^2].C &&
                        c[^3].L < c[^2].L &&
                        c[^1].O > c[^1].C &&
                        c[^1].H < c[^2].H &&
                        c[^3].H < c[^2].H
                }
            };

        /// <summary>
        /// Initializes a new instance of the <see cref="CandlePatternFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="patterns">Patterns we are looking for. Null for all available.</param>
        public CandlePatternFinder(IBarsProvider barsProvider, 
            HashSet<CandlePatternType> patterns = null)
        {
            m_BarsProvider = barsProvider;
            patterns ??= Enum.GetValues<CandlePatternType>().ToHashSet();
            m_Patterns = patterns;
        }

        /// <summary>
        /// Gets the candle patterns for the specified index of the bar or null.
        /// </summary>
        /// <param name="barIndex">Index of the bar.</param>
        public List<CandlesResult> GetCandlePatterns(int barIndex)
        {
            if (barIndex < MIN_BARS_INDEX)
                return null;

            var candles = new Candle[MIN_BARS_INDEX + 1];
            for (int i = barIndex; i >= barIndex - MIN_BARS_INDEX; i--)
            {
                double h = m_BarsProvider.GetHighPrice(i);
                double l = m_BarsProvider.GetLowPrice(i);
                double range = h - l;
                if (range < 0)
                    return null;

                candles[i] = new Candle(m_BarsProvider.GetOpenPrice(i),
                    h,
                    l,
                    m_BarsProvider.GetClosePrice(i));
            }

            List<CandlesResult> res = null;
            foreach (CandlePatternType candlePatternType in m_Patterns)
            {
                if (!m_PatternExpressionMap.TryGetValue(
                        candlePatternType, out Func<Candle[], bool> func) ||
                    !m_PatternDirectionMap.TryGetValue(candlePatternType, out CandlePatternSettings settings) ||
                    !func(candles))
                {
                    continue;
                }

                res ??= new List<CandlesResult>();

                double sl = 0;
                switch (settings.StopLossBarIndex)
                {
                    case -1:// max or min of indices 0 and 1
                        sl = settings.IsBull
                            ? Math.Min(candles[^1].L, candles[^2].L)
                            : Math.Max(candles[^1].L, candles[^2].L);
                        break;
                    case 1:// max or min of index 1
                        sl = settings.IsBull ? candles[^2].L : candles[^2].H;
                        break;
                    case 0:// max or min of index 0
                        sl = settings.IsBull ? candles[^1].L : candles[^1].H;
                        break;
                }

                if (sl == 0)
                    continue;

                res.Add(new CandlesResult(candlePatternType, settings.IsBull, sl, barIndex));
            }

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
        private static bool IsOneCandlePattern(Candle c, Func<Candle, double, bool> expression)
        {
            double range = c.H - c.L;
            double ratio = range * ONE_CANDLE_RATIO;
            bool isPattern = expression(c, ratio);
            return isPattern;
        }
    }
}
