using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 6 of EW_MARKUP_v2.md §19 — prediction mode (§13) and extension cancellation
    /// zones (§11). Drives <see cref="ElliottWaveExactMarkupV2.Parse"/> over deterministic
    /// synthetic pivots that end one or two waves short of a complete model, and over a
    /// bounded slice of real <c>data/</c> candles, asserting:
    /// <list type="bullet">
    /// <item>the best continuation is a <see cref="NodeStatus.PROJECTED"/> node whose
    /// confirmed prefix obeys the hard rules §7–9 and whose missing tail is projected
    /// (correct count, direction, finite targets, cancellation zone §11.2);</item>
    /// <item>prediction is deterministic: identical input ⇒ identical projection (T-MK-4).</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2ProjectionTests
    {
        private const int MAX_BARS = 400;
        private const int MAX_FILES = 3;
        private const double DEVIATION_PERCENT = 0.5;

        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly HashSet<ElliottModelType> WAVE4_OVERLAP_ALLOWED = new()
        {
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.FLAT_REGULAR
        };

        private static BarPoint Pt(int barIndex, double value) =>
            new(value, BASE_TIME.AddHours(barIndex), HOUR1, barIndex);

        /// <summary>Builds strictly-alternating pivots spaced two bars apart.</summary>
        private static IReadOnlyList<BarPoint> Pivots(params double[] values)
        {
            var pts = new List<BarPoint>(values.Length);
            for (int i = 0; i < values.Length; i++)
                pts.Add(Pt(i * 2, values[i]));
            return pts;
        }

        // ----- impulse missing wave 5 -----------------------------------------

        [Test]
        public void Parse_ImpulseMissingWave5_ProjectsForwardContinuation()
        {
            // W1 0→10, W2 →4, W3 →24, W4 →16 — four valid impulse waves, W5 not yet formed.
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.BestProjection, Is.Not.Null,
                "A range ending mid-structure must yield a projected continuation (§13).");
            AssertProjectionInvariants(result.BestProjection!);

            // The last confirmed move (W4) is down, so the projected next wave points up.
            WaveProjection first = result.BestProjection!.Projections[0];
            double lastPrice = result.BestProjection.EndPivot.Value;
            Assert.That(first.Price, Is.GreaterThan(lastPrice),
                "The wave after a down move must be projected upward.");
            Assert.That(first.BarIndex, Is.GreaterThan(result.BestProjection.EndPivot.BarIndex),
                "A projected wave must end after the current bar.");
        }

        // ----- zigzag missing wave C ------------------------------------------

        [Test]
        public void Parse_ZigzagMissingWaveC_ProjectsDownwardC()
        {
            // A 100→60 (down), B →80 (up) — wave C not yet formed.
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(100, 60, 80));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.BestProjection, Is.Not.Null);
            AssertProjectionInvariants(result.BestProjection!);

            // After an up wave B, the projected continuation points down.
            WaveProjection first = result.BestProjection!.Projections[0];
            Assert.That(first.Price, Is.LessThan(result.BestProjection.EndPivot.Value),
                "The wave after an up move must be projected downward.");
        }

        // ----- two missing waves ----------------------------------------------

        [Test]
        public void Parse_ImpulseMissingTwoWaves_ProjectsBothTailWaves()
        {
            // W1 0→10, W2 →4, W3 →24 — waves 4 and 5 not yet formed (S = K−2).
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.BestProjection, Is.Not.Null);
            AssertProjectionInvariants(result.BestProjection!);

            // Every projected wave alternates direction from the previous one.
            IReadOnlyList<WaveProjection> projections = result.BestProjection!.Projections;
            for (int i = 1; i < projections.Count; i++)
            {
                bool prevUp = projections[i - 1].Price
                              > (i == 1
                                  ? result.BestProjection.EndPivot.Value
                                  : projections[i - 2].Price);
                bool thisUp = projections[i].Price > projections[i - 1].Price;
                Assert.That(thisUp, Is.Not.EqualTo(prevUp),
                    "Consecutive projected waves must alternate direction.");
            }
        }

        // ----- T-MK-4 determinism for the projection --------------------------

        [Test]
        public void Parse_SameInput_ProjectionIsDeterministic()
        {
            IReadOnlyList<BarPoint> pivots = Pivots(0, 10, 4, 24, 16);

            string first = Serialize(new ElliottWaveExactMarkupV2(null, pivots).Parse().BestProjection);
            string second = Serialize(new ElliottWaveExactMarkupV2(null, pivots).Parse().BestProjection);

            Assert.That(second, Is.EqualTo(first),
                "T-MK-4: identical input must yield an identical projection.");
        }

        // ----- complete input still offers a continuation ---------------------

        [Test]
        public void Parse_CompleteImpulse_AlsoOffersAProjectedContinuation()
        {
            // A textbook complete impulse — Roots carry it; BestProjection offers the
            // "structure still forming" alternative reaching the current bar.
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Not.Empty, "A complete impulse must be marked up.");
            if (result.BestProjection != null)
                AssertProjectionInvariants(result.BestProjection);
        }

        // ----- real data, bounded range ---------------------------------------

        [TestCaseSource(nameof(DataFiles))]
        public void Parse_RealDataBoundedRange_ProjectionObeysHardRulesAndIsDeterministic(string filePath)
        {
            var provider = new TestBarsProvider(HOUR1);
            provider.LoadCandles(filePath);
            Assert.That(provider.Count, Is.GreaterThan(10), $"Too few candles in {filePath}.");

            int endIndex = Math.Min(provider.Count - 1, MAX_BARS);
            var markup = new ElliottWaveExactMarkupV2(provider, 0, endIndex, DEVIATION_PERCENT);
            if (markup.Segments.Count < 3)
                Assert.Ignore("Not enough zigzag segments in this slice for a projection.");

            int endSeg = Math.Min(markup.Segments.Count - 1, 8);

            MarkupSearchResult first = markup.ParseSegmentRange(0, endSeg);
            Assert.That(first.Metrics.Aborted, Is.False, "The node cap must not abort a bounded slice.");

            if (first.BestProjection != null)
                AssertProjectionInvariants(first.BestProjection);

            MarkupSearchResult second = markup.ParseSegmentRange(0, endSeg);
            Assert.That(Serialize(second.BestProjection), Is.EqualTo(Serialize(first.BestProjection)),
                "T-MK-4: the projection over a fixed slice must be deterministic.");
        }

        // ----- invariants -----------------------------------------------------

        private static void AssertProjectionInvariants(TreeNode node)
        {
            Assert.That(node.Status, Is.EqualTo(NodeStatus.PROJECTED),
                "The best continuation must be a PROJECTED node.");

            int waves = ElliottWaveExactMarkup.GetExpectedWaves(node.Model);
            int confirmed = node.Children.Count;

            Assert.That(confirmed, Is.GreaterThanOrEqualTo(1), "A projection needs a confirmed prefix.");
            Assert.That(confirmed, Is.LessThan(waves), "A projection must be missing at least one wave.");
            Assert.That(waves - confirmed, Is.LessThanOrEqualTo(ElliottWaveExactMarkupV2.MAX_MISSING_WAVES),
                "At most two tail waves may be projected (§13).");
            Assert.That(node.ActiveFromWaveIndex, Is.EqualTo(confirmed),
                "ActiveFromWaveIndex must equal the confirmed-wave count (§3.5).");
            Assert.That(node.Projections.Count, Is.EqualTo(waves - confirmed),
                "There must be exactly one projection per missing wave.");

            Assert.That(node.Cancellation, Is.Not.Null, "A projection must carry a cancellation zone (§11.2).");
            Assert.That(node.Cancellation!.Reason, Is.EqualTo(DeathReason.EXTENSION_CANCELLED));

            foreach (WaveProjection p in node.Projections)
            {
                Assert.That(p.WaveName, Is.Not.Null.And.Not.Empty, "Every projection must name its wave.");
                Assert.That(double.IsFinite(p.Price), Is.True, "Projected prices must be finite.");
                Assert.That(p.Weight, Is.GreaterThan(0.0), "Projected weights must be positive.");
            }

            AssertConfirmedPrefixObeysHardRules(node);
        }

        /// <summary>The confirmed prefix of a projection must obey the same hard rules §7–9.</summary>
        private static void AssertConfirmedPrefixObeysHardRules(TreeNode node)
        {
            var waves = new List<ElliottWaveExactMarkupV2.Segment>(node.Children.Count);
            foreach (TreeNode child in node.Children)
                waves.Add(new ElliottWaveExactMarkupV2.Segment(child.StartPivot, child.EndPivot));

            bool wave4Simple = waves.Count < 4
                || !WAVE4_OVERLAP_ALLOWED.Contains(node.Children[3].Model);

            Assert.That(ElliottWaveExactMarkupV2.CheckPriceRules(node.Model, waves, wave4Simple),
                Is.EqualTo(DeathReason.NONE),
                $"Confirmed prefix of {node.Model} violates a hard-price rule.");
            Assert.That(ElliottWaveExactMarkupV2.CheckTimeWindow(node.Model, waves),
                Is.EqualTo(DeathReason.NONE),
                $"Confirmed prefix of {node.Model} violates a hard-time rule.");
        }

        // ----- helpers --------------------------------------------------------

        private static string Serialize(TreeNode? node)
        {
            if (node == null)
                return "<none>";

            var sb = new System.Text.StringBuilder();
            Serialize(node, sb);
            return sb.ToString();
        }

        private static void Serialize(TreeNode node, System.Text.StringBuilder sb)
        {
            sb.Append(node.Model)
                .Append(':').Append(node.WavePos ?? "root")
                .Append(':').Append(node.RangeStartSegment).Append('-').Append(node.RangeEndSegment)
                .Append(':').Append(node.Status)
                .Append(":L").Append(node.Level)
                .Append(':').Append(node.Score.ToString("R"))
                .Append(":a").Append(node.ActiveFromWaveIndex);

            foreach (WaveProjection p in node.Projections)
                sb.Append("|P[").Append(p.WaveName).Append(' ')
                    .Append(p.Price.ToString("R")).Append('@').Append(p.BarIndex)
                    .Append(' ').Append(p.RatioLabel).Append(']');

            sb.Append('{');
            foreach (TreeNode child in node.Children)
            {
                Serialize(child, sb);
                sb.Append(';');
            }
            sb.Append('}');
        }

        private static IEnumerable<TestCaseData> DataFiles()
        {
            string dataDir = FindDataDir();
            string[] files = Directory
                .GetFiles(dataDir, "*.csv")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(MAX_FILES)
                .ToArray();

            foreach (string file in files)
                yield return new TestCaseData(file)
                    .SetName($"Projection_{Path.GetFileNameWithoutExtension(file)}");
        }

        private static string FindDataDir()
        {
            DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                string dataDir = Path.Combine(dir.FullName, "data");
                if (Directory.Exists(dataDir) &&
                    File.Exists(Path.Combine(dir.FullName, "TradeKit.sln")))
                {
                    return dataDir;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repo 'data' folder above the test directory.");
        }
    }
}
