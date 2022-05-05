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
        private const int IMPULSE_EXTREMA_COUNT = 6;
        private const int ZIGZAG_EXTREMA_COUNT = 4;
        private const double MINOR_DEVIATION_START = 0.1;// from 0.1 to 1
        private const double MINOR_DEVIATION_STEP = 0.01;// from 0.1 to 1
        // If we have m_DeviationPercent == 0.2, MINOR_DEVIATION_START ==0.5
        // and MINOR_DEVIATION_STEP == 0.1 so start minor deviation is = 0.2*0.5 = 0.1,
        // and step every (0.2-0.1)*0.1 => 0.1, 0.11, 0.12, 0.13, ..., 0.19, 0.2

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternFinder"/> class.
        /// </summary>
        /// <param name="deviationPercent">The deviation percent.</param>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="barsProviders">The bars providers.</param>
        public PatternFinder(double correctionAllowancePercent,
            double deviationPercent,
            List<IBarsProvider> barsProviders)
        {
            m_CorrectionAllowancePercent = correctionAllowancePercent;

            double startDeviation = deviationPercent * MINOR_DEVIATION_START;
            double step = (deviationPercent - startDeviation)* MINOR_DEVIATION_STEP;
            double currentDeviation = startDeviation;

            MinorDeviations = new List<double>();
            do
            {
                MinorDeviations.Add(currentDeviation);
                currentDeviation += step;
            } while (currentDeviation < deviationPercent);

            MinorDeviations.Reverse();
            if (barsProviders.Count != 2)
            {
                throw new NotSupportedException(
                    "We support two bar providers only - for two time frames");
            }

            FirstBars = barsProviders[0];
            SecondBars = barsProviders[1];
        }

        private List<double> MinorDeviations { get; }
        
        IBarsProvider FirstBars { get; }
        IBarsProvider SecondBars { get; }

        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start of the interval.</param>
        /// <param name="end">The end of the interval.</param>
        /// <param name="deviations">The deviations to analyze.</param>
        /// <param name="provider">The provider.</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        private bool IsZigzag(
            DateTime start, DateTime end, 
            List<double> deviations, IBarsProvider provider)
        {
            foreach (var deviation in deviations)
            {
                var minorExtremumFinder =
                    new ExtremumFinder(deviation, provider);
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
            }

            return false;
        }

        /// <summary>
        /// Determines whether the specified extrema is an simple impulse.
        /// Simple impulse has <see cref="IMPULSE_EXTREMA_COUNT"/> extrema and 5 waves
        /// </summary>
        /// <param name="deviation">The deviation we should check the impulse with.</param>
        /// <param name="dateStart">The date start.</param>
        /// <param name="dateEnd">The date end.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(
            double deviation, DateTime dateStart, DateTime dateEnd)
        {
            var minorExtremumFinder =
                new ExtremumFinder(deviation, FirstBars);
            minorExtremumFinder.Calculate(dateStart, dateEnd);
            Extremum[] extrema = minorExtremumFinder.ToExtremaArray();
            int count = extrema.Length;

            if (count != IMPULSE_EXTREMA_COUNT &&
                count != IMPULSE_EXTREMA_COUNT + 1)
                // support 10, 14, 18 as well with a recursive call maybe
            {
                return false;
            }

            Extremum firstItem = extrema[0];
            Extremum lastItem = extrema[count - 1];
            bool isUp = lastItem.Value > firstItem.Value;
            
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

            // System.Diagnostics.Debugger.Launch();
            List<double> minorDeviations = MinorDeviations
                .SkipWhile(a => a >= deviation)
                .ToList();
            
            // Let's look closer to the impulse waves 1, 3 and 5.
            // We shouldn't pass zigzags in it
            if (IsZigzag(firstItem.OpenTime, firstWaveEnd.CloseTime,
                    minorDeviations, SecondBars) 
                || IsZigzag(secondWaveEnd.OpenTime, thirdWaveEnd.CloseTime,
                    minorDeviations, SecondBars)
                || IsZigzag(fourthWaveEnd.OpenTime, fifthWaveEnd.CloseTime,
                    minorDeviations, SecondBars))
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
            if (IsZigzag(dateStart, dateEnd, MinorDeviations, FirstBars))
            {
                return false;
            }

            foreach (double minorDeviation in MinorDeviations)
            {
                bool isSimpleImpulse = IsSimpleImpulse(
                    minorDeviation, dateStart, dateEnd);
                if (isSimpleImpulse)
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}
