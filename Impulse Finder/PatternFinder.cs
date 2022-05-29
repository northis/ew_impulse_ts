using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class PatternFinder
    {
        private readonly double m_CorrectionAllowancePercent;
        private readonly double m_Deviation;
        private readonly IBarsProvider m_BarsProvider;
        private const int IMPULSE_EXTREMA_COUNT = 6;
        private const int SIMPLE_EXTREMA_COUNT = 2;
        private const int ZIGZAG_EXTREMA_COUNT = 4;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternFinder"/> class.
        /// </summary>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="deviation">The deviation.</param>
        /// <param name="barsProvider">The bars provider.</param>
        public PatternFinder(double correctionAllowancePercent,
            double deviation,
            IBarsProvider barsProvider)
        {
            m_CorrectionAllowancePercent = correctionAllowancePercent;
            m_Deviation = deviation;
            m_BarsProvider = barsProvider;
        }

        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start of the interval.</param>
        /// <param name="end">The end of the interval.</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        private bool IsZigzag(DateTime start, DateTime end)
        {
            var minorExtremumFinder =
                new ExtremumFinder(m_Deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(start, end);
            List<Extremum> extrema = minorExtremumFinder.ToExtremaList();
            int count = extrema.Count;

            if (count == IMPULSE_EXTREMA_COUNT ||
                count == IMPULSE_EXTREMA_COUNT + 1)
            {
                return false;
            }

            if (count == ZIGZAG_EXTREMA_COUNT ||
                count == ZIGZAG_EXTREMA_COUNT + 1)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified extrema is an simple impulse.
        /// Simple impulse has <see cref="IMPULSE_EXTREMA_COUNT"/> extrema and 5 waves
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="deviation">The deviation percent</param>
        /// <param name="extrema">The impulse waves found.</param>
        /// <param name="isImpulseUp">if set to <c>true</c> than impulse should go up.</param>
        /// <param name="allowSimple">True if we treat count <see cref="SIMPLE_EXTREMA_COUNT"/>-movement as impulse.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(
            Extremum start, Extremum end, 
            double deviation, out List<Extremum> extrema, bool isImpulseUp, 
            bool allowSimple = true)
        {
            var minorExtremumFinder = new ExtremumFinder(deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(start.OpenTime, end.OpenTime);
            extrema = minorExtremumFinder.ToExtremaList();

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
            int countRest = count - IMPULSE_EXTREMA_COUNT;
            if (countRest != 0)
            {
                return false;
                //if (countRest < ZIGZAG_EXTREMA_COUNT)
                //{
                //    return false;
                //}

                //double innerDeviation = deviation + Helper.DEVIATION_STEP;
                //if (innerDeviation > Helper.DEVIATION_MAX)
                //{
                //    return false;
                //}

                //bool innerCheck = IsSimpleImpulse(
                //    dateStart, dateEnd, innerDeviation, out extrema, isImpulseUp, false);
                //return innerCheck;
            }

            Extremum firstWaveEnd = extrema[1];
            Extremum secondWaveEnd = extrema[2];
            Extremum thirdWaveEnd = extrema[3];
            Extremum fourthWaveEnd = extrema[4];
            Extremum fifthWaveEnd = extrema[5];

            double secondWaveDuration = (secondWaveEnd.OpenTime - firstWaveEnd.OpenTime).TotalSeconds;
            double fourthWaveDuration = (fourthWaveEnd.OpenTime - thirdWaveEnd.OpenTime).TotalSeconds;
            if (secondWaveDuration <= 0 || fourthWaveDuration <= 0)
            {
                return false;
            }

            // Check harmony between 2nd and 4th waves 
            double correctionRatio = fourthWaveDuration / secondWaveDuration;
            if (correctionRatio * 100 > m_CorrectionAllowancePercent ||
                correctionRatio < 100d / m_CorrectionAllowancePercent)
            {
                return false;
            }

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

            // Check the 3rd wave length
            if (thirdWaveLength < firstWaveLength &&
                thirdWaveLength < fifthWaveLength)
            {
                return false;
            }

            for (double dv = deviation;
                 dv >= Helper.DEVIATION_LOW;
                 dv -= Helper.DEVIATION_STEP)
            {
                if (IsSimpleImpulse(
                        firstItem, firstWaveEnd, dv, out _, isImpulseUp, false) &&
                    IsSimpleImpulse(
                        secondWaveEnd, thirdWaveEnd, dv, out _, isImpulseUp, false) &&
                    IsSimpleImpulse(
                        fourthWaveEnd, fifthWaveEnd, dv, out _, isImpulseUp, false))
                {
                    return true;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether the interval between the dates is an impulse.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="isImpulseUp">if set to <c>true</c> than impulse should go up.</param>
        /// <param name="extrema">The impulse waves found.</param>
        /// <returns>
        ///   <c>true</c> if the interval is impulse; otherwise, <c>false</c>.
        /// </returns>
        public bool IsImpulse(
            Extremum start, Extremum end, bool isImpulseUp, out List<Extremum> extrema)
        {
            //bool isZigzag = IsZigzag(dateStart, dateEnd);

            //// Let's look closer to the impulse waves 1, 3 and 5.
            //// We shouldn't pass zigzags in it
            //if (IsZigzag(firstItem.OpenTime, firstWaveEnd.CloseTime)
            //    || IsZigzag(secondWaveEnd.OpenTime, thirdWaveEnd.CloseTime)
            //    || IsZigzag(fourthWaveEnd.OpenTime, fifthWaveEnd.CloseTime))
            //{
            //    return false;
            //}

            extrema = null;
            for (double dv = m_Deviation; 
                 dv >= Helper.DEVIATION_LOW;
                 dv -= Helper.DEVIATION_STEP)
            {
                bool isSimpleImpulse = IsSimpleImpulse(
                    start, end, dv, out extrema, isImpulseUp, false);
                if (isSimpleImpulse)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
