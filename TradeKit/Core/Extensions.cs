using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;

namespace TradeKit.Core
{
    public static class Extensions
    {
        /// <summary>
        /// Gets the string for the specified value for ratios from 0 to 9.
        /// </summary>
        /// <param name="val">The double value.</param>
        public static string Ratio(this double val)
        {
            return val.ToString("0.###", CultureInfo.InvariantCulture);
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
            double y = max - Convert.ToInt32((max - min) / 2);
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

    }
}
