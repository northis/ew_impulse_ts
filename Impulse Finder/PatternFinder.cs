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
        private readonly List<IBarsProvider> m_BarsProviders;
        private const int IMPULSE_EXTREMA_COUNT = 6;
        private const int SIMPLE_IMPULSE_EXTREMA_COUNT = 2;
        private const double MINOR_DEVIATION_START = 0.5;// from 0.1 to 1
        private const double MINOR_DEVIATION_STEP = 0.1;// from 0.1 to 1
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
            m_BarsProviders = barsProviders;

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
        }

        private List<double> MinorDeviations { get; }

         /// <summary>
        /// Checks the impulse.
        /// </summary>
        /// <param name="extremaList">The extrema list.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        private bool CheckImpulse(
            List<Extremum[]> extremaList, Extremum start, Extremum end)
        {
            List<Extremum[]> minorExtremaSet = extremaList.Skip(1).ToList();
            Extremum[] minorExtrema = minorExtremaSet.FirstOrDefault();
            if (minorExtrema == null)
            {
                // If we are here, so there are no minor extrema and
                // we've found an impulse
                return true;
            }
            
            Extremum[] minorWave = minorExtrema
                .SkipWhile(a => a.OpenTime < start.OpenTime)
                .TakeWhile(a => a.OpenTime < end.CloseTime)
                .ToArray();

            minorExtremaSet.RemoveAt(0);
            minorExtremaSet.Insert(0, minorWave);

            bool isWaveImpulse = IsSimpleImpulse(minorExtremaSet);
            return isWaveImpulse;
        }

        /// <summary>
        /// Determines whether the specified extrema is an simple impulse.
        /// Simple impulse has <see cref="IMPULSE_EXTREMA_COUNT"/> extrema and 5 waves
        /// </summary>
        /// <param name="extremaList">The extrema - list of sorted arrays.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(List<Extremum[]> extremaList)
        {
            Extremum[] extrema = extremaList[0];
            int count = extrema.Length;

            if (count == SIMPLE_IMPULSE_EXTREMA_COUNT && extremaList.Count == 1)
            {
                bool res = CheckImpulse(extremaList, extrema[0], extrema[1]);
                return res;
            }

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
            if (thirdWaveLength < firstWaveLength ||
                thirdWaveLength < fifthWaveLength)
            {
                return false;
            }
            

            // Let's look closer to the impulse waves 1, 3 and 5 using
            // the minor extrema provided
            if (!CheckImpulse(extremaList, firstItem, firstWaveEnd) 
                || !CheckImpulse(extremaList, secondWaveEnd, thirdWaveEnd) 
                || !CheckImpulse(extremaList, fourthWaveEnd, fifthWaveEnd))
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
            foreach (double minorDeviation in MinorDeviations)
            {
                var extremaSet = new List<Extremum[]>();
                foreach (IBarsProvider barsProvider in m_BarsProviders)
                {
                    var minorExtremumFinder = new ExtremumFinder(
                        minorDeviation, barsProvider);
                    minorExtremumFinder.Calculate(dateStart, dateEnd);
                    extremaSet.Add(minorExtremumFinder.ToExtremaArray());
                }
                bool isSimpleImpulse = IsSimpleImpulse(extremaSet);
                if (isSimpleImpulse)
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}
