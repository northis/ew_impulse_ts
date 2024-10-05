using Microsoft.FSharp.Core;
using PuppeteerSharp.Input;
using System.Globalization;
using TradeKit.Core.Gartley;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.Common
{
    public static class Extensions
    {
        private static readonly TimeSpan TIME_OFFSET = DateTime.UtcNow - DateTime.Now;

        /// <summary>
        /// To SVG path point.
        /// </summary>
        public static string ToSvgPoint(this BarPoint item)
        {
            string xVal = item.Value.ToString(CultureInfo.InvariantCulture);
            string xDat = item.OpenTime.Add(TIME_OFFSET).ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture);
            return $"{xDat} {xVal}";
        }

        /// <summary>
        /// DateTime to UNIX milliseconds.
        /// </summary>
        public static double ToUnixMilliseconds(this DateTime item)
        {
            return (item - DateTime.UnixEpoch).TotalMilliseconds;
        }

#if !GARTLEY_PROD
        /// <summary>
        /// Converts to F# object.
        /// </summary>
        public static FSharpOption<T> ToFSharp<T>(this T item)
        {
            return new FSharpOption<T>(item);
        }
#endif


#if !GARTLEY_PROD
        private static readonly Dictionary<CandlePatternType, string> CANDLE_PATTERN_NAME_MAP = new()
        {
            {CandlePatternType.DOWN_PIN_BAR, "PB\n ↓"},
            {CandlePatternType.UP_PIN_BAR, " ↑\nPB"},
            {CandlePatternType.DOWN_OUTER_BAR, "OB\n ↓"},
            {CandlePatternType.UP_OUTER_BAR, " ↑\nOB"},
            {CandlePatternType.DOWN_INNER_BAR, "IB\n ↓"},
            {CandlePatternType.UP_INNER_BAR, " ↑\nIB"},
            {CandlePatternType.DOWN_OUTER_BAR_BODIES, "OBB\n  ↓"},
            {CandlePatternType.UP_OUTER_BAR_BODIES, "  ↑\nOBB"},
            {CandlePatternType.DOWN_PPR, "PPR\n  ↓"},
            {CandlePatternType.UP_PPR, "  ↑\nPPR"},
            {CandlePatternType.INVERTED_HAMMER, "IH\n ↓"},
            {CandlePatternType.HAMMER, "↑\nH"},
            {CandlePatternType.UP_RAILS, "↑\nR"},
            {CandlePatternType.DOWN_RAILS, "R\n↓"},
            {CandlePatternType.UP_PPR_IB, "  ↑\nP+I"},
            {CandlePatternType.DOWN_PPR_IB, "P+I\n  ↓"},
            {CandlePatternType.UP_DOUBLE_INNER_BAR, "  ↑\nDIB"},
            {CandlePatternType.DOWN_DOUBLE_INNER_BAR, "DIB\n  ↓"},
            {CandlePatternType.UP_CPPR, "     ↑\nCPPR"},
            {CandlePatternType.DOWN_CPPR, "CPPR\n     ↓"},
            {CandlePatternType.DOWN_DOJI, "Doji\n  ↓"},
            {CandlePatternType.UP_DOJI, "  ↑\nDoji"},
            {CandlePatternType.DARK_CLOUD, "DC\n  ↓"},
            {CandlePatternType.PIECING_LINE, "  ↑\nPL"},
            {CandlePatternType.DOWN_HARAMI, "HAR\n   ↓"},
            {CandlePatternType.UP_HARAMI, "   ↑\nHAR"}
        };

        /// <summary>
        /// Formats the <see cref="CandlePatternType"/> enum.
        /// </summary>
        /// <param name="type">The candle pattern type.</param>
        public static string Format(this CandlePatternType type)
        {
            if (CANDLE_PATTERN_NAME_MAP.TryGetValue(type, out string val))
                return val;
            return type.ToString();
        }
#endif
        private static readonly Dictionary<GartleyPatternType, string> GARTLEY_PATTERN_NAME_MAP = new()
        {
            {GartleyPatternType.ALT_BAT, "Alt. Bat"},
            {GartleyPatternType.BAT, "Bat"},
            {GartleyPatternType.BUTTERFLY, "Butterfly"},
            {GartleyPatternType.CRAB, "Crab"},
            {GartleyPatternType.CYPHER, "Cypher"},
            {GartleyPatternType.DEEP_CRAB, "Deep Crab"},
            {GartleyPatternType.GARTLEY, "Gartley"},
            {GartleyPatternType.SHARK, "Shark"}
        };

        /// <summary>
        /// Formats the <see cref="GartleyPatternType"/> enum.
        /// </summary>
        /// <param name="type">The Gartley pattern type.</param>
        public static string Format(this GartleyPatternType type)
        {
            if (GARTLEY_PATTERN_NAME_MAP.TryGetValue(type, out string val))
                return val;
            return type.ToString();
        }

        /// <summary>
        /// Gets the string for the specified value for ratios from 0 to 9.
        /// </summary>
        /// <param name="val">The double value.</param>
        public static string Ratio(this double val)
        {
            return val.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Adds the value to the sorted dict with list-backed value.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedDictionary">The sorted dict.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public static void AddValue<TK, TV>(
            this SortedDictionary<TK, List<TV>> sortedDictionary, TK key, TV value)
        {
            if (!sortedDictionary.TryGetValue(key, out List<TV> valList))
            {
                valList = new List<TV>();
                sortedDictionary[key] = valList;
            }

            valList.Add(value);
        }

        /// <summary>
        /// Slices the ordered array by value.
        /// </summary>
        /// <param name="inDoubles">The array of doubles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <returns>The sliced array</returns>
        public static double[] RangeVal(
            this double[] inDoubles, double startValue, double endValue)
        {
            return inDoubles
                .SkipWhile(a => a < startValue)
                .TakeWhile(a => a <= endValue)
                .ToArray();
        }

        /// <summary>
        /// Removes according to the enumerable.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="toDeleteEnumerable">The enumerable to delete.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveWhere<TK, TV>(
            this IDictionary<TK, TV> sortedList, IEnumerable<TK> toDeleteEnumerable)
        {
            var keysToRemove = new List<TK>();
            foreach (TK key in toDeleteEnumerable)
            {
                keysToRemove.Add(key);
            }

            foreach (TK key in keysToRemove)
            {
                sortedList.Remove(key);
            }

            return keysToRemove.Count;
        }

        /// <summary>
        /// Removes according to the func (left part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveLeft<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.TakeWhile(compareFunc));
        }

        /// <summary>
        /// Removes according to the func (right part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveRight<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.SkipWhile(compareFunc));
        }

        /// <summary>
        /// Gets a new <see cref="BarPoint"/> with given index.
        /// </summary>
        /// <param name="bp">The bar point.</param>
        /// <param name="index">The new bar index.</param>
        /// <param name="provider">Bar provider to update the open date.</param>
        public static BarPoint WithIndex(this BarPoint bp, int index, IBarsProvider provider)
        {
            return new BarPoint(bp.Value, provider.GetOpenTime(index), bp.BarTimeFrame, index);
        }

        /// <summary>
        /// Gets a new <see cref="BarPoint"/> with given price.
        /// </summary>
        /// <param name="bp">The bar point.</param>
        /// <param name="price">The changed price.</param>
        public static BarPoint WithPrice(this BarPoint bp, double price)
        {
            return new BarPoint(price, bp.OpenTime, bp.BarTimeFrame, bp.BarIndex);
        }

        /// <summary>
        /// Normalizes the extrema from Zigzag indicator.
        /// </summary>
        /// <param name="extrema">The extrema.</param>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        public static void NormalizeExtrema(
            this List<BarPoint> extrema, BarPoint start, BarPoint end)
        {
            if (extrema.Count == 0)
            {
                extrema.Add(start);
                extrema.Add(end);
            }
            else
            {
                if (extrema[0].OpenTime == start.OpenTime)
                {
                    extrema[0] = start;
                }
                else
                {
                    extrema.Insert(0, start);
                }

                if (extrema[^1].OpenTime == end.OpenTime)
                {
                    extrema[^1] = end;
                }
                else
                {
                    extrema.Add(end);
                }
            }

            const int minPointCount = 4;
            if (extrema.Count < minPointCount)
            {
                return;
            }

            // We want to leave only true extrema
            BarPoint current = start;
            bool direction = start > end;
            List<BarPoint> toDelete = null;
            for (int i = 1; i < extrema.Count; i++)
            {
                BarPoint extremum = extrema[i];
                bool newDirection = current < extremum;
                if (direction == newDirection)
                {
                    toDelete ??= new List<BarPoint>();
                    toDelete.Add(current);
                }

                direction = newDirection;
                current = extremum;
            }

            if (toDelete == null)
            {
                return;
            }

            foreach (BarPoint toDeleteItem in toDelete)
            {
                extrema.Remove(toDeleteItem);
            }
        }

        /// <summary>
        /// Gets the extremum between two dates.
        /// The start and end candles ARE NOT included.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTimeStart">The start date time.</param>
        /// <param name="dateTimeEnd">The end date time.</param>
        /// <param name="isHigh">True if we want to find a high, false - if a low.</param>
        public static BarPoint GetExtremumBetween(
            this IBarsProvider barProvider,
            DateTime dateTimeStart,
            DateTime dateTimeEnd,
            bool isHigh)
        {
            int startIndex = barProvider.GetIndexByTime(dateTimeStart) + 1;//! +1
            int endIndex = barProvider.GetIndexByTime(dateTimeEnd);

            double currentValue = isHigh
                ? barProvider.GetHighPrice(startIndex)
                : barProvider.GetLowPrice(startIndex);
            int currentIndex = startIndex;

            for (int i = startIndex; i < endIndex; i++)//! <
            {
                if (isHigh && barProvider.GetHighPrice(i) >= currentValue ||
                    !isHigh && barProvider.GetLowPrice(i) <= currentValue)
                {
                    currentValue = isHigh
                        ? barProvider.GetHighPrice(i)
                        : barProvider.GetLowPrice(i);
                    currentIndex = i;
                }
            }

            return endIndex <= currentIndex ? null : new BarPoint(currentValue, currentIndex, barProvider);
        }

        /// <summary>
        /// Gets the high price.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTime">The date time.</param>
        public static double GetHighPrice(
            this IBarsProvider barProvider, DateTime dateTime)
        {
            int index = barProvider.GetIndexByTime(dateTime);
            double res = barProvider.GetHighPrice(index);

            return res;
        }

        /// <summary>
        /// Gets the low price.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTime">The date time.</param>
        public static double GetLowPrice(
            this IBarsProvider barProvider, DateTime dateTime)
        {
            int index = barProvider.GetIndexByTime(dateTime);
            double res = barProvider.GetLowPrice(index);

            return res;
        }

        /// <summary>
        /// Initializes the IsHighFirst property. This can be costly, we do this on-demand only.
        /// </summary>
        /// <param name="candle">The candle we should check</param>
        /// <param name="barsFunc">The time frame to bars provider function.</param>
        /// <param name="candleTimeFrame">The TF of the candle.</param>
        public static void InitIsHighFirst(
            this Candle candle, Func<ITimeFrame, IBarsProvider> barsFunc, ITimeFrame candleTimeFrame)
        {
            if (candle.IsHighFirst.HasValue || !candle.Index.HasValue)
                return;

            IBarsProvider currentBarsProvider = barsFunc(candleTimeFrame);

            DateTime startDate = currentBarsProvider.GetOpenTime(candle.Index.Value);
            TimeFrameInfo timeFrameInfo = TimeFrameHelper.GetTimeFrameInfo(candleTimeFrame);
            DateTime endDate = startDate + timeFrameInfo.TimeSpan;

            ITimeFrame currentTimeFrame = candleTimeFrame;

            for (; ; )
            {
                var prevTimeFrame = TimeFrameHelper.GetPreviousTimeFrameInfo(currentTimeFrame).TimeFrame;
                if (currentTimeFrame == prevTimeFrame)
                    break;

                IBarsProvider barsProvider1M = barsFunc(prevTimeFrame);
                int startIndex1M = barsProvider1M.GetIndexByTime(startDate);
                if (startIndex1M == -1)
                    barsProvider1M.LoadBars(startDate);

                int endIndex1M = barsProvider1M.GetIndexByTime(endDate);

                var highIndex = 0;
                var lowIndex = 0;

                for (int i = startIndex1M; i <= endIndex1M; i++)
                {
                    double high = barsProvider1M.GetHighPrice(i);
                    if (Math.Abs(candle.H - high) < double.Epsilon)
                    {
                        highIndex++;
                    }

                    double low = barsProvider1M.GetLowPrice(i);
                    if (Math.Abs(candle.L - low) < double.Epsilon)
                    {
                        lowIndex++;
                    }

                    if (highIndex == lowIndex)
                        continue;

                    break;
                }

                candle.IsHighFirst = highIndex < lowIndex;
                break;
            }
        }

        /// <summary>
        /// Returns the amount of volume based on your provided risk percentage and stop loss
        /// </summary>
        /// <param name="symbol">The symbol</param>
        /// <param name="riskPercentage">Risk percentage amount</param>
        /// <param name="accountBalance">The account balance</param>
        /// <param name="stopLossInPips">Stop loss amount in Pips</param>
        public static double GetVolume(this ISymbol symbol, double riskPercentage, double accountBalance, double stopLossInPips)
        {
            return riskPercentage / (Math.Abs(stopLossInPips) * symbol.PipValue / accountBalance * 100);
        }
    }
}
