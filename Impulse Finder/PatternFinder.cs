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
        private const int IMPULSE_EXTREMA_INDEX = IMPULSE_EXTREMA_COUNT - 1;
        private const int IMPULSE_EXTREMA_STEP_COUNT = 4;
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
        /// <param name="extrema">The extrema.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(Extremum[] extrema)
        {
            int count = extrema.Length;
            if (count < SIMPLE_EXTREMA_COUNT)
            {
                return false;
            }

            if (count == SIMPLE_EXTREMA_COUNT)
            {
                return true;
            }

            if (count == ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }

            Extremum firstItem = extrema[0];
            Extremum lastItem = extrema[^1];
            bool isUp = lastItem.Value > firstItem.Value;

            int countRest = count - IMPULSE_EXTREMA_COUNT;

            int? GetCorrectionEndIndex(int prevImpulseEndIndex)
            {
                for (int j = prevImpulseEndIndex + 1; j < count; j++)
                {
                    if (isUp && extrema[j] > extrema[prevImpulseEndIndex] ||
                        !isUp && extrema[j] < extrema[prevImpulseEndIndex])
                    {
                        Extremum[] secExtrema = extrema[(prevImpulseEndIndex + 1)..j];
                        if (secExtrema.Length < SIMPLE_EXTREMA_COUNT)
                        {
                            break;
                        }

                        return Array.IndexOf(extrema,
                            isUp ? secExtrema.Min() : secExtrema.Max());
                    }
                }

                return null;
            }

            if (countRest == 0 /*|| countRest == 1*/)
            {

            } else if (countRest % IMPULSE_EXTREMA_STEP_COUNT == 0)
            {
                System.Diagnostics.Debugger.Launch();
                for (int i = 1; i < count; i++)
                {
                    //int endIndex = i + IMPULSE_EXTREMA_INDEX;
                    //if (endIndex >= count)
                    //{
                    //    return false;
                    //}

                    if (i % 2 == 0)// We don't want to consider corrections
                    {
                        continue;
                    }

                    int ni = i + 1;//TODO what the fuck is this?
                    bool isFirstWaveImpulse = IsSimpleImpulse(extrema[..ni]);
                    if (!isFirstWaveImpulse)
                    {
                        continue;
                    }

                    // 2nd wave end candidate
                    int? secondEndIndex = GetCorrectionEndIndex(i);
                    // We can check here the 2nd wave for zigzag/flat/combination
                    if (!secondEndIndex.HasValue)
                    {
                        continue;
                    }

                    int thirdStartIndex = secondEndIndex.Value;
                    for (int j = thirdStartIndex; j < count; j++)
                    {
                        if ((j - thirdStartIndex) % 2 == 0)
                        {
                            continue;
                        }

                        int nj = j + 1;
                        bool isThirdWaveImpulse = IsSimpleImpulse(
                            extrema[thirdStartIndex..nj]);
                        if (!isThirdWaveImpulse)
                        {
                            continue;
                        }

                        // We can support here a triangle/flat/zigzag check
                        // For flat and triangle we cannot use so simple
                        // logic with Max() and Min().
                        int? forthEndIndex = GetCorrectionEndIndex(j);
                        if (!forthEndIndex.HasValue)
                        {
                            continue;
                        }

                        bool isFifthWaveImpulse = IsSimpleImpulse(
                            extrema[forthEndIndex.Value..]);
                        if (!isFifthWaveImpulse)
                        {
                            continue;
                        }
                        // If we are here, we've just found an impulse.
                        // Lets check all the rules:

                        var builtExtrema = new Extremum[IMPULSE_EXTREMA_STEP_COUNT];
                        builtExtrema[0] = extrema[0];
                        builtExtrema[1] = extrema[i];
                        builtExtrema[2] = extrema[thirdStartIndex];
                        builtExtrema[3] = extrema[j];
                        builtExtrema[4] = extrema[forthEndIndex.Value];
                        builtExtrema[IMPULSE_EXTREMA_INDEX] = extrema[^1];

                        bool isImpulse = IsSimpleImpulse(builtExtrema);
                        if (isImpulse)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            else
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

            var minorExtremumFinder = new ExtremumFinder(m_Deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(dateStart, dateEnd);
            Extremum[] extrema = minorExtremumFinder.ToExtremaArray();
            bool isSimpleImpulse = IsSimpleImpulse(extrema);
            if (isSimpleImpulse)
            {
                return true;
            }

            return false;
        }
    }
}
