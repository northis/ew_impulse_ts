using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeKit
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class PatternFinder
    {
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
        public PatternFinder(double correctionAllowancePercent, IBarsProvider barsProvider)
        {
            m_CorrectionAllowancePercent = correctionAllowancePercent;
            m_BarsProvider = barsProvider;
        }

        /// <summary>
        /// Determines whether the specified interval has a zigzag.
        /// </summary>
        /// <param name="start">The start of the interval.</param>
        /// <param name="end">The end of the interval.</param>
        /// <param name="deviation">The deviation percent</param>
        /// <returns>
        ///   <c>true</c> if the specified interval has a zigzag; otherwise, <c>false</c>.
        /// </returns>
        private bool IsZigzag(DateTime start, DateTime end, double deviation)
        {
            var minorExtremumFinder =
                new ExtremumFinder(deviation, m_BarsProvider);
            minorExtremumFinder.Calculate(start, end);
            int count = minorExtremumFinder.Extrema.Count;
            if (count == ZIGZAG_EXTREMA_COUNT)
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
        /// <param name="allowSimple">True if we treat count <see cref="SIMPLE_EXTREMA_COUNT"/>-movement as impulse.</param>
        /// <returns>
        ///   <c>true</c> if the specified extrema is an simple impulse; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSimpleImpulse(
            Extremum start, Extremum end,
            double deviation, out List<Extremum> extrema,
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
            Extremum firstWaveEnd;
            Extremum secondWaveEnd;
            Extremum thirdWaveEnd;
            Extremum fourthWaveEnd;
            Extremum fifthWaveEnd = extrema[5];
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
                    if (IsZigzag(firstItem.OpenTime, firstWaveEnd.OpenTime, dv) ||
                        IsZigzag(secondWaveEnd.OpenTime, thirdWaveEnd.OpenTime, dv) ||
                        IsZigzag(fourthWaveEnd.OpenTime, fifthWaveEnd.OpenTime, dv))
                    {
                        return false;
                    }

                    //if (IsSimpleImpulse(
                    //        firstItem, firstWaveEnd, dv, out _) &&
                    //    IsSimpleImpulse(
                    //        secondWaveEnd, thirdWaveEnd, dv, out _) &&
                    //    IsSimpleImpulse(
                    //        fourthWaveEnd, fifthWaveEnd, dv, out _))
                    //{
                    //    return true;
                    //}
                }

                return true;
            }
            
            if (countRest == 0)
            {
                firstWaveEnd = extrema[1];
                secondWaveEnd = extrema[2];
                thirdWaveEnd = extrema[3];
                fourthWaveEnd = extrema[4];
                return CheckWaves();
            }

            if (countRest < ZIGZAG_EXTREMA_COUNT)
            {
                return false;
            }
            
            //var diffs = new List<Tuple<int, double>>();
            //Extremum currentExtremum = extrema[0];

            //for (int i = 1; i < count; i++)
            //{
            //    Extremum nextExtremum = extrema[i];
            //    diffs.Add(new Tuple<int, double>(i, Math.Abs(currentExtremum - nextExtremum)));
            //    currentExtremum = nextExtremum;
            //}

            int[] impulseStartIndices = {1, 5, 7, 9, 15, 17, 21};
            int[] correctionStartIndices = { 1, 3, 9 };
            int minWaveRest = count - ZIGZAG_EXTREMA_COUNT;

            foreach (int firstIndex in impulseStartIndices)
            {
                if (firstIndex > minWaveRest)
                {
                    return false;
                }
                firstWaveEnd = extrema[firstIndex];

                foreach (int correctionSecondIndex in correctionStartIndices)
                {
                    int secondIndex = firstIndex + correctionSecondIndex;
                    if (secondIndex > minWaveRest - 1)
                    {
                        return false;
                    }
                    secondWaveEnd = extrema[secondIndex];

                    foreach (int impulseThirdIndex in impulseStartIndices)
                    {
                        int thirdIndex =  secondIndex + impulseThirdIndex;
                        if (thirdIndex > minWaveRest - 2)
                        {
                            return false;
                        }
                        
                        thirdWaveEnd = extrema[thirdIndex];

                        // TODO add triangle (5)
                        foreach (int correctionFourthIndex in correctionStartIndices)
                        {
                            int fourthIndex = thirdIndex + correctionFourthIndex;
                            if (thirdIndex > minWaveRest - 3)
                            {
                                return false;
                            }

                            fourthWaveEnd = extrema[fourthIndex];

                            bool res = CheckWaves();
                            if (res)
                            {
                                return true;
                            }
                        }
                    }
                }
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
        public bool IsImpulse(
            Extremum start, Extremum end, double deviation, out List<Extremum> extrema)
        {
            extrema = null;
            for (double dv = deviation;
                 dv >= Helper.DEVIATION_LOW;
                 dv -= Helper.DEVIATION_STEP)
            {
                bool isSimpleImpulse = IsSimpleImpulse(start, end, dv, out extrema, false);
                if (isSimpleImpulse)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
