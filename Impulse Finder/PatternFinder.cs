using System;
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
            Extremum[] extrema = minorExtremumFinder.ToExtremaArray();
            int count = extrema.Length;

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
        /// <param name="dateStart">The date start.</param>
        /// <param name="dateEnd">The date end.</param>
        /// <param name="deviation">The deviation percent</param>
        /// <param name="allowSimple">True if we treat count <see cref="SIMPLE_EXTREMA_COUNT"/>-movement as impulse.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(
            DateTime dateStart, DateTime dateEnd, 
            double deviation, bool allowSimple = true)
        {
            var minorExtremumFinder = new ExtremumFinder(deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(dateStart, dateEnd);
            Extremum[] extrema = minorExtremumFinder.ToExtremaArray();

            int count = extrema.Length;
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
            Extremum lastItem = extrema[^1];
            bool isUp = lastItem.Value > firstItem.Value;
            int countRest = count - IMPULSE_EXTREMA_COUNT;
            if (countRest != 0 /*&& countRest != 1*/)
            {
                return false;
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
            if (isUp && firstWaveEnd.Value >= fourthWaveEnd.Value ||
                !isUp && firstWaveEnd.Value <= fourthWaveEnd.Value)
            {
                return false;
            }

            double firstWaveLength = (isUp ? 1 : -1) *
                                     (firstWaveEnd.Value - firstItem.Value);

            double thirdWaveLength = (isUp ? 1 : -1) *
                                     (thirdWaveEnd.Value - secondWaveEnd.Value);

            double fifthWaveLength = (isUp ? 1 : -1) *
                                     (fifthWaveEnd.Value - fourthWaveEnd.Value);

            if (firstWaveLength <= 0 ||
                thirdWaveLength <= 0 ||
                fifthWaveLength <= 0)
            {
                return false;
            }

            // Check the 3rd wave length
            if (thirdWaveLength < firstWaveLength &&
                thirdWaveLength < fifthWaveLength)
            {
                return false;
            }

            //for (double dv = deviation * Helper.DEVIATION_START_RATIO * 0.5;
            //     dv >= Helper.DEVIATION_LOW;
            //     dv -= Helper.DEVIATION_STEP)
            //{
            //    if (IsSimpleImpulse(firstItem.OpenTime, firstWaveEnd.CloseTime, dv) &&
            //        IsSimpleImpulse(secondWaveEnd.OpenTime, thirdWaveEnd.CloseTime, dv) &&
            //        IsSimpleImpulse(fourthWaveEnd.OpenTime, fifthWaveEnd.CloseTime, dv))
            //    {
            //        return true;
            //    }
            //}

            return true;
        }
        
        /// <summary>
        /// Determines whether the interval between the dates is an impulse.
        /// </summary>
        /// <param name="dateStart">The date start.</param>
        /// <param name="dateEnd">The date end.</param>
        /// <returns>
        ///   <c>true</c> if the interval is impulse; otherwise, <c>false</c>.
        /// </returns>
        public bool IsImpulse(DateTime dateStart, DateTime dateEnd)
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

            for (double dv = m_Deviation; 
                 dv >= Helper.DEVIATION_LOW;
                 dv -= Helper.DEVIATION_STEP)
            {

                bool isSimpleImpulse = IsSimpleImpulse(dateStart, dateEnd, dv, false);
                if (isSimpleImpulse)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
