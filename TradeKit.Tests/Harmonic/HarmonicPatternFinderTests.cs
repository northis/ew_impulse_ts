using NUnit.Framework;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Stage 2 checks: the incremental XABC candidate pool, the invalidation rules and the
    /// point D confirmation.
    /// </summary>
    [TestFixture]
    public class HarmonicPatternFinderTests
    {
        private const int X_BAR = 10;
        private const int A_BAR = 20;
        private const int B_BAR = 30;
        private const int C_BAR = 40;
        private const int D_BAR = 50;

        private static readonly double XA_HEIGHT = 100d;
        private static readonly double A_VALUE = 200d;
        private static readonly double X_VALUE = 100d;
        private static readonly double B_VALUE = A_VALUE - HarmonicFib.F618 * XA_HEIGHT;
        private static readonly double C_VALUE = B_VALUE + HarmonicFib.F886 * (A_VALUE - B_VALUE);
        private static readonly double D_VALUE = A_VALUE - HarmonicFib.F786 * XA_HEIGHT;

        private static HarmonicParams GartleyOnly()
        {
            return new HarmonicParams
            {
                Patterns = new SortedSet<HarmonicPatternType> { HarmonicPatternType.GARTLEY },

                // The synthetic geometry above is built on the exact Fibonacci ratios and is
                // read against the strict Pine tolerance: a bar placed just outside a ratio
                // has to be rejected. The library ships a wider one, which the archive says
                // pays, but which would accept the deliberately wrong bars of these tests.
                FibErrorPercent = 15d
            };
        }

        private static List<(int, double)> BullPoints(int dBar = D_BAR, int lastBar = 60)
        {
            return new List<(int, double)>
            {
                (0, 150d),
                (X_BAR, X_VALUE),
                (A_BAR, A_VALUE),
                (B_BAR, B_VALUE),
                (C_BAR, C_VALUE),
                (dBar, D_VALUE),
                (lastBar, 190d)
            };
        }

        private static List<(int, double)> BearPoints()
        {
            return BullPoints().Select(a => (a.Item1, 300d - a.Item2)).ToList();
        }

        private static List<(int Index, IReadOnlyList<HarmonicItem> Items)> Run(
            TestBarsProvider provider, HarmonicParams parameters)
        {
            var finder = new HarmonicPatternFinder(provider, parameters);
            var result = new List<(int, IReadOnlyList<HarmonicItem>)>();

            for (int i = 0; i < provider.Count; i++)
            {
                IReadOnlyList<HarmonicItem> items = finder.FindPatterns(i);
                if (items.Count > 0)
                    result.Add((i, items));
            }

            return result;
        }

        [Test]
        public void FindsBullishGartley_WithExactPoints()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints());
            List<(int Index, IReadOnlyList<HarmonicItem> Items)> found = Run(provider, GartleyOnly());

            Assert.That(found, Has.Count.EqualTo(1),
                "Exactly one bar must produce the pattern.");
            Assert.That(found[0].Index, Is.EqualTo(D_BAR + 1),
                "The pattern must appear on the point D confirmation bar.");

            HarmonicItem item = found[0].Items.Single();
            Assert.Multiple(() =>
            {
                Assert.That(item.PatternType, Is.EqualTo(HarmonicPatternType.GARTLEY));
                Assert.That(item.IsBull, Is.True);
                Assert.That(item.ItemX.BarIndex, Is.EqualTo(X_BAR));
                Assert.That(item.ItemA.BarIndex, Is.EqualTo(A_BAR));
                Assert.That(item.ItemB.BarIndex, Is.EqualTo(B_BAR));
                Assert.That(item.ItemC.BarIndex, Is.EqualTo(C_BAR));
                Assert.That(item.ItemD.BarIndex, Is.EqualTo(D_BAR));
                Assert.That(item.ItemX.Value, Is.EqualTo(X_VALUE).Within(1e-9));
                Assert.That(item.ItemD.Value, Is.EqualTo(D_VALUE).Within(1e-9));
                Assert.That(item.Score.FinalRatio, Is.EqualTo(HarmonicFib.F786).Within(1e-9));
                Assert.That(item.Score.Total, Is.GreaterThan(0d).And.LessThanOrEqualTo(1d));
            });
        }

        [Test]
        public void FindsBearishGartley_WithExactPoints()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BearPoints());
            List<(int Index, IReadOnlyList<HarmonicItem> Items)> found = Run(provider, GartleyOnly());

            Assert.That(found, Has.Count.EqualTo(1));
            HarmonicItem item = found[0].Items.Single();

            Assert.Multiple(() =>
            {
                Assert.That(item.IsBull, Is.False);
                Assert.That(item.ItemX.BarIndex, Is.EqualTo(X_BAR));
                Assert.That(item.ItemD.BarIndex, Is.EqualTo(D_BAR));
                Assert.That(item.ItemD.Value, Is.EqualTo(300d - D_VALUE).Within(1e-9));
            });
        }

        [Test]
        public void PointD_IsNotConfirmedEarlierThanRequested()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints());
            HarmonicParams parameters = GartleyOnly();
            parameters.DConfirmationBars = 3;

            List<(int Index, IReadOnlyList<HarmonicItem> Items)> found = Run(provider, parameters);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Index, Is.EqualTo(D_BAR + 3));
        }

        [Test]
        public void PointD_CanBeConfirmedWithoutTrailingBars()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints());

            HarmonicParams waiting = GartleyOnly();
            HarmonicParams immediate = GartleyOnly();
            immediate.DConfirmationBars = 0;

            List<(int Index, IReadOnlyList<HarmonicItem> Items)> withWait = Run(provider, waiting);
            List<(int Index, IReadOnlyList<HarmonicItem> Items)> withoutWait = Run(provider, immediate);

            Assert.That(withWait, Has.Count.EqualTo(1));
            Assert.That(withoutWait, Has.Count.EqualTo(1));

            HarmonicItem item = withoutWait[0].Items.Single();
            Assert.Multiple(() =>
            {
                Assert.That(withoutWait[0].Index, Is.EqualTo(item.ItemD.BarIndex),
                    "Without trailing bars the pattern is reported on the point D bar itself.");
                Assert.That(item.ItemD.Value,
                    Is.EqualTo(provider.GetLowPrice(item.ItemD.BarIndex)).Within(1e-12));

                // The price is still falling towards the deepest low, so an immediate point D
                // is taken on the first bar that makes a new extreme and passes the ratios.
                Assert.That(withoutWait[0].Index, Is.LessThan(withWait[0].Index),
                    "An immediate point D must be reported earlier than a confirmed one.");
                Assert.That(item.ItemC.BarIndex, Is.LessThan(item.ItemD.BarIndex));
            });
        }

        [Test]
        public void EqualLowOnATrailingBar_DoesNotInvalidateThePointD()
        {
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                // The bar right after D repeats its low exactly. The Pine comparison is
                // strict, so the pivot must survive.
                [D_BAR + 1] = (D_VALUE + 1d, D_VALUE)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints(), overrides);
            List<(int Index, IReadOnlyList<HarmonicItem> Items)> found = Run(provider, GartleyOnly());

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Index, Is.EqualTo(D_BAR + 1));
        }

        [Test]
        public void PriceLeavingThePrz_InvalidatesTheCandidate()
        {
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                // A bar between C and D dips below the farthest PRZ level.
                [C_BAR + 5] = (C_VALUE - 0.5d * (C_VALUE - D_VALUE), 100d)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints(), overrides);
            Assert.That(Run(provider, GartleyOnly()), Is.Empty);
        }

        [Test]
        public void IntermediateLowBetweenCandD_RejectsThePointD()
        {
            // The dip stays inside the PRZ, so the candidate survives, but it makes the later
            // point D no longer the lowest low since C - and the dip itself is too deep for
            // the AD/XA ratio of a Gartley.
            double spikeLow = A_VALUE - 0.93d * XA_HEIGHT;
            var overrides = new Dictionary<int, (double High, double Low)>
            {
                [C_BAR + 5] = (C_VALUE - 0.5d * (C_VALUE - D_VALUE), spikeLow)
            };

            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints(), overrides);
            Assert.That(Run(provider, GartleyOnly()), Is.Empty);
        }

        [Test]
        public void Candidate_ExpiresByTheCdTimeout()
        {
            // The point D arrives far beyond ((C - X) / 3) * (1 + asymmetry) bars after C.
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints(dBar: 200, lastBar: 240));
            HarmonicParams parameters = GartleyOnly();

            var finder = new HarmonicPatternFinder(provider, parameters);
            int maxCandidates = 0;
            var found = new List<HarmonicItem>();

            for (int i = 0; i < provider.Count; i++)
            {
                found.AddRange(finder.FindPatterns(i));
                maxCandidates = Math.Max(maxCandidates, finder.CandidateCount);
            }

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.Empty, "The timed-out candidate must not produce a pattern.");
                Assert.That(maxCandidates, Is.GreaterThan(0), "A candidate must have been registered.");
                Assert.That(finder.CandidateCount, Is.Zero, "The candidate pool must be emptied.");
            });
        }

        [Test]
        public void Candidate_IsEvictedByBarsDepth()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints(dBar: 200, lastBar: 240));
            HarmonicParams parameters = GartleyOnly();

            // Effectively disable the CD timeout so only the depth eviction can fire.
            parameters.LegAsymmetryPercent = 100_000d;
            parameters.BarsDepth = 30;

            var finder = new HarmonicPatternFinder(provider, parameters);
            int lastCandidateBar = -1;

            for (int i = 0; i < provider.Count; i++)
            {
                finder.FindPatterns(i);
                if (finder.CandidateCount > 0)
                    lastCandidateBar = i;
            }

            Assert.Multiple(() =>
            {
                Assert.That(lastCandidateBar, Is.GreaterThan(C_BAR));
                Assert.That(lastCandidateBar, Is.LessThanOrEqualTo(C_BAR + parameters.BarsDepth + 1));
                Assert.That(finder.CandidateCount, Is.Zero);
            });
        }

        [Test]
        public void RepeatedRun_ProducesAnIdenticalSequence()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints());
            var parameters = new HarmonicParams();

            string first = Describe(Run(provider, parameters));
            string second = Describe(Run(provider, parameters));

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void SamePattern_IsNotReportedTwice()
        {
            TestBarsProvider provider = HarmonicTestBars.Build(BullPoints());
            var finder = new HarmonicPatternFinder(provider, GartleyOnly());

            var keys = new List<string>();
            for (int i = 0; i < provider.Count; i++)
            {
                foreach (HarmonicItem item in finder.FindPatterns(i))
                {
                    keys.Add($"{item.PatternType}|{item.IsBull}|{item.ItemX.BarIndex}|" +
                             $"{item.ItemA.BarIndex}|{item.ItemB.BarIndex}|{item.ItemC.BarIndex}");
                }
            }

            Assert.That(keys, Is.Unique);
            Assert.That(keys, Is.Not.Empty);
        }

        private static string Describe(IEnumerable<(int Index, IReadOnlyList<HarmonicItem> Items)> found)
        {
            return string.Join(";", found.SelectMany(a => a.Items.Select(b =>
                $"{a.Index}:{b.PatternType}:{b.IsBull}:{b.ItemX.BarIndex}:{b.ItemA.BarIndex}:" +
                $"{b.ItemB.BarIndex}:{b.ItemC.BarIndex}:{b.ItemD.BarIndex}:" +
                $"{b.Score.Total:F12}:{b.TakeProfit1:F12}:{b.TakeProfit2:F12}")));
        }
    }
}
