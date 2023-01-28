using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Gartley;

#if !GARTLEY_PROD
using TradeKit.PriceAction;
using Microsoft.FSharp.Core;
#endif

namespace TradeKit.Core
{
    internal static class Extensions
    {
        private static readonly TimeSpan TIME_OFFSET = DateTime.UtcNow - DateTime.Now;

        #region UnixEpoch

        private const long TicksPerMillisecond = 10000;
        private const long TicksPerSecond = TicksPerMillisecond * 1000;
        private const long TicksPerMinute = TicksPerSecond * 60;
        private const long TicksPerHour = TicksPerMinute * 60;
        private const long TicksPerDay = TicksPerHour * 24;

        // Number of days in a non-leap year
        private const int DaysPerYear = 365;
        // Number of days in 4 years
        private const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
        // Number of days in 100 years
        private const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
        // Number of days in 400 years
        private const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097

        // Number of days from 1/1/0001 to 12/31/1600
        private const int DaysTo1601 = DaysPer400Years * 4;          // 584388
        // Number of days from 1/1/0001 to 12/30/1899
        private const int DaysTo1899 = DaysPer400Years * 4 + DaysPer100Years * 3 - 367;
        // Number of days from 1/1/0001 to 12/31/1969
        internal const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162
        internal const long UnixEpochTicks = DaysTo1970 * TicksPerDay;

        #endregion
        public static readonly DateTime UnixEpoch = new DateTime(UnixEpochTicks, DateTimeKind.Utc);

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
            return (item - UnixEpoch).TotalMilliseconds;
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


#if !GARTLEY_PROD
        private static readonly Dictionary<CandlePatternType, string> CANDLE_PATTERN_NAME_MAP = new Dictionary<CandlePatternType, string>
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
            {CandlePatternType.DOWN_CPPR, "CPPR\n     ↓"}
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
        private static readonly Dictionary<GartleyPatternType, string> GARTLEY_PATTERN_NAME_MAP = new Dictionary<GartleyPatternType, string>
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
        /// Sets the <see cref="rectangle"/> filled.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        /// <returns>The rectangle.</returns>
        public static ChartRectangle SetFilled(this ChartRectangle rectangle)
        {
            rectangle.IsFilled = true;
            return rectangle;
        }

        /// <summary>
        /// Aligns the <see cref="ChartText"/> item.
        /// </summary>
        /// <param name="textItem">The text item.</param>
        /// <param name="isUp">Label location.</param>
        /// <param name="horizontalAlignment">The horizontal alignment.</param>
        /// <returns>The <see cref="ChartText"/> ite</returns>
        public static ChartText ChartTextAlign(this ChartText textItem, bool isUp,
            HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center)
        {
            textItem.HorizontalAlignment = horizontalAlignment;
            textItem.VerticalAlignment = isUp ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            return textItem;
        }

        /// <summary>
        /// Shows the text for <see cref="ChartTrendLine"/>.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <param name="chart">The chart.</param>
        /// <param name="text">The text.</param>
        /// <param name="isUp">if set to <c>true</c> text will show above the line.</param>
        /// <param name="x1">The x1.</param>
        /// <param name="x2">The x2.</param>
        /// <returns><see cref="ChartTrendLine"/> object.</returns>
        public static ChartTrendLine TextForLine(
            this ChartTrendLine line, Chart chart, string text, bool isUp, int x1, int x2)
        {
            double max = Math.Max(line.Y1, line.Y2);
            double min = Math.Min(line.Y1, line.Y2);
            int maxX = Math.Max(x1, x2);
            int minX = Math.Min(x1, x2);
            double y = max - (max - min) / 2;
            int x = minX + Convert.ToInt32((maxX - minX) / 2);
            chart.DrawText(line.Name + "Text", text, x, y, line.Color).ChartTextAlign(isUp);
            return line;
        }

        /// <summary>
        /// Returns the amount of volume based on your provided risk percentage and stop loss
        /// </summary>
        /// <param name="symbol">The symbol</param>
        /// <param name="riskPercentage">Risk percentage amount</param>
        /// <param name="accountBalance">The account balance</param>
        /// <param name="stopLossInPips">Stop loss amount in Pips</param>
        public static double GetVolume(this Symbol symbol, double riskPercentage, double accountBalance, double stopLossInPips)
        {
            return symbol.NormalizeVolumeInUnits(riskPercentage / (Math.Abs(stopLossInPips) * symbol.PipValue / accountBalance * 100));
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
            return inDoubles.SkipWhile(a => a < startValue).TakeWhile(a => a <= endValue).ToArray();
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

                if (extrema[extrema.Count-1].OpenTime == end.OpenTime)
                {
                    extrema[extrema.Count - 1] = end;
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
                    toDelete = toDelete ?? new List<BarPoint>();
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

    }
}
