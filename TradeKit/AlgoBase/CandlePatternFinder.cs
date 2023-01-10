using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TradeKit.Core;
using TradeKit.PriceAction;
using CPS = TradeKit.PriceAction.CandlePatternSettings;
using CPT = TradeKit.PriceAction.CandlePatternType;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Finds candle-based patters
    /// </summary>
    public class CandlePatternFinder
    {
        private readonly IBarsProvider m_BarsProvider;
        private static bool m_useStrengthBar;
        private readonly HashSet<CPT> m_Patterns;
        private const double ONE_CANDLE_RATIO = 0.7;
        private const int MIN_BARS_INDEX = 3;//0,1,2,3

        private readonly Dictionary<CPT, CPS>
            m_PatternDirectionMap = new()
            {
                {CPT.HAMMER, new CPS(true, 0, 1)},
                {CPT.INVERTED_HAMMER, new CPS(false, 0, 1)},
                {CPT.UP_PIN_BAR, new CPS(true, 1, 3)},
                {CPT.DOWN_PIN_BAR, new CPS(false, 1, 3)},
                {CPT.UP_OUTER_BAR, new CPS(true, 0, 2)},
                {CPT.DOWN_OUTER_BAR, new CPS(false, 0, 2)},
                {CPT.UP_OUTER_BAR_BODIES, new CPS(true, -1, 2)},
                {CPT.DOWN_OUTER_BAR_BODIES, new CPS(false, -1, 2)},
                {CPT.UP_INNER_BAR, new CPS(true, 1, 2)},
                {CPT.DOWN_INNER_BAR, new CPS(false, 1, 2)},
                {CPT.UP_PPR, new CPS(true, 1, 3)},
                {CPT.DOWN_PPR, new CPS(false, 1, 3)},
                {CPT.UP_RAILS, new CPS(true, -1, 2, 0)},
                {CPT.DOWN_RAILS, new CPS(false, -1, 2, 0)},
                {CPT.UP_BOWMAN, new CPS(true, -1, 4, 0)},
                {CPT.DOWN_BOWMAN, new CPS(false, -1, 4, 0)},
                {CPT.UP_DOUBLE_INNER_BAR, new CPS(true, -1, 3, 0)},
                {CPT.DOWN_DOUBLE_INNER_BAR, new CPS(false, -1, 3, 0)}
            };

        private static readonly Dictionary<CPT, Func<Candle[], bool>>
            PATTERN_EXPRESSION_MAP = new()
            {
                {
                    CPT.HAMMER, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O < c.C && c.O < c.H - r && c.C < c.H - r)
                },
                {
                    CPT.INVERTED_HAMMER, cls => IsOneCandlePattern(cls[^1],
                        (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r)
                },
                {
                    CPT.UP_PIN_BAR, c => IsPinBar(c,true) && IsStrengthBar(c[^1], true)
                },
                {
                    CPT.DOWN_PIN_BAR, c => IsPinBar(c,false) && IsStrengthBar(c[^1], false)
                },
                {
                    CPT.UP_OUTER_BAR, c => IsOuterBar(c, true) &&
                                           IsStrengthBar(c[^1], true)
                },
                {
                    CPT.DOWN_OUTER_BAR, c => IsOuterBar(c, false) &&
                                             IsStrengthBar(c[^1], false)
                },
                {
                    CPT.UP_OUTER_BAR_BODIES, c => IsOuterBarBodies(c, true) &&
                                                  IsStrengthBar(c[^1], true)
                },
                {
                    CPT.DOWN_OUTER_BAR_BODIES, c => IsOuterBarBodies(c, false) &&
                                                    IsStrengthBar(c[^1], false)
                },
                {
                    CPT.UP_INNER_BAR, c => IsInnerBar(c, true) &&
                                           IsStrengthBar(c[^1], true)
                },
                {
                    CPT.DOWN_INNER_BAR, c => IsInnerBar(c, false) &&
                                             IsStrengthBar(c[^1], false)
                },
                {
                    CPT.UP_PPR, c => IsPpr(c, true) &&
                                     IsStrengthBar(c[^1], true)
                },
                {
                    CPT.DOWN_PPR, c => IsPpr(c, false) &&
                                       IsStrengthBar(c[^1], false)
                },
                {
                    CPT.UP_RAILS, c => IsRails(c, true)
                },
                {
                    CPT.DOWN_RAILS, c => IsRails(c, false)
                },
                {
                    CPT.UP_BOWMAN, IsBowman
                },
                {
                    CPT.DOWN_BOWMAN, IsBowman
                },
                {
                    CPT.UP_DOUBLE_INNER_BAR, c => IsDoubleInnerBar(c, true)
                },
                {
                    CPT.DOWN_DOUBLE_INNER_BAR, c => IsDoubleInnerBar(c, false)
                }
            };

        private static bool IsPinBar(Candle[] c, bool isUp)
        {
            bool res;
            if (isUp)
            {
                double upperPart = c[^2].H - c[^2].Length / 3;
                double secHalf = c[^2].H - c[^2].Length / 2;
                res = c[^2].BodyLow >= upperPart &&
                      c[^3].L >= secHalf && c[^2].C <= c[^3].H &&
                      c[^1].L >= secHalf && c[^1].C > c[^2].H;
            }
            else
            {
                double lowerPart = c[^2].L + c[^2].Length / 3;
                double secHalf = c[^2].L + c[^2].Length / 2;
                res = c[^2].BodyHigh <= lowerPart &&
                      c[^3].H <= secHalf && c[^2].C >= c[^3].L &&
                      c[^1].H <= secHalf && c[^1].C < c[^2].L;
            }

            return res;
        }

        private static bool IsOuterBarBodies(Candle[] c, bool isUp)
        {
            bool res = isUp
                ? c[^1].O < c[^1].C &&
                  c[^2].BodyHigh <= c[^1].BodyHigh &&
                  c[^2].BodyLow >= c[^1].BodyLow
                : c[^1].O > c[^1].C &&
                  c[^2].BodyHigh <= c[^1].BodyHigh &&
                  c[^2].BodyLow >= c[^1].BodyLow;
            return res;
        }

        private static bool IsOuterBar(Candle[] c, bool isUp)
        {
            bool res = isUp
                ? c[^1].O < c[^1].C &&
                  c[^2].O > c[^1].C &&
                  c[^2].H < c[^1].H &&
                  c[^2].L > c[^1].L
                : c[^1].O > c[^1].C &&
                  c[^2].O < c[^1].C &&
                  c[^2].H < c[^1].H &&
                  c[^2].L > c[^1].L;
            return res;
        }

        private static bool IsInnerBar(Candle[] c, bool isUp)
        {
            bool res = isUp
                ? c[^1].O < c[^1].C &&
                  c[^2].H > c[^1].H &&
                  c[^2].L < c[^1].L &&
                  c[^2].C > c[^1].O &&
                  c[^2].O <= c[^1].O
                : c[^1].O > c[^1].C &&
                  c[^2].O < c[^2].C &&
                  c[^2].H > c[^1].H &&
                  c[^2].L < c[^1].L &&
                  c[^2].C >= c[^1].O;
            return res;
        }

        private static bool IsPpr(Candle[] c, bool isUp)
        {
            bool res = isUp
                ? c[^3].O > c[^3].C &&
                  c[^2].O > c[^2].C &&
                  c[^3].H > c[^2].H &&
                  c[^1].O < c[^1].C &&
                  c[^1].L > c[^2].L &&
                  c[^3].L > c[^2].L
                : c[^3].O < c[^3].C &&
                  c[^2].O < c[^2].C &&
                  c[^3].L < c[^2].L &&
                  c[^1].O > c[^1].C &&
                  c[^1].H < c[^2].H &&
                  c[^3].H < c[^2].H;
            return res;
        }

        private static bool IsDoubleInnerBar(Candle[] c, bool isUp)
        {
            Candle[] ib = c[..^1];
            bool res = IsInnerBar(ib, isUp) && IsInnerBar(c, !isUp);
            return res;
        }

        private static bool IsStrengthBar(Candle candle, bool isUp)
        {
            if (!m_useStrengthBar)
                return true;

            bool res = Math.Abs(candle.C - (isUp ? candle.H : candle.L)) < double.Epsilon;
            return res;
        }

        private static bool IsBowman(Candle[] c)
        {
            Candle[] ppr = c[..^1];
            bool res = IsPpr(ppr, false) && IsInnerBar(c, true) ||
                       IsPpr(ppr, true) && IsInnerBar(c, false);
            return res;
        }

        private static bool IsRails(Candle[] c, bool isUp)
        {
            double len = Math.Max(c[^2].H, c[^1].H) - Math.Min(c[^2].L, c[^1].L);
            if (len <= 0) return false;

            // We wanna somehow decide if these two candles are really long
            if (c[^3].Length > len * 0.5) return false; 

            double allowance = len * 0.1;
            double bodyMinLength = len * 0.7;
            bool isBodyCloseFit = c[^2].BodyHigh - c[^2].BodyLow >= bodyMinLength &&
                                  c[^1].BodyHigh - c[^1].BodyLow >= bodyMinLength &&
                                  Math.Abs(c[^2].C - c[^1].O) < allowance &&
                                  Math.Abs(c[^2].O - c[^1].C) < allowance;
            if (!isBodyCloseFit)
                return false;

            bool res = isUp
                ? c[^2].O > c[^2].C && c[^1].O < c[^1].C
                : c[^2].O < c[^2].C && c[^1].O > c[^1].C;

            return res;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CandlePatternFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="useStrengthBar">If true, we will use patterns with "bar of the strength" in the end.</param>
        /// <param name="patterns">Patterns we are looking for. Null for all available.</param>
        public CandlePatternFinder(IBarsProvider barsProvider,
            bool useStrengthBar = false,
            HashSet<CPT> patterns = null)
        {
            m_BarsProvider = barsProvider;
            m_useStrengthBar = useStrengthBar;
            patterns ??= Enum.GetValues<CPT>().ToHashSet();
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
            for (int i = 0; i <= MIN_BARS_INDEX; i++)
            {
                int index = barIndex - MIN_BARS_INDEX + i;
                double h = m_BarsProvider.GetHighPrice(index);
                double l = m_BarsProvider.GetLowPrice(index);
                double range = h - l;
                if (range < 0)
                    return null;

                candles[i] = new Candle(m_BarsProvider.GetOpenPrice(index),
                    h,
                    l,
                    m_BarsProvider.GetClosePrice(index));
            }

            
            List<CandlesResult> res = null;
            foreach (CPT CPT in m_Patterns)
            {
                if (!PATTERN_EXPRESSION_MAP.TryGetValue(
                        CPT, out Func<Candle[], bool> func) ||
                    !m_PatternDirectionMap.TryGetValue(CPT, out CPS settings) ||
                    !func(candles))
                {
                    continue;
                }
                
                res ??= new List<CandlesResult>();

                double sl;
                int slIndex = 0;

                if (settings.StopLossBarIndex >= 0)// If we know the extremum bar index
                {
                    int offset = settings.StopLossBarIndex + 1;
                    sl = settings.IsBull
                        ? candles[^offset].L
                        : candles[^offset].H;

                    slIndex = barIndex - settings.StopLossBarIndex;
                }
                else// If we find min or max inside the bars belongs to the pattern
                {
                    double max = double.MinValue;
                    double min = double.MaxValue;

                    for (int i = 0; i < settings.BarsCount; i++)
                    {
                        int j = i + 1;// We count from the end of array
                        if (candles[^j].H > max)
                        {
                            max = candles[^j].H;
                            if (!settings.IsBull)
                                slIndex = barIndex - i;
                        }

                        if (!(candles[^j].L < min))
                            continue;

                        min = candles[^j].L;
                        if (settings.IsBull)
                            slIndex = barIndex - i;
                    }

                    sl = settings.IsBull ? min : max;
                }

                if (sl == 0)
                    continue;

                double? limitPrice = null;
                if (settings.LimitPriceBarIndex.HasValue)
                {
                    int offset = settings.LimitPriceBarIndex.Value + 1;
                    limitPrice = settings.IsBull
                        ? candles[^offset].H
                        : candles[^offset].L;
                }
                
                res.Add(new CandlesResult(CPT,
                    settings.IsBull, sl, slIndex, barIndex, settings.BarsCount, limitPrice));
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
