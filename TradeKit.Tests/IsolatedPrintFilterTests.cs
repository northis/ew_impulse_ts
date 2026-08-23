using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Synthetic tests of <see cref="IsolatedPrintFilter"/> (DIAGONAL.md §4.4): the
    /// two-sided gap, the displacement-vs-volatility threshold, the retrace window and
    /// the confirmation delay are each exercised in isolation on a flat baseline series
    /// into which crafted spikes are injected.
    /// </summary>
    internal class IsolatedPrintFilterTests
    {
        /// <summary>Flat baseline range — the local median range is exactly this.</summary>
        private const double BASE_LOW = 1.0000;

        private const double BASE_HIGH = 1.0001;

        /// <summary>Index of the injected spike in the default scenario.</summary>
        private const int SPIKE_BAR = 60;

        private static TestBarsProvider BuildProvider(
            IReadOnlyDictionary<int, (double Low, double High)>? overrides = null,
            int barCount = 90)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < barCount; i++)
            {
                (double low, double high) =
                    overrides != null && overrides.TryGetValue(i, out var o)
                        ? o
                        : (BASE_LOW, BASE_HIGH);
                provider.AddCandle(
                    new Candle(low, high, low, high, null, i), start.AddMinutes(5 * i));
            }

            return provider;
        }

        private static void RunToBar(IsolatedPrintFilter filter, TestBarsProvider provider,
            int barIndex)
        {
            for (int i = 0; i <= barIndex; i++)
                filter.OnCalculate(provider.GetOpenTime(i));
        }

        private static void RunToEnd(IsolatedPrintFilter filter, TestBarsProvider provider)
        {
            RunToBar(filter, provider, provider.Count - 1);
        }

        [Test]
        public void DownSpike_ConfirmedExactlyWhenRetraceWindowCloses()
        {
            // D = 1.0000 - 0.9950 = 0.0050 = 50 median ranges; retrace on the very next bar.
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (0.9950, 0.9950)
            });
            var filter = new IsolatedPrintFilter(provider);

            // End 60 matures when the bar 60 + 12 = 72 closes — not a bar earlier.
            RunToBar(filter, provider, SPIKE_BAR + filter.RetraceWindowBars - 1);
            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.False,
                "the segment must not be confirmed before its retrace window has closed");
            Assert.That(filter.Segments.Count, Is.EqualTo(0));

            filter.OnCalculate(provider.GetOpenTime(SPIKE_BAR + filter.RetraceWindowBars));
            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.True);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));

            IsolatedPrintSegment segment = filter.Segments[0];
            Assert.That(segment.StartBar, Is.EqualTo(SPIKE_BAR));
            Assert.That(segment.EndBar, Is.EqualTo(SPIKE_BAR));
            Assert.That(segment.IsDown, Is.True);
            Assert.That(segment.Displacement, Is.EqualTo(0.0050).Within(1e-9));

            // Idempotence: reprocessing the same bar reconfirms nothing.
            filter.OnCalculate(provider.GetOpenTime(SPIKE_BAR + filter.RetraceWindowBars));
            Assert.That(filter.Segments.Count, Is.EqualTo(1));

            // Neighbors stay usable.
            Assert.That(filter.IsExcluded(SPIKE_BAR - 1), Is.False);
            Assert.That(filter.IsExcluded(SPIKE_BAR + 1), Is.False);
        }

        [Test]
        public void UpSpike_Confirmed()
        {
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (1.0050, 1.0050)
            });
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.True);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));
            Assert.That(filter.Segments[0].IsDown, Is.False);
            Assert.That(filter.Segments[0].Displacement, Is.EqualTo(0.0049).Within(1e-9));
        }

        [Test]
        public void TwoBarSpike_MergedIntoOneSegment()
        {
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (0.9950, 0.9950),
                [SPIKE_BAR + 1] = (0.9950, 0.9950)
            });
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.Segments.Count, Is.EqualTo(1),
                "adjacent print bars must merge into a single segment");
            Assert.That(filter.Segments[0].StartBar, Is.EqualTo(SPIKE_BAR));
            Assert.That(filter.Segments[0].EndBar, Is.EqualTo(SPIKE_BAR + 1));
            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.True);
            Assert.That(filter.IsExcluded(SPIKE_BAR + 1), Is.True);
            Assert.That(filter.IsExcluded(SPIKE_BAR - 1), Is.False);
            Assert.That(filter.IsExcluded(SPIKE_BAR + 2), Is.False);
        }

        [Test]
        public void OneSidedStep_NotConfirmed()
        {
            // A weekend-gap shape: price steps down and STAYS there — the next bar's range
            // touches the segment, so there is no two-sided gap.
            var overrides = new Dictionary<int, (double, double)>();
            for (int i = SPIKE_BAR; i < 90; i++)
                overrides[i] = (0.9950, 0.9951);

            var provider = BuildProvider(overrides);
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.Segments.Count, Is.EqualTo(0),
                "a one-sided step is not an isolated print");
            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.False);
        }

        [Test]
        public void GapWithoutRetrace_NotConfirmed()
        {
            // A genuine breakdown shape: the market gaps down (the spike range
            // 0.9940..0.9960 hangs below both neighbors), then consolidates at 0.9990 and
            // never reclaims the pre-gap level inside the window — retrace target
            // = 0.9960 + 0.8 * (0.9990 - 0.9940) = 1.0000, and no bar reaches it. The
            // decision is made once, when the window closes, and is never revisited.
            var overrides = new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (0.9940, 0.9960)
            };
            for (int i = SPIKE_BAR + 1; i < 90; i++)
                overrides[i] = (0.9990, 0.9991);

            var provider = BuildProvider(overrides);
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.Segments.Count, Is.EqualTo(0),
                "a displacement that is not retraced inside the window is not a print");
        }

        [Test]
        public void SubThresholdDisplacement_NotConfirmed()
        {
            // D = 0.0003 = 3 median ranges < MinDisplacementAtr = 4 — an ordinary outlier,
            // not an isolated print.
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (0.9997, 0.9997)
            });
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.Segments.Count, Is.EqualTo(0));
            Assert.That(filter.IsExcluded(SPIKE_BAR), Is.False);
        }

        [Test]
        public void SpikeBeforeEnoughHistory_NotConfirmed()
        {
            // No volatility context (fewer than MIN_VOLATILITY_BARS before the segment) —
            // nothing to scale the displacement against.
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [10] = (0.9950, 0.9950)
            });
            var filter = new IsolatedPrintFilter(provider);
            RunToEnd(filter, provider);

            Assert.That(filter.Segments.Count, Is.EqualTo(0));
        }

        [Test]
        public void DisabledFilter_ExcludesNothing()
        {
            var provider = BuildProvider(new Dictionary<int, (double, double)>
            {
                [SPIKE_BAR] = (0.9950, 0.9950)
            });

            var bySpikeBars = new IsolatedPrintFilter(provider, maxSpikeBars: 0);
            RunToEnd(bySpikeBars, provider);
            Assert.That(bySpikeBars.IsEnabled, Is.False);
            Assert.That(bySpikeBars.Segments.Count, Is.EqualTo(0));
            Assert.That(bySpikeBars.IsExcluded(SPIKE_BAR), Is.False);

            var byDisplacement = new IsolatedPrintFilter(provider, minDisplacementAtr: 0);
            RunToEnd(byDisplacement, provider);
            Assert.That(byDisplacement.IsEnabled, Is.False);
            Assert.That(byDisplacement.Segments.Count, Is.EqualTo(0));
        }
    }
}
