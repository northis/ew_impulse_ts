using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Detects whether an impulse movement is wave C of a flat pattern
    /// (FLAT_EXTENDED or FLAT_RUNNING).
    /// <para>
    /// Motivation: <see cref="ImpulseSetupFinder"/> finds impulses that may be
    /// waves C of flats.  Trading such impulses is risky because the flat
    /// already contains two corrective waves and the impulse may be the
    /// terminal leg.  This detector uses Elliott-wave rules with strict
    /// Fibonacci requirements to filter them out.
    /// </para>
    /// </summary>
    public static class FlatDetector
    {
        // ---- FLAT_EXTENDED strict C/A ratios (±10 %) -------------------------

        /// <summary>
        /// Allowed C/A ratios for FLAT_EXTENDED (EW_RULES §12: C/A ≥ 1.618).
        /// Each value is the centre; the window is [r/1.1, r*1.1].
        /// </summary>
        private static readonly double[] STRICT_C_TO_A_RATIOS =
            { 0.618, 1.0, 1.618, 2.618 };

        private const double RATIO_TOLERANCE = 1.1; // 10 %

        // ----------------------------------------------------------------------

        /// <summary>
        /// Checks whether the given impulse (<paramref name="startItem"/> →
        /// <paramref name="endItem"/>) is wave C of a flat.
        /// </summary>
        /// <param name="barsProvider">Source bars.</param>
        /// <param name="startItem">Start of the suspected impulse (wave C start).</param>
        /// <param name="endItem">End of the suspected impulse (wave C end).</param>
        /// <param name="edgeExtremum">
        /// The candle found by <c>IsInitialMovement</c> — marks the furthest the
        /// prior counter-move reached.
        /// </param>
        /// <param name="flatType">
        /// When the result is <c>true</c>, the detected flat model type.
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="startItem"/>→<paramref name="endItem"/>
        /// is wave C of a flat; otherwise <c>false</c>.
        /// </returns>
        public static bool IsFlatWaveC(
            IBarsProvider barsProvider,
            BarPoint startItem,
            BarPoint endItem,
            Candle edgeExtremum,
            out ElliottModelType? flatType)
        {
            flatType = null;

            if (barsProvider == null || startItem == null || endItem == null)
                return false;

            int edgeIndex = edgeExtremum?.Index ?? 0;
            int startIdx = Math.Max(edgeIndex, 0);
            int endIdx = endItem.BarIndex;

            if (endIdx - startIdx < 4) // need at least a few bars for A+B
                return false;

            // 1. Try FLAT_EXTENDED → search between edgeExtremum and endItem.
            if (TryDetectFlat(barsProvider, startIdx, endIdx, startItem, endItem,
                    extended: true, out flatType))
                return true;

            // 2. Try FLAT_RUNNING → extend search before edgeExtremum.
            int runStart = Math.Max(startIdx - (endIdx - startIdx) / 2, 0);
            if (runStart < startIdx &&
                TryDetectFlat(barsProvider, runStart, endIdx, startItem, endItem,
                    extended: false, out flatType))
                return true;

            return false;
        }

        /// <summary>
        /// Core detection logic for one window.
        /// </summary>
        private static bool TryDetectFlat(
            IBarsProvider barsProvider,
            int barFrom, int barTo,
            BarPoint startItem, BarPoint endItem,
            bool extended,
            out ElliottModelType? flatType)
        {
            flatType = null;

            // Build zigzag in the window.
            var optimizer = new DeviationOptimizer(barsProvider, barFrom, barTo, false);
            double dev = optimizer.FindOptimalDeviation();
            bool isUp = endItem.Value > startItem.Value;
            var finder = new SimpleExtremumFinder(dev, barsProvider, !isUp);
            finder.Calculate(barFrom, barTo);

            List<BarPoint> pivots = finder.ToExtremaList()
                .Where(p => p.BarIndex >= barFrom && p.BarIndex <= barTo)
                .ToList();

            if (pivots.Count < 4) // need at least 3 segments (A-B-C)
                return false;

            // Ensure the impulse endpoints are in the pivot list.
            if (pivots.All(p => p.BarIndex != startItem.BarIndex))
                pivots.Insert(0, startItem);
            if (pivots.All(p => p.BarIndex != endItem.BarIndex))
                pivots.Add(endItem);

            // Sort by bar index.
            pivots = pivots.OrderBy(p => p.BarIndex).ToList();

            // The last 3 segments should be A-B-C.
            // Find where startItem and endItem are in the list.
            int cStartIdx = pivots.FindIndex(p => p.BarIndex == startItem.BarIndex);
            int cEndIdx = pivots.FindIndex(p => p.BarIndex == endItem.BarIndex);

            if (cStartIdx < 2 || cEndIdx < cStartIdx + 1)
                return false;

            // Wave C = pivots[cStartIdx] → pivots[cEndIdx]
            // Wave B = pivots[cStartIdx-1] → pivots[cStartIdx]
            // Wave A = pivots[cStartIdx-2] → pivots[cStartIdx-1]
            int aIdx = cStartIdx - 2;
            int bIdx = cStartIdx - 1;

            double aLen = Math.Abs(pivots[bIdx].Value - pivots[aIdx].Value);
            double bLen = Math.Abs(pivots[cStartIdx].Value - pivots[bIdx].Value);
            double cLen = Math.Abs(pivots[cEndIdx].Value - pivots[cStartIdx].Value);

            if (aLen <= 0 || bLen <= 0 || cLen <= 0)
                return false;

            double s = isUp ? 1.0 : -1.0;
            double aStart = pivots[aIdx].Value; // where A began
            double aEnd = pivots[bIdx].Value;   // where A ended
            double bEnd = pivots[cStartIdx].Value; // where B ended
            double cEnd = pivots[cEndIdx].Value;   // where C ended

            // ---- Flat common rule: B must overshoot A's start ----
            // For a flat correcting upward (A up): B goes down past A's start.
            // s * (bEnd - aStart) < 0 means B went past origin.
            if (s * (bEnd - aStart) >= 0)
                return false; // B didn't overshoot — not a flat

            // ---- C must actually correct: C ends on the correction side of A's start ----
            if (extended)
            {
                // FLAT_EXTENDED: C must break beyond A's end.
                if (s * (cEnd - aEnd) <= 0)
                    return false;

                // Strict C/A check.
                double ca = cLen / aLen;
                if (!IsStrictRatio(ca))
                    return false;

                // B/A must be within fibo map bounds.
                double ba = bLen / aLen;
                if (ba < 0.786 * 0.9 || ba > 1.618 * 1.1)
                    return false;

                flatType = ElliottModelType.FLAT_EXTENDED;
                return true;
            }
            else
            {
                // FLAT_RUNNING: C corrects at least 38.2 % of A.
                double correction = s * (aStart - cEnd);
                if (correction < 0.382 * aLen - 1e-9)
                    return false;

                // C must still be on the correction side.
                if (s * (cEnd - aStart) >= 0)
                    return false; // C is on the non-correction side

                // Use B/A and C/A from standard fibo maps.
                double ba = bLen / aLen;
                if (ba < 0.786 * 0.9 || ba > 1.618 * 1.1)
                    return false;

                double ca = cLen / aLen;
                if (ca > 1.618 * 1.1)
                    return false; // C/A too large

                flatType = ElliottModelType.FLAT_RUNNING;
                return true;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="ratio"/> is within 10 % of
        /// one of the <see cref="STRICT_C_TO_A_RATIOS"/>.
        /// </summary>
        private static bool IsStrictRatio(double ratio)
        {
            foreach (double r in STRICT_C_TO_A_RATIOS)
            {
                double lo = r / RATIO_TOLERANCE;
                double hi = r * RATIO_TOLERANCE;
                if (ratio >= lo && ratio <= hi)
                    return true;
            }
            return false;
        }

        // ----------- internal (test-only) API for direct-pivot testing ----------

        /// <summary>
        /// Same core logic as <see cref="TryDetectFlat"/> but operates on pre-built
        /// pivots.  Visible to tests only.
        /// </summary>
        public static bool TryDetectFromPivots(
            List<BarPoint> pivots, bool isUp, bool extended,
            out ElliottModelType? flatType)
        {
            flatType = null;
            if (pivots.Count < 4)
                return false;

            double aLen = Math.Abs(pivots[1].Value - pivots[0].Value);
            double bLen = Math.Abs(pivots[2].Value - pivots[1].Value);
            double cLen = Math.Abs(pivots[3].Value - pivots[2].Value);
            if (aLen <= 0 || bLen <= 0 || cLen <= 0)
                return false;

            double s = isUp ? 1.0 : -1.0;
            double aStart = pivots[0].Value;
            double aEnd   = pivots[1].Value;
            double bEnd   = pivots[2].Value;
            double cEnd   = pivots[3].Value;

            // B must overshoot A's start.
            if (s * (bEnd - aStart) >= 0)
                return false;

            if (extended)
            {
                if (s * (cEnd - aEnd) <= 0) return false;
                double ca = cLen / aLen;
                if (!IsStrictRatio(ca)) return false;
                double ba = bLen / aLen;
                if (ba < 0.786 * 0.9 || ba > 1.618 * 1.1) return false;
                flatType = ElliottModelType.FLAT_EXTENDED;
                return true;
            }
            else
            {
                double correction = s * (aStart - cEnd);
                if (correction < 0.382 * aLen - 1e-9) return false;
                if (s * (cEnd - aStart) >= 0) return false;
                double ba = bLen / aLen;
                if (ba < 0.786 * 0.9 || ba > 1.618 * 1.1) return false;
                double ca = cLen / aLen;
                if (ca > 1.618 * 1.1) return false;
                flatType = ElliottModelType.FLAT_RUNNING;
                return true;
            }
        }
    }
}
