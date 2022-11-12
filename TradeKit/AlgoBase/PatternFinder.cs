using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class PatternFinder
    {
        private readonly int m_ZoomMin;
        private readonly double m_CorrectionAllowancePercent;
        private readonly IBarsProvider m_BarsProvider;
        private const int IMPULSE_EXTREMA_COUNT = 6;
        private const int SIMPLE_EXTREMA_COUNT = 2;
        private const int ZIGZAG_EXTREMA_COUNT = 4;
        private const double FIBONACCI = 1.618;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternFinder"/> class.
        /// </summary>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="zoomMin">The zoom minimum.</param>
        public PatternFinder(double correctionAllowancePercent, IBarsProvider barsProvider, int zoomMin)
        {
            m_ZoomMin = zoomMin;
            m_CorrectionAllowancePercent = correctionAllowancePercent;
            m_BarsProvider = barsProvider;
        }
        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="devStartMax">The deviation percent - start of the range</param>
        /// <param name="devEndMin">The deviation percent - end of the range</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        public bool IsZigzag(BarPoint start, BarPoint end, int devStartMax, int devEndMin)
        {
            for (int dv = devStartMax; dv >= devEndMin; dv -= Helper.ZOOM_STEP)
            {
                if (IsZigzag(start, end, dv))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsDoubleZigzag(BarPoint start, BarPoint end, int devStartMax, int devEndMin)
        {
            bool isUp = start < end;
            for (int dv = devStartMax; dv >= devEndMin; dv -= Helper.ZOOM_STEP)
            {
                List<BarPoint> extrema = GetNormalizedExtrema(start, end, dv);
                int count = extrema.Count;

                if (count < ZIGZAG_EXTREMA_COUNT)
                {
                    continue;
                }

                bool isSimpleOverlap = isUp && extrema[1] > extrema[^2] ||
                                       !isUp && extrema[1] < extrema[^2];
                
                if (isSimpleOverlap &&
                    IsZigzag(extrema[0], extrema[1], dv, devEndMin) && 
                    IsZigzag(extrema[^2], extrema[^1], dv, devEndMin))
                {
                    return true;
                }
            }

            return false;
        }

        private List<BarPoint> GetNormalizedExtrema(BarPoint start, BarPoint end, int scale)
        {
            bool isUp = start < end;
            var minorExtremumFinder = new ExtremumFinder(scale, m_BarsProvider, isUp);
            minorExtremumFinder.Calculate(start.OpenTime, end.OpenTime);
            List<BarPoint> extrema = minorExtremumFinder.ToExtremaList();

            NormalizeExtrema(extrema, start, end);
            return extrema;
        }

        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="scale">The deviation percent</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        public bool IsZigzag(BarPoint start, BarPoint end, int scale)
        {
            List<BarPoint> extrema = GetNormalizedExtrema(start, end, scale);
            int count = extrema.Count;

            if (count < ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }

            if (count == ZIGZAG_EXTREMA_COUNT)
            {
                return true;
            }

            BarPoint first = extrema[0];
            BarPoint second = extrema[1];
            BarPoint subLast = extrema[^2];
            BarPoint last = extrema[^1];
            bool isUp = first < last;

            if (isUp && second > subLast || !isUp && second < subLast)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Normalizes the extrema.
        /// </summary>
        /// <param name="extrema">The extrema.</param>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        private void NormalizeExtrema(List<BarPoint> extrema, BarPoint start, BarPoint end)
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

            if (extrema.Count < ZIGZAG_EXTREMA_COUNT)
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
        /// Determines whether the specified extrema is an impulse.
        /// Simple impulse has <see cref="IMPULSE_EXTREMA_COUNT"/> extrema and 5 waves
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="deviation">The deviation percent</param>
        /// <param name="extrema">The impulse waves found.</param>
        /// <param name="allowSimple">True if we treat count <see cref="SIMPLE_EXTREMA_COUNT"/>-movement as impulse.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsImpulseInner(
            BarPoint start, BarPoint end,
            int deviation, out List<BarPoint> extrema,
            bool allowSimple = true)
        {
            extrema = GetNormalizedExtrema(start, end, deviation);

            int count = extrema.Count;
            if (count < SIMPLE_EXTREMA_COUNT)
            {
                return false;
            }

            if (count == SIMPLE_EXTREMA_COUNT)
            {
                return allowSimple;
            }

            if (count == ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }

            BarPoint firstItem = extrema[0];
            BarPoint firstWaveEnd;
            BarPoint secondWaveEnd;
            BarPoint thirdWaveEnd;
            BarPoint fourthWaveEnd;
            BarPoint fifthWaveEnd= extrema[^1];
            int countRest = count - IMPULSE_EXTREMA_COUNT;

            bool CheckWaves()
            {
                double secondWaveDuration = (secondWaveEnd.OpenTime - firstWaveEnd.OpenTime).TotalSeconds;
                double fourthWaveDuration = (fourthWaveEnd.OpenTime - thirdWaveEnd.OpenTime).TotalSeconds;
                if (secondWaveDuration <= 0 || fourthWaveDuration <= 0)
                {
                    return false;
                }

                // Check harmony between the 2nd and the 4th waves 
                double correctionRatio = fourthWaveDuration / secondWaveDuration;
                if (correctionRatio * 100 > m_CorrectionAllowancePercent ||
                    correctionRatio < 100d / m_CorrectionAllowancePercent)
                {
                    return false;
                }

                bool isImpulseUp = start.Value < end.Value;
                // Check the overlap rule
                if (isImpulseUp && firstWaveEnd.Value >= fourthWaveEnd.Value ||
                    !isImpulseUp && firstWaveEnd.Value <= fourthWaveEnd.Value)
                {
                    return false;
                }

                double firstWaveLength = (isImpulseUp ? 1 : -1) *
                                         (firstWaveEnd.Value - firstItem.Value);

                double thirdWaveLength = (isImpulseUp ? 1 : -1) *
                                         (thirdWaveEnd.Value - secondWaveEnd.Value);

                double fifthWaveLength = (isImpulseUp ? 1 : -1) *
                                         (fifthWaveEnd.Value - fourthWaveEnd.Value);

                if (firstWaveLength <= 0 || thirdWaveLength <= 0 || fifthWaveLength <= 0)
                {
                    return false;
                }

                if (Math.Abs(thirdWaveEnd.Value - fifthWaveEnd.Value) / fifthWaveLength <
                    Helper.THIRD_FIFTH_BREAK_MIN_RATIO)
                {
                    // We don't want to use impulse with short 5th wave, cause this is can be
                    // not accurate data from the market data provider.
                    return false;
                }

                if (Math.Abs(secondWaveEnd - firstItem) / firstWaveLength < Helper.SECOND_WAVE_PULLBACK_MIN_RATIO)
                {
                    // The 2nd wave is too close to the impulse beginning
                    return false;
                }

                // Check the 3rd wave length
                if (thirdWaveLength < firstWaveLength &&
                    thirdWaveLength < fifthWaveLength)
                {
                    return false;
                }

                bool hasExtendedWave = firstWaveLength > thirdWaveLength * FIBONACCI ||
                                       thirdWaveLength > firstWaveLength * FIBONACCI ||
                                       fifthWaveLength > thirdWaveLength * FIBONACCI ||
                                       thirdWaveLength > fifthWaveLength * FIBONACCI ||
                                       firstWaveLength > fifthWaveLength * FIBONACCI ||
                                       fifthWaveLength > firstWaveLength * FIBONACCI;

                if (!hasExtendedWave)
                {
                    Logger.Write("No extended wave in impulse");
                    return false;
                }

                for (int dv = deviation; dv >= m_ZoomMin; dv -= Helper.ZOOM_STEP)
                {
                    if (IsZigzag(firstItem, firstWaveEnd, dv) ||
                        IsZigzag(secondWaveEnd, thirdWaveEnd, dv) ||
                        IsZigzag(fourthWaveEnd, fifthWaveEnd, dv))
                    {
                        return false;
                    }
                }

                bool ok1 = false, ok3 = false, ok5 = false;
                for (int dv = deviation; dv >= m_ZoomMin; dv -= Helper.ZOOM_STEP)
                {
                    if (IsImpulseInner(firstItem, firstWaveEnd, dv, out _))
                    {
                        ok1 = true;
                    }

                    if (IsImpulseInner(secondWaveEnd, thirdWaveEnd, dv, out _))
                    {
                        ok3 = true;
                    }

                    if (IsImpulseInner(fourthWaveEnd, fifthWaveEnd, dv, out _))
                    {
                        ok5 = true;
                    }

                    if (ok1 && ok3 && ok5)
                    {
                        return true;
                    }
                }

                return true;
            }
            
            if (countRest == 0)
            {
                firstWaveEnd = extrema[1];
                secondWaveEnd = extrema[2];
                thirdWaveEnd = extrema[3];
                fourthWaveEnd = extrema[4];
                fifthWaveEnd = extrema[5];
                return CheckWaves();
            }

            if (countRest < ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the interval between the dates is an impulse.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="deviation">The deviation to use.</param>
        /// <param name="extrema">The impulse waves found.</param>
        /// <returns>
        ///   <c>true</c> if the interval is impulse; otherwise, <c>false</c>.
        /// </returns>
        public bool IsImpulse(BarPoint start, BarPoint end, int deviation, out List<BarPoint> extrema)
        {
            extrema = null;
            if (IsDoubleZigzag(start, end, deviation, m_ZoomMin))
            {
                return false;
            }

            if (IsZigzag(start, end, deviation, m_ZoomMin))
            {
                return false;
            }

            return true; // IsImpulseInner(start, end, deviation, out extrema);

            //for (int dv = deviation; dv >= m_ZoomMin; dv -= Helper.ZOOM_STEP)
            //{
            //    // Debugger.Launch();

            //    if (IsImpulseInner(start, end, dv, out extrema, false))
            //    {
            //        return true;
            //    }
            //}

            //return false;
        }
    }
}
