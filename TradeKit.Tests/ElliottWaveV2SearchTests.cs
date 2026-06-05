using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 5 of EW_MARKUP_v2.md §19 — the markup search engine (§14).
    /// Drives <see cref="ElliottWaveExactMarkupV2.Parse"/> /
    /// <see cref="ElliottWaveExactMarkupV2.ParseSegmentRange"/> over deterministic
    /// synthetic pivots and over a bounded slice of real <c>data/</c> candles, and
    /// asserts the two markup invariants this step is responsible for:
    /// <list type="bullet">
    /// <item>T-MK-2 — no <see cref="NodeStatus.COMPLETE"/> node violates the hard
    /// rules §7–9 (re-validated against the same predicates the engine uses).</item>
    /// <item>T-MK-4 — the search is deterministic: identical input ⇒ identical output.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2SearchTests
    {
        private const int MAX_BARS = 400;
        private const int MAX_FILES = 3;
        private const double DEVIATION_PERCENT = 0.5;
        private const int REAL_RANGE_SEGMENTS = 9;

        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Mirrors ElliottWaveExactMarkupV2's private §6.3 W4-overlap exception set,
        // so the re-validation matches the engine's own wave4Simple computation.
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

        // ----- synthetic IMPULSE ----------------------------------------------

        [Test]
        public void Parse_CleanImpulse_ProducesValidCompleteImpulseRoot()
        {
            // W1 0→10, W2 →4, W3 →24, W4 →16, W5 →30 (a textbook valid impulse).
            var markup = new ElliottWaveExactMarkupV2(
                null, Pivots(0, 10, 4, 24, 16, 30));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Not.Empty, "Expected at least one COMPLETE root.");
            Assert.That(result.Metrics.Aborted, Is.False, "The node cap must not abort a tiny input.");

            TreeNode? impulse = result.Roots.FirstOrDefault(
                r => r.Model == ElliottModelType.IMPULSE);
            Assert.That(impulse, Is.Not.Null, "A full-range IMPULSE root must be found.");
            Assert.That(impulse!.Status, Is.EqualTo(NodeStatus.COMPLETE));
            Assert.That(impulse.RangeStartSegment, Is.EqualTo(0));
            Assert.That(impulse.RangeEndSegment, Is.EqualTo(markup.Segments.Count - 1));
            Assert.That(impulse.Children.Count, Is.EqualTo(5),
                "An impulse over five single-segment waves must have five children.");

            foreach (TreeNode root in result.Roots)
                AssertNodeObeysHardRules(root);
        }

        // ----- synthetic ZIGZAG -----------------------------------------------

        [Test]
        public void Parse_CleanZigzag_ProducesValidCompleteZigzagRoot()
        {
            // A down zigzag: A 100→60, B →80, C →40.
            var markup = new ElliottWaveExactMarkupV2(
                null, Pivots(100, 60, 80, 40));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Not.Empty);
            TreeNode? zigzag = result.Roots.FirstOrDefault(
                r => r.Model == ElliottModelType.ZIGZAG);
            Assert.That(zigzag, Is.Not.Null, "A full-range ZIGZAG root must be found.");
            Assert.That(zigzag!.Children.Count, Is.EqualTo(3),
                "A zigzag over three single-segment waves must have three children.");

            foreach (TreeNode root in result.Roots)
                AssertNodeObeysHardRules(root);
        }

        // ----- T-MK-4 determinism ---------------------------------------------

        [Test]
        public void Parse_SameInput_IsDeterministic()
        {
            IReadOnlyList<BarPoint> pivots = Pivots(0, 10, 4, 24, 16, 30);

            string first = Serialize(new ElliottWaveExactMarkupV2(null, pivots).Parse().Roots);
            string second = Serialize(new ElliottWaveExactMarkupV2(null, pivots).Parse().Roots);

            Assert.That(second, Is.EqualTo(first),
                "T-MK-4: identical input must yield an identical markup.");
        }

        [Test]
        public void Parse_SingleSegmentRange_HasNoRootsButReportsMetrics()
        {
            // Two pivots = a single segment: no multi-wave start model fits (a lone
            // zigzag leg is not a complete Elliott structure), yet the metrics for the
            // parsed range must still be reported.
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10));

            MarkupSearchResult result = markup.Parse();

            Assert.That(result.Roots, Is.Empty);
            Assert.That(result.Metrics.RangeSegments, Is.EqualTo(1));
            Assert.That(result.Metrics.Coverage, Is.EqualTo(0.0));
            Assert.That(result.Metrics.GapCount, Is.EqualTo(1));
        }

        // ----- real data, bounded range ---------------------------------------

        [TestCaseSource(nameof(DataFiles))]
        public void Parse_RealDataBoundedRange_ObeysHardRulesAndIsDeterministic(string filePath)
        {
            var provider = new TestBarsProvider(HOUR1);
            provider.LoadCandles(filePath);
            Assert.That(provider.Count, Is.GreaterThan(10), $"Too few candles in {filePath}.");

            int endIndex = Math.Min(provider.Count - 1, MAX_BARS);
            var markup = new ElliottWaveExactMarkupV2(provider, 0, endIndex, DEVIATION_PERCENT);

            if (markup.Segments.Count < 3)
                Assert.Ignore("Not enough segments at this deviation to search.");

            int endSeg = Math.Min(markup.Segments.Count - 1, REAL_RANGE_SEGMENTS - 1);

            MarkupSearchResult first = markup.ParseSegmentRange(0, endSeg);
            MarkupSearchResult second = markup.ParseSegmentRange(0, endSeg);

            Assert.That(first.Metrics.Aborted, Is.False, "The bounded range must not hit the node cap.");
            Assert.That(Serialize(second.Roots), Is.EqualTo(Serialize(first.Roots)),
                "T-MK-4: the search must be deterministic on real data.");

            foreach (TreeNode root in first.Roots)
            {
                Assert.That(root.Status, Is.EqualTo(NodeStatus.COMPLETE));
                AssertNodeObeysHardRules(root);
            }
        }

        // ----- helpers --------------------------------------------------------

        /// <summary>
        /// T-MK-2 — re-validates every composite node in the tree against the same
        /// hard-rule predicates (§7–9) the engine applies, reconstructing each node's
        /// waves from its children's pivots.
        /// </summary>
        private static void AssertNodeObeysHardRules(TreeNode node)
        {
            if (node.Children.Count > 0)
            {
                var waves = new List<ElliottWaveExactMarkupV2.Segment>(node.Children.Count);
                foreach (TreeNode child in node.Children)
                    waves.Add(new ElliottWaveExactMarkupV2.Segment(child.StartPivot, child.EndPivot));

                bool wave4Simple = waves.Count < 4
                    || !WAVE4_OVERLAP_ALLOWED.Contains(node.Children[3].Model);

                Assert.That(
                    ElliottWaveExactMarkupV2.CheckPriceRules(node.Model, waves, wave4Simple),
                    Is.EqualTo(DeathReason.NONE),
                    $"T-MK-2: {node.Model} node [{node.RangeStartSegment}..{node.RangeEndSegment}] " +
                    "violates a hard-price rule.");
                Assert.That(
                    ElliottWaveExactMarkupV2.CheckTimeWindow(node.Model, waves),
                    Is.EqualTo(DeathReason.NONE),
                    $"T-MK-2: {node.Model} node [{node.RangeStartSegment}..{node.RangeEndSegment}] " +
                    "violates a hard-time rule.");

                // Children must contiguously cover the node's range.
                Assert.That(node.Children[0].RangeStartSegment, Is.EqualTo(node.RangeStartSegment));
                Assert.That(node.Children[^1].RangeEndSegment, Is.EqualTo(node.RangeEndSegment));
                for (int k = 1; k < node.Children.Count; k++)
                    Assert.That(node.Children[k].RangeStartSegment,
                        Is.EqualTo(node.Children[k - 1].RangeEndSegment + 1),
                        "Child wave ranges must be contiguous.");
            }

            foreach (TreeNode child in node.Children)
                AssertNodeObeysHardRules(child);
        }

        /// <summary>Serializes the markup forest into a stable canonical string.</summary>
        private static string Serialize(IReadOnlyList<TreeNode> roots)
        {
            var sb = new System.Text.StringBuilder();
            foreach (TreeNode root in roots)
                Serialize(root, sb);
            return sb.ToString();
        }

        private static void Serialize(TreeNode node, System.Text.StringBuilder sb)
        {
            sb.Append('(')
                .Append(node.Model).Append(':')
                .Append(node.WavePos ?? "-").Append(':')
                .Append(node.RangeStartSegment).Append('-').Append(node.RangeEndSegment).Append(':')
                .Append(node.Level).Append(':')
                .Append(node.Score.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            foreach (TreeNode child in node.Children)
                Serialize(child, sb);
            sb.Append(')');
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
                    .SetName($"Search_{Path.GetFileNameWithoutExtension(file)}");
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
