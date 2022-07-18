using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TradeKit
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
        public bool IsZigzag(Extremum start, Extremum end, int devStartMax, int devEndMin)
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

        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="scale">The deviation percent</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        public bool IsZigzag(Extremum start, Extremum end, int scale)
        {
            var minorExtremumFinder = new ExtremumFinder(scale, m_BarsProvider);
            minorExtremumFinder.Calculate(start.OpenTime, end.OpenTime);
            List<Extremum> extrema = minorExtremumFinder.ToExtremaList();

            NormalizeExtrema(extrema, start, end);
            int count = extrema.Count;

            if (count < ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }

            if (count == ZIGZAG_EXTREMA_COUNT)
            {
                return true;
            }

            Extremum first = extrema[0];
            Extremum second = extrema[1];
            Extremum subLast = extrema[^2];
            Extremum last = extrema[^1];
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
        private void NormalizeExtrema(List<Extremum> extrema, Extremum start, Extremum end)
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
            Extremum start, Extremum end,
            int deviation, out List<Extremum> extrema,
            bool allowSimple = true)
        {
            var minorExtremumFinder = new ExtremumFinder(deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(start.OpenTime, end.OpenTime);
            extrema = minorExtremumFinder.ToExtremaList();
            NormalizeExtrema(extrema, start, end);

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

            Extremum firstItem = extrema[0];
            Extremum firstWaveEnd;
            Extremum secondWaveEnd;
            Extremum thirdWaveEnd;
            Extremum fourthWaveEnd;
            Extremum fifthWaveEnd= extrema[^1];
            int countRest = count - IMPULSE_EXTREMA_COUNT;

            bool CheckWaves()
            {
                double secondWaveDuration = (secondWaveEnd.OpenTime - firstWaveEnd.OpenTime).TotalSeconds;
                double fourthWaveDuration = (fourthWaveEnd.OpenTime - thirdWaveEnd.OpenTime).TotalSeconds;
                if (secondWaveDuration <= 0 || fourthWaveDuration <= 0)
                {
                    return false;
                }

                double minSeconds = Helper.CORRECTION_BAR_MIN *
                                    TimeFrameHelper.TimeFrames[secondWaveEnd.BarTimeFrame].TimeSpan.TotalSeconds;

                if (secondWaveDuration < minSeconds)
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

                // Check the 3rd wave length
                if (thirdWaveLength < firstWaveLength &&
                    thirdWaveLength < fifthWaveLength)
                {
                    return false;
                }
                
                for (int dv = deviation; dv  >= m_ZoomMin; dv -= Helper.ZOOM_STEP)
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
                    
                    if(IsImpulseInner(fourthWaveEnd, fifthWaveEnd, dv, out _))
                    {
                        ok5 = true;
                    }

                    if (ok1 && ok3 && ok5)
                    {
                        return true;
                    }
                }

                return false;
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
        public bool IsImpulse(Extremum start, Extremum end, int deviation, out List<Extremum> extrema)
        {
            return IsImpulseInner(start, end, m_ZoomMin, out extrema);
        }
    }
}
