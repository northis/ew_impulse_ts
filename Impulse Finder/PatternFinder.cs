using System.Collections.Generic;
using System.Linq;

namespace cAlgo
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public static class PatternFinder
    {
        /// <summary>
        /// Determines whether the specified extrema is an simple impulse.
        /// Simple impulse has 6 extrema and 5 waves
        /// </summary>
        /// <param name="extrema">The extrema collection.</param>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsSimpleImpulse(
            SortedDictionary<int, double> extrema, int correctionAllowancePercent)
        {
            int count = extrema.Count;
            if (count != 6)// support 10, 14, 18 as well with a recursive call maybe
            {
                return false;
            }

            KeyValuePair<int, double> firstItem = extrema.First();
            KeyValuePair<int, double> lastItem = extrema.Last();
            bool isUp = lastItem.Value > firstItem.Value;
            
            KeyValuePair<int, double> firstWaveEnd = extrema.ElementAt(1);
            KeyValuePair<int, double> secondWaveEnd = extrema.ElementAt(2);
            KeyValuePair<int, double> thirdWaveEnd = extrema.ElementAt(3);
            KeyValuePair<int, double> fourthWaveEnd = extrema.ElementAt(4);
            KeyValuePair<int, double> fifthWaveEnd = extrema.ElementAt(5);

            int secondWaveDuration = secondWaveEnd.Key - firstWaveEnd.Key;
            int fourthWaveDuration = fourthWaveEnd.Key - thirdWaveEnd.Key;
            if (secondWaveDuration <= 0 || fourthWaveDuration <= 0)
            {
                return false;
            }

            // Check harmony between 2nd and 4th waves 
            double correctionRatio = (double)fourthWaveDuration / secondWaveDuration;
            if (correctionRatio * 100 > correctionAllowancePercent ||
                correctionRatio < 100d / correctionAllowancePercent)
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

            return true;
        }

        /// <summary>
        /// Determines whether the specified extrema is an impulse.
        /// </summary>
        /// <param name="mainExtrema">The extrema collection - main.</param>
        /// <param name="correctionAllowancePercent">The correction allowance percent.</param>
        /// <param name="minorExtrema">The extrema collection - minor for inner structures.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is impulse; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsImpulse(
            SortedDictionary<int, double> mainExtrema, int correctionAllowancePercent, SortedDictionary<int, double> minorExtrema)
        {
            if (mainExtrema == null)
            {
                return false;
            }

            int count = mainExtrema.Count;
            if (count < 6)
            {
                // 0 -> wave 1 -> 2 -> 3 -> 4 -> 5
                return false;
            }

            bool isSimpleImpulse = IsSimpleImpulse(
                mainExtrema, correctionAllowancePercent);

            return isSimpleImpulse;
        }
    }
}
