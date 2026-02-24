using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Classifies whether a segment is an impulse by iteratively applying a zigzag indicator
    /// and checking the resulting wave structure.
    /// </summary>
    public static class IterativeZigzagImpulseClassifier
    {
        /// <summary>
        /// Determines if the segment from start to end is an impulse.
        /// </summary>
        /// <param name="start">The start point of the segment.</param>
        /// <param name="end">The end point of the segment.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="startPeriod">Optional. The starting period for the zigzag. If null, uses the segment length.</param>
        /// <returns>True if it's an impulse, false otherwise.</returns>
        public static bool IsImpulse(BarPoint start, BarPoint end, IBarsProvider barsProvider, int? startPeriod = null)
        {
            int barCount = end.BarIndex - start.BarIndex;
            if (barCount < 3)
                return false;

            bool isUp = end.Value > start.Value;
            
            int currentPeriod = startPeriod ?? barCount;
            if (currentPeriod < 1) 
                currentPeriod = 1;

            while (currentPeriod >= 1)
            {
                List<BarPoint> extrema = GetZigzagExtrema(start, end, currentPeriod, barsProvider, isUp);
                
                // Filter extrema to be strictly within the segment boundaries
                extrema = extrema.Where(e => e.BarIndex >= start.BarIndex && e.BarIndex <= end.BarIndex).ToList();

                if (extrema.Count >= 2)
                {
                    int segmentCount = extrema.Count - 1;

                    if (segmentCount == 3)
                    {
                        // 3 segments (A-B-C) means it's a zigzag correction, not an impulse
                        return false;
                    }
                    else if (segmentCount == 5)
                    {
                        // Exclude for now
                        return false;
                        double wave1 = Math.Abs(extrema[1].Value - extrema[0].Value);
                        double wave2 = Math.Abs(extrema[2].Value - extrema[1].Value);
                        double wave3 = Math.Abs(extrema[3].Value - extrema[2].Value);
                        double wave4 = Math.Abs(extrema[4].Value - extrema[3].Value);
                        double wave5 = Math.Abs(extrema[5].Value - extrema[4].Value);

                        // 1. Two corrections are proportional (one is not more than 1.5x the other)
                        bool correctionsProportional = wave4 <= 1.5 * wave2 && wave2 <= 1.5 * wave4;

                        // 2. Corrections do not overlap (Wave 4 extreme does not overlap Wave 1 extreme)
                        bool noOverlap = isUp 
                            ? extrema[4].Value > extrema[1].Value 
                            : extrema[4].Value < extrema[1].Value;

                        // 3. Wave 3 is not the shortest among impulse waves (1, 3, 5)
                        bool wave3NotShortest = wave3 > wave1 || wave3 > wave5;

                        return correctionsProportional && noOverlap && wave3NotShortest;
                    }
                    else if (segmentCount > 5)
                    {
                        // TODO: If there are more than 5 segments, it might be an extended impulse.
                        // For now, we do not consider this case and return false.
                        return false;
                    }
                }

                currentPeriod--;
            }

            // Reached minimum period (1) and segment count didn't increase (remained 1 segment)
            return true;
        }

        private static List<BarPoint> GetZigzagExtrema(BarPoint start, BarPoint end, int period, IBarsProvider barsProvider, bool isUp)
        {
            // If the overall movement is Up, the prior movement was Down, so we initialize isUpDirection to false
            // to catch the 'start' point as a Low.
            SimpleExtremumFinder finder = new SimpleExtremumFinder(period, barsProvider, !isUp);
            
            int startIndex = Math.Max(0, start.BarIndex - period);
            int endIndex = Math.Min(barsProvider.Count - 1, end.BarIndex + period);
            
            finder.Calculate(startIndex, endIndex);
            return finder.ToExtremaList();
        }
    }
}
