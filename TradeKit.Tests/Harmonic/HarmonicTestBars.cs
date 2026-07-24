using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Builds synthetic zig-zag bar series with exactly placed pivot points, so the harmonic
    /// search can be tested against a known figure.
    /// </summary>
    internal static class HarmonicTestBars
    {
        private static readonly DateTime START = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Builds a provider whose highs and lows form a strict zig-zag through the points
        /// specified. Every point bar is the only extremum of its leg, so it is a valid pivot
        /// for any pivot period that fits inside the neighbouring legs.
        /// </summary>
        /// <param name="points">The turning points, ordered by bar index.</param>
        /// <param name="overrides">Optional per-bar high/low replacements applied after the zig-zag is built.</param>
        public static TestBarsProvider Build(
            IReadOnlyList<(int Index, double Value)> points,
            IReadOnlyDictionary<int, (double High, double Low)>? overrides = null)
        {
            if (points == null || points.Count < 2)
                throw new ArgumentException("At least two points are required.", nameof(points));

            int total = points[^1].Index + 1;
            var high = new double[total];
            var low = new double[total];

            double minStep = double.MaxValue;
            for (int k = 1; k < points.Count; k++)
            {
                double step = Math.Abs(points[k].Value - points[k - 1].Value) /
                              (points[k].Index - points[k - 1].Index);
                minStep = Math.Min(minStep, step);
            }

            double delta = minStep / 100d;

            for (int k = 1; k < points.Count; k++)
            {
                (int fromIndex, double fromValue) = points[k - 1];
                (int toIndex, double toValue) = points[k];
                bool up = toValue > fromValue;

                for (int i = fromIndex; i <= toIndex; i++)
                {
                    double price = fromValue +
                                   (toValue - fromValue) * (i - fromIndex) / (toIndex - fromIndex);
                    if (up)
                    {
                        high[i] = price;
                        low[i] = price - delta;
                    }
                    else
                    {
                        low[i] = price;
                        high[i] = price + delta;
                    }
                }
            }

            for (int k = 0; k < points.Count; k++)
            {
                (int index, double value) = points[k];
                bool isHigh = k == 0
                    ? value > points[1].Value
                    : value > points[k - 1].Value;

                if (isHigh)
                {
                    high[index] = value;
                    low[index] = value - delta;
                }
                else
                {
                    low[index] = value;
                    high[index] = value + delta;
                }
            }

            var provider = new TestBarsProvider(TimeFrameHelper.Hour1,
                new SymbolBase("TEST", "Test Symbol", 1, 5, 0.00001, 0.00001, 100_000));

            if (overrides != null)
            {
                foreach (KeyValuePair<int, (double High, double Low)> pair in overrides)
                {
                    high[pair.Key] = pair.Value.High;
                    low[pair.Key] = pair.Value.Low;
                }
            }

            for (int i = 0; i < total; i++)
            {
                double mid = (high[i] + low[i]) / 2d;
                provider.AddCandle(new Candle(mid, high[i], low[i], mid, null, i),
                    START.AddHours(i));
            }

            return provider;
        }

        /// <summary>
        /// Feeds every bar of the provider to the action specified, in chronological order.
        /// </summary>
        /// <param name="provider">The bars provider.</param>
        /// <param name="action">The per-bar action.</param>
        public static void Replay(TestBarsProvider provider, Action<int> action)
        {
            for (int i = 0; i < provider.Count; i++)
                action(i);
        }
    }
}
