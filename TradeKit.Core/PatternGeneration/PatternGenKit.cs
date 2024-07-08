namespace TradeKit.Core.PatternGeneration
{
    public static class PatternGenKit
    {
        private static double GetRandomNumber(Random random, double mean, double stdDev)
        {
            double u1 = 1.0 - random.NextDouble(); // uniform(0,1] random doubles
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                                   Math.Sin(2.0 * Math.PI * u2); // random normal(0,1)
            return mean + stdDev * randStdNormal; // random normal(mean,stdDev^2)
        }

        /// <summary>
        /// Gets the random number in range using normal distribution.
        /// </summary>
        /// <param name="random">The random.</param>
        /// <param name="min">The minimum.</param>
        /// <param name="max">The maximum.</param>
        /// <param name="mean">The mean value (most popular).</param>
        /// <returns>The picked value from the range.</returns>
        /// <exception cref="ArgumentException"></exception>
        public static double GetNormalDistributionNumber(
            Random random, double min, double max, double mean)
        {
            if (min >= max) throw new ArgumentException(
                $"{nameof(min)} should be less than {nameof(max)}");
            if (mean <= min || mean >= max) 
                throw new ArgumentException(
                    $"{mean} should be between {nameof(min)} and {nameof(max)}");

            double stdDev = (max - min) / 6.0; // Approximation: 99.7% of values will fall in [l,k]
            double result;

            do
            {
                result = GetRandomNumber(random, mean, stdDev);
            } while (result < min || result > max);

            return result;
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

            const double doubleMin = 0.000000000000001;
            if (Math.Abs(fractions.Sum() - 1d) > doubleMin)
            {
                double totalSumFix = 1 / fractions.Sum();
                for (int i = 0; i < fractions.Length; i++)
                {
                    fractions[i] *= totalSumFix;
                }
            }

            int[] parts = new int[fractions.Length];
            double correction = number;
            
            for (int i = 0; i < fractions.Length; i++)
            {
                parts[i] = (int)Math.Floor(number * fractions[i]);
                correction -= parts[i];
            }
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] < 1)
                {
                    correction -= (1 - parts[i]);
                    parts[i] = 1;
                }
            }
            
            int index = 0;
            while (correction > doubleMin)
            {
                if (parts[index] > 1)
                {
                    parts[index]++;
                    correction--;
                }

                index = (index + 1) % parts.Length;
            }

            return parts;
        }
    }
}
