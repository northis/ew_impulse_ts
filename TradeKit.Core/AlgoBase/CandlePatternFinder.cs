using System.Diagnostics;
using TradeKit.Core.Common;
using TradeKit.Core.PriceAction;
using static TradeKit.Core.PriceAction.CandlePatternType;
using CPS = TradeKit.Core.PriceAction.CandlePatternSettings;
using CPT = TradeKit.Core.PriceAction.CandlePatternType;

namespace TradeKit.Core.AlgoBase
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
        private const double DOJI_MIN_PERCENT_WICKS = 60;
        private const double DOJI_PREV_PIP_MIN_BODY_SIZE = 15;
        private const double PIECING_LINE_DARK_CLOUD_C = 0.7;
        private const int MIN_BARS_INDEX = 9;//0..9

        private static readonly Dictionary<CPT, CPS>
            PATTERN_DIRECTION_MAP = new()
            {
                {HAMMER, new CPS(true, 0, 1)},
                {INVERTED_HAMMER, new CPS(false, 0, 1)},
                {UP_PIN_BAR, new CPS(true, 0, 2)},
                {DOWN_PIN_BAR, new CPS(false, 0, 2)},
                {UP_PIN_BAR_TRIO, new CPS(true, 1, 3)},
                {DOWN_PIN_BAR_TRIO, new CPS(false, 1, 3)},
                {UP_OUTER_BAR, new CPS(true, 0, 2)},
                {DOWN_OUTER_BAR, new CPS(false, 0, 2)},
                {UP_OUTER_BAR_BODIES, new CPS(true, -1, 2)},
                {DOWN_OUTER_BAR_BODIES, new CPS(false, -1, 2)},
                {UP_INNER_BAR, new CPS(true, 1, 2)},
                {DOWN_INNER_BAR, new CPS(false, 1, 2)},
                {UP_PPR, new CPS(true, 1, 3)},
                {DOWN_PPR, new CPS(false, 1, 3)},
                {UP_RAILS, new CPS(true, -1, 2, 0)},
                {DOWN_RAILS, new CPS(false, -1, 2, 0)},
                {UP_PPR_IB, new CPS(true, -1, 4, 0)},
                {DOWN_PPR_IB, new CPS(false, -1, 4, 0)},
                {UP_DOUBLE_INNER_BAR, new CPS(true, -1, 3, 0)},
                {DOWN_DOUBLE_INNER_BAR, new CPS(false, -1, 3, 0)},
                {UP_CPPR, new CPS(true, -1, 0)},
                {DOWN_CPPR, new CPS(false, -1, 0)},
                {UP_DOJI, new CPS(true, 0, 2)},
                {DOWN_DOJI, new CPS(false, 0, 2)},
                {PIECING_LINE, new CPS(true, 0, 2)},
                {DARK_CLOUD, new CPS(false, 0, 2)},
                {DOWN_HARAMI, new CPS(false, 0, 2)},
                {UP_HARAMI, new CPS(true, 0, 2)}
            };

        // Func - array of candles and count of the bars in the pattern
        private static readonly Dictionary<CPT, Func<CandleParams, int>>
            PATTERN_EXPRESSION_MAP = new()
            {
                {
                    HAMMER, cdParams => IsOneCandlePattern(cdParams.Candles[^1],
                        (c, r) => c.O > c.C && c.O > c.L + r && c.C > c.L + r)
                        ? PATTERN_DIRECTION_MAP[HAMMER].BarsCount
                        : 0
                },
                {
                    INVERTED_HAMMER, cdParams => IsOneCandlePattern(cdParams.Candles[^1],
                        (c, r) => c.O < c.C && c.O < c.H - r && c.C < c.H - r)
                        ? PATTERN_DIRECTION_MAP[INVERTED_HAMMER].BarsCount
                        : 0
                },
                {
                    UP_PIN_BAR, cdParams => 
                        IsPinBar(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_PIN_BAR].BarsCount
                        : 0
                },
                {
                    DOWN_PIN_BAR, cdParams => 
                        IsPinBar(cdParams.Candles, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_PIN_BAR].BarsCount
                        : 0
                },
                {
                    UP_PIN_BAR_TRIO, cdParams =>
                        IsPinBarTrio(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                            ? PATTERN_DIRECTION_MAP[UP_PIN_BAR_TRIO].BarsCount
                            : 0
                },
                {
                    DOWN_PIN_BAR_TRIO, cdParams =>
                        IsPinBarTrio(cdParams.Candles, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                            ? PATTERN_DIRECTION_MAP[DOWN_PIN_BAR_TRIO].BarsCount
                            : 0
                },
                {
                    UP_OUTER_BAR, cdParams => 
                        IsOuterBar(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_OUTER_BAR].BarsCount
                        : 0
                },
                {
                    DOWN_OUTER_BAR, cdParams => 
                        IsOuterBar(cdParams.Candles, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_OUTER_BAR].BarsCount
                        : 0
                },
                {
                    UP_OUTER_BAR_BODIES, cdParams => 
                        IsOuterBarBodies(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_OUTER_BAR_BODIES].BarsCount
                        : 0
                },
                {
                    DOWN_OUTER_BAR_BODIES, cdParams => IsOuterBarBodies(cdParams.Candles, false) && 
                                                           IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_OUTER_BAR_BODIES].BarsCount
                        : 0
                },
                {
                    UP_INNER_BAR, cdParams => 
                        IsInnerBar(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_INNER_BAR].BarsCount
                        : 0
                },
                {
                    DOWN_INNER_BAR, cdParams =>
                        IsInnerBar(cdParams.Candles, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_INNER_BAR].BarsCount
                        : 0
                },
                {
                    UP_PPR, cdParams => 
                        IsPpr(cdParams.Candles, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_PPR].BarsCount
                        : 0
                },
                {
                    DOWN_PPR, cdParams => 
                        IsPpr(cdParams.Candles, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_PPR].BarsCount
                        : 0
                },
                {
                    UP_RAILS, cdParams => IsRails(cdParams.Candles, true) 
                        ? PATTERN_DIRECTION_MAP[UP_RAILS].BarsCount 
                        : 0
                },
                {
                    DOWN_RAILS, cdParams => IsRails(cdParams.Candles, false) 
                        ? PATTERN_DIRECTION_MAP[DOWN_RAILS].BarsCount 
                        : 0
                },
                {
                    UP_PPR_IB, cdParams => IsPprAndIb(cdParams.Candles) 
                        ? PATTERN_DIRECTION_MAP[UP_PPR_IB].BarsCount 
                        : 0
                },
                {
                    DOWN_PPR_IB, cdParams => IsPprAndIb(cdParams.Candles) 
                        ? PATTERN_DIRECTION_MAP[DOWN_PPR_IB].BarsCount 
                        : 0
                },
                {
                    UP_DOUBLE_INNER_BAR, cdParams => IsDoubleInnerBar(cdParams.Candles, true) 
                        ? PATTERN_DIRECTION_MAP[UP_DOUBLE_INNER_BAR].BarsCount 
                        : 0
                },
                {
                    DOWN_DOUBLE_INNER_BAR, cdParams => IsDoubleInnerBar(cdParams.Candles, false) 
                        ? PATTERN_DIRECTION_MAP[DOWN_DOUBLE_INNER_BAR].BarsCount 
                        : 0
                },
                {
                    UP_CPPR, cdParams => GetCPprCount(cdParams.Candles, true)
                },
                {
                    DOWN_CPPR, cdParams => GetCPprCount(cdParams.Candles, false)
                },
                {
                    UP_DOJI, cdParams => 
                        IsDoji(cdParams, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[UP_DOJI].BarsCount
                        : 0
                },
                {
                    DOWN_DOJI, cdParams => 
                        IsDoji(cdParams, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DOWN_DOJI].BarsCount
                        : 0
                },
                {
                    PIECING_LINE, cdParams =>
                        IsPiecingLineDarkCloud(cdParams, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                        ? PATTERN_DIRECTION_MAP[PIECING_LINE].BarsCount
                        : 0
                },
                {
                    DARK_CLOUD, cdParams =>
                        IsPiecingLineDarkCloud(cdParams, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                        ? PATTERN_DIRECTION_MAP[DARK_CLOUD].BarsCount
                        : 0
                },
                {
                    UP_HARAMI, cdParams =>
                        IsHarami(cdParams, true) && IsStrengthBarInner(cdParams.Candles[^1], true)
                            ? PATTERN_DIRECTION_MAP[UP_HARAMI].BarsCount
                            : 0
                },
                {
                    DOWN_HARAMI, cdParams =>
                        IsHarami(cdParams, false) && IsStrengthBarInner(cdParams.Candles[^1], false)
                            ? PATTERN_DIRECTION_MAP[DOWN_HARAMI].BarsCount
                            : 0
                }
            };

        private static int GetCPprCount(Candle[] c, bool _)
        {
            for (int i = c.Length - 3; i >= 0; i--)
            {
                Candle rangeL = c[i];
                bool isInside = true;
                for (int j = i + 1; j < c.Length; j++)
                {
                    if (rangeL.H <= c[j].H || rangeL.L >= c[j].L)
                    {
                        isInside = false;
                        break;
                    }
                }

                if (isInside)
                    return c.Length - i;
            }

            return 0;
        }

        private static bool IsPinBarTrio(Candle[] c, bool isUp)
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

            //bool pb = IsPinBar(c[..^1], isUp);
            return res;// && pb;
        }

        private static bool IsPinBar(Candle[] c, bool isUp)
        {
            double bodyPb = Math.Abs(c[^1].C - c[^1].O);
            double bodyPb1 = Math.Abs(c[^2].C - c[^2].O);
            if (bodyPb1 <= bodyPb)
                return false;

            double upShadowPb;
            double downShadowPb;
            if (isUp)
            {
                if (c[^2].O <= c[^2].C)
                    return false;

                downShadowPb = c[^1].O - c[^1].L;
                if (downShadowPb <= 0.5 * bodyPb)
                    return false;

                upShadowPb = c[^1].H - c[^1].C;
                if (downShadowPb <= 2 * upShadowPb)
                    return false;

                return true;
            }

            if (c[^2].C <= c[^2].O)
                return false;

            upShadowPb = c[^1].H - c[^1].O;
            if (upShadowPb <= 0.5 * bodyPb)
                return false;

            downShadowPb = c[^1].C - c[^1].L;
            if (upShadowPb <= 2 * downShadowPb)
                return false;

            return true;
        }

        private static bool IsDoji(CandleParams candleParams, bool isUp)
        {
            Candle[] c = candleParams.Candles;

            double pctCDw = DOJI_MIN_PERCENT_WICKS / 2 * 0.01;
            double pctCDwBody = pctCDw * c[^1].Length;
            double pctCDb = (100 - DOJI_MIN_PERCENT_WICKS) * 0.01;
            double minPip = DOJI_PREV_PIP_MIN_BODY_SIZE * Math.Pow(10, -candleParams.Symbol.Digits);

            double bodyPb = Math.Abs(c[^1].C - c[^1].O);
            double maxCo1 = Math.Max(c[^1].C, c[^1].O);
            double minCo1 = Math.Min(c[^1].C, c[^1].O);

            if (bodyPb / c[^1].Length >= pctCDb)
                return false;

            if (c[^1].H - maxCo1 <= pctCDwBody)
                return false;

            if (minCo1 - c[^1].L <= pctCDwBody)
                return false;

            if (isUp)
            {
                if (c[^2].O <= c[^2].C)
                    return false;

                if (c[^2].O - c[^2].C <= minPip)
                    return false;


                if (c[^2].O < maxCo1)
                    return false;

                if (c[^2].C > minCo1)
                    return false;

                return true;
            }

            if (c[^2].O >= c[^2].C)
                return false;

            if (c[^2].C - c[^2].O <= minPip)
                return false;

            if (c[^2].C < maxCo1)
                return false;

            if (c[^2].O > minCo1)
                return false;

            return true;
        }
        private static bool IsHarami(CandleParams candleParams, bool isUp)
        {
            Candle[] c = candleParams.Candles;

            if (isUp)
            {
                if (c[^2].O <= c[^2].C)
                    return false;

                if (c[^1].C <= c[^1].O)
                    return false;

                if (c[^1].C > c[^2].O)
                    return false;

                if (c[^2].C > c[^2].O)
                    return false;

                if (c[^1].C - c[^1].O >= c[^2].O- c[^2].C)
                    return false;

                return true;
            }

            if (c[^2].O >= c[^2].C)
                return false;

            if (c[^1].O <= c[^1].C)
                return false;

            if (c[^1].O > c[^2].C)
                return false;

            if (c[^2].O > c[^1].C)
                return false;

            if (c[^1].O - c[^1].C >= c[^2].C - c[^2].O)
                return false;

            return true;
        }

        private static bool IsPiecingLineDarkCloud(CandleParams candleParams, bool isUp)
        {
            Candle[] c = candleParams.Candles;
            double minPip = DOJI_PREV_PIP_MIN_BODY_SIZE * Math.Pow(10, -candleParams.Symbol.Digits);
            double oToC = c[^1].O - c[^1].C;
            double oToC1 = c[^2].O - c[^2].C;

            if (Math.Abs(oToC1) / c[^2].Length < PIECING_LINE_DARK_CLOUD_C)
                return false;

            if (Math.Abs(oToC) / c[^1].Length < PIECING_LINE_DARK_CLOUD_C)
                return false;

            if (isUp)
            {
                if (oToC1 <= 0)
                    return false;

                if (oToC1 <= minPip)
                    return false;

                if (oToC >= 0)
                    return false;

                if (c[^1].O > c[^2].C)
                    return false;

                if (c[^1].C >= c[^2].O)
                    return false;

                if (c[^1].C <= (c[^2].O + c[^2].C) / 2)
                    return false;

                return true;
            }

            if (oToC1 >= 0)
                return false;

            if (oToC1 >= minPip)
                return false;

            if (oToC <= 0)
                return false;

            if (c[^1].O < c[^2].C)
                return false;

            if (c[^1].C <= c[^2].O)
                return false;

            if (c[^1].C >= (c[^2].O + c[^2].C) / 2)
                return false;

            return true;
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

        private static bool IsStrengthBarInner(Candle candle, bool isUp)
        {
            if (!m_useStrengthBar)
                return true;

            bool res = Helper.IsStrengthBar(candle, isUp);
            return res;
        }

        private static bool IsPprAndIb(Candle[] c)
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
        /// <param name="barsProvider">The bar provider.</param>
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
                Candle candle = Candle.FromIndex(m_BarsProvider, index);
                
                if (candle is null)
                    return null;

                candles[i] = candle;
            }

            
            List<CandlesResult> res = null;
            foreach (CPT cpt in m_Patterns)
            {
                if (!PATTERN_EXPRESSION_MAP.TryGetValue(cpt, out Func<CandleParams, int> func) ||
                    !PATTERN_DIRECTION_MAP.TryGetValue(cpt, out CPS settings))
                {
                    continue;
                }

                int barsCount = func(new CandleParams(candles, m_BarsProvider.BarSymbol));
                if (barsCount == 0)
                {
                    continue;
                }

                res ??= new List<CandlesResult>();

                double sl;
                int slIndex = 0;
                double? limitPrice = null;
                
                if (settings.StopLossBarIndex >= 0)// If we know the extreme bar index
                {
                    int offset = settings.StopLossBarIndex + 1;
                    sl = settings.IsBull
                        ? candles[^offset].L
                        : candles[^offset].H;

                    slIndex = barIndex - settings.StopLossBarIndex;
                }
                else if (settings.BarsCount == 0)
                {
                    // For CPPR we want to use the 1st candle in the pattern
                    Candle firstBar = candles[^barsCount];
                    if (settings.IsBull)
                    {
                        sl = firstBar.L;
                        limitPrice = firstBar.H;
                    }
                    else
                    {
                        sl = firstBar.H;
                        limitPrice = firstBar.L;
                    }

                    slIndex = barIndex - barsCount;
                }
                else // If we find min or max inside the bars belongs to the pattern
                {
                    double max = double.MinValue;
                    double min = double.MaxValue;

                    for (int i = 0; i < barsCount; i++)
                    {
                        int j = i + 1; // We count from the end of array
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

                if (settings.LimitPriceBarIndex.HasValue)
                {
                    int offset = settings.LimitPriceBarIndex.Value + 1;
                    limitPrice = settings.IsBull
                        ? candles[^offset].H
                        : candles[^offset].L;
                }
                
                res.Add(new CandlesResult(cpt,
                    settings.IsBull, sl, slIndex, barIndex, (short)barsCount, limitPrice));
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
