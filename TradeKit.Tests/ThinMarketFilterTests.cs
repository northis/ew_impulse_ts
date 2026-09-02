using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Synthetic tests of <see cref="ThinMarketFilter"/> (DIAGONAL.md §4.5): the volume
    /// metric, the OHLC range fallback, the minimum run length and the causal appearance
    /// of a segment are each exercised on a flat baseline series with a crafted thin tail.
    /// </summary>
    internal class ThinMarketFilterTests
    {
        private const double BASE_LOW = 1.0000;
        private const double BASE_HIGH = 1.0001;

        private const double BASE_VOLUME = 1000;

        /// <summary>First bar of the injected thin run in the default scenario.</summary>
        private const int THIN_START = 300;

        private const int THIN_BARS = 20;

        /// <summary>Builds a provider with `thinVolume` on bars [THIN_START, THIN_START+thinBars).</summary>
        private static TestBarsProvider BuildProvider(
            double thinVolume = 100,
            double thinRange = BASE_HIGH - BASE_LOW,
            int thinBars = THIN_BARS,
            int barCount = 340,
            double baseVolume = BASE_VOLUME)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            var start = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < barCount; i++)
            {
                bool thin = i >= THIN_START && i < THIN_START + thinBars;
                double high = thin ? BASE_LOW + thinRange : BASE_HIGH;
                double volume = thin ? thinVolume : baseVolume;
                provider.AddCandle(
                    new Candle(BASE_LOW, high, BASE_LOW, high, null, i), start.AddMinutes(5 * i), volume);
            }

            return provider;
        }

        private static void RunToBar(ThinMarketFilter filter, TestBarsProvider provider,
            int barIndex)
        {
            for (int i = 0; i <= barIndex; i++)
                filter.OnCalculate(provider.GetOpenTime(i));
        }

        [Test]
        public void VolumeMetric_ThinRun_FormsSegment()
        {
            var provider = BuildProvider();
            var filter = new ThinMarketFilter(provider);
            RunToBar(filter, provider, provider.Count - 1);

            Assert.That(filter.UsesVolume, Is.True);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));

            ThinMarketSegment segment = filter.Segments[0];
            Assert.That(segment.StartBar, Is.EqualTo(THIN_START));
            Assert.That(segment.EndBar, Is.EqualTo(THIN_START + THIN_BARS - 1));

            Assert.That(filter.IsInThinSegment(THIN_START), Is.True);
            Assert.That(filter.IsInThinSegment(THIN_START + THIN_BARS - 1), Is.True);
            Assert.That(filter.IsInThinSegment(THIN_START - 1), Is.False);
            Assert.That(filter.IsInThinSegment(THIN_START + THIN_BARS), Is.False);
        }

        [Test]
        public void RangeProxy_UsedWhenNoVolume()
        {
            // The provider reports no volume at all (the pre-TickVolume archives): the
            // filter must fall back to the high-low range.
            var provider = BuildProvider(baseVolume: 0, thinVolume: 0, thinRange: 0.00001);
            var filter = new ThinMarketFilter(provider);
            RunToBar(filter, provider, provider.Count - 1);

            Assert.That(filter.UsesVolume, Is.False);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));
            Assert.That(filter.Segments[0].StartBar, Is.EqualTo(THIN_START));
            Assert.That(filter.Segments[0].EndBar, Is.EqualTo(THIN_START + THIN_BARS - 1));
        }

        [Test]
        public void ShortRun_NotASegment()
        {
            // A run shorter than MinThinRunBars must not form a segment.
            var provider = BuildProvider(thinVolume: 100, thinBars: 10);
            var filter = new ThinMarketFilter(provider);
            RunToBar(filter, provider, provider.Count - 1);

            Assert.That(filter.Segments.Count, Is.EqualTo(0));
            Assert.That(filter.IsInThinSegment(THIN_START + 5), Is.False);
        }

        [Test]
        public void Segment_AppearsExactlyWhenRunReachesMinimum_GrowsWithTheRun()
        {
            var provider = BuildProvider();
            var filter = new ThinMarketFilter(provider);

            // The run starts at THIN_START; a segment exists only from its 12th dead bar.
            RunToBar(filter, provider, THIN_START + filter.MinThinRunBars - 2);
            Assert.That(filter.Segments.Count, Is.EqualTo(0),
                "a thin run must not form a segment before MinThinRunBars dead bars");

            filter.OnCalculate(provider.GetOpenTime(THIN_START + filter.MinThinRunBars - 1));
            Assert.That(filter.Segments.Count, Is.EqualTo(1));
            Assert.That(filter.Segments[0].EndBar,
                Is.EqualTo(THIN_START + filter.MinThinRunBars - 1));

            // While the run continues the segment grows, it is not duplicated.
            RunToBar(filter, provider, THIN_START + THIN_BARS - 1);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));
            Assert.That(filter.Segments[0].EndBar, Is.EqualTo(THIN_START + THIN_BARS - 1));

            // After activity returns the segment is frozen.
            RunToBar(filter, provider, provider.Count - 1);
            Assert.That(filter.Segments.Count, Is.EqualTo(1));
            Assert.That(filter.Segments[0].EndBar, Is.EqualTo(THIN_START + THIN_BARS - 1));
        }

        [Test]
        public void Disabled_NoSegments()
        {
            var provider = BuildProvider();

            var byFactor = new ThinMarketFilter(provider, thinFactor: 0);
            var byRun = new ThinMarketFilter(provider, minThinRunBars: 0);
            Assert.That(byFactor.IsEnabled, Is.False);
            Assert.That(byRun.IsEnabled, Is.False);

            RunToBar(byFactor, provider, provider.Count - 1);
            RunToBar(byRun, provider, provider.Count - 1);
            Assert.That(byFactor.Segments.Count, Is.EqualTo(0));
            Assert.That(byRun.Segments.Count, Is.EqualTo(0));
        }
    }
}
