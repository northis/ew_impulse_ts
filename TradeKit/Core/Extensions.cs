using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;

namespace TradeKit.Core
{
    public static class Extensions
    {
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
                    extrema[0].Value = start.Value;
                }
                else
                {
                    extrema.Insert(0, start);
                }

                if (extrema[^1].OpenTime == end.OpenTime)
                {
                    extrema[^1].Value = end.Value;
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
