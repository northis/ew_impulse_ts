using System;
using System.Linq;

namespace TradeKit.PatternGeneration
{
    public static class PatternGenKit
    {
        public static double GetRandomGaussian(Random random)
        {
            double x1 = 1 - random.NextDouble();
            double x2 = 1 - random.NextDouble();
            const double baseVal = 1d;

            double y1 = Math.Sqrt(-baseVal * Math.Log(x1)) *
                        Math.Cos(baseVal * Math.PI * x2);
            return y1;
        }
        public static double GetRandomGaussianAbs(Random random)
        {
            return Math.Abs(GetRandomGaussian(random));
        }

        /// <summary>
        /// Splits the number to integer parts according to the fractions passed.
        /// </summary>
        /// <param name="number">The number.</param>
        /// <param name="fractions">The fractions.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">
        /// fractions
        /// or
        /// the sum of {nameof(fractions)} values should be equals 1 - fractions
        /// </exception>
        public static int[] SplitNumber(int number, double[] fractions)
        {
            if (number < fractions.Length)
                throw new ArgumentException($"{nameof(fractions)} length shouldn't be less than {nameof(number)}", nameof(fractions));

            if (Math.Abs(fractions.Sum() - 1d) > double.Epsilon)
                throw new ArgumentException($"the sum of {nameof(fractions)} values should be equals 1",
                    nameof(fractions));

            int[] parts = new int[fractions.Length];
            double correction = number;

            for (int i = 0; i < fractions.Length; i++)
            {
                parts[i] = (int)Math.Floor(number * fractions[i]);
                correction -= parts[i];
            }

            int index = 0;
            while (correction > 0)
            {
                parts[index]++;
                correction--;
                index = (index + 1) % parts.Length;
            }

            return parts;
        }
    }
}
