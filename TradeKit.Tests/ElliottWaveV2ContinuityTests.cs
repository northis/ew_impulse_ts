using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 9 of EW_MARKUP_v2.md §19 — continuity (§15.3). Drives the windowed
    /// whole-history stitch (<see cref="ElliottWaveExactMarkupV2.ParseContinuous"/>)
    /// over a per-symbol auto-deviation zigzag (§4 — the period differs per symbol) and
    /// asserts the continuity invariants:
    /// <list type="bullet">
    /// <item>T-MK-1 — the tiles cover the whole input with no holes: they start at
    /// segment 0, end at the last segment, and join end-to-end.</item>
    /// <item>T-MK-2 — every multi-wave tile obeys the hard rules §7–9.</item>
    /// <item>T-MK-3 — boundary stability: extending the history on the right does not
    /// change the tiles committed over the common left interior.</item>
    /// <item>T-MK-4 — determinism: identical input ⇒ identical stitch.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2ContinuityTests
    {
        // Bars analyzed per file. Bounded so the auto-deviation zigzag (fine on liquid
        // pairs) stays quick under the windowed stitch while still covering a long,
        // multi-pattern stretch of real history.
        private const int MAX_BARS = 3000;

        // Distinct symbols / timeframes with very different price scales and volatility,
        // so the per-symbol auto-deviation (§4) genuinely varies between cases.
        private static readonly (string File, ITimeFrame Tf)[] CASES =
        {
            ("EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv", null),   // ~1.0, h1
            ("USDJPY_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv", null),  // ~150, m15
            ("XAUUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv", null),   // ~2000, h1
            ("AUDCAD_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv", null)   // ~0.9, m15
        };

        private static readonly HashSet<ElliottModelType> WAVE4_OVERLAP_ALLOWED = new()
        {
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.FLAT_REGULAR
        };

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach ((string file, ITimeFrame _) in CASES)
                yield return new TestCaseData(file)
                    .SetName($"Continuity_{Path.GetFileNameWithoutExtension(file)}");
        }

        // ----- T-MK-1 / T-MK-2 / T-MK-4 on real auto-deviation data -----------

        [TestCaseSource(nameof(Cases))]
        public void ParseContinuous_RealData_IsGapFreeRuleValidAndDeterministic(string fileName)
        {
            var markup = BuildMarkup(fileName, deviation: null, extraBars: 0);
            int n = markup.Segments.Count;
            if (n < ElliottWaveExactMarkupV2.STITCH_MIN_PATTERN_SEGMENTS)
                Assert.Ignore("Not enough segments at the auto deviation to stitch.");

            ContinuousMarkupResult result = markup.ParseContinuous();

            // T-MK-1: hole-free contiguous tiling of [0..n-1].
            AssertContiguousTiling(result.Tiles, n);

            // T-MK-2: every composite tile obeys the hard rules §7–9.
            foreach (TreeNode tile in result.Tiles)
                AssertNodeObeysHardRules(tile);

            // T-MK-4: a second stitch of the same input is identical.
            ContinuousMarkupResult again = markup.ParseContinuous();
            Assert.That(Serialize(again.Tiles), Is.EqualTo(Serialize(result.Tiles)),
                "T-MK-4: identical input must yield an identical stitch.");

            Assert.That(result.Metrics.Aborted, Is.False, "The bounded-window stitch must not abort.");
            TestContext.WriteLine(
                $"{fileName}: dev={markup.DeviationPercent:F4}% segs={n} tiles={result.Tiles.Count} " +
                $"coverage={result.Metrics.Coverage:P1} gaps={result.Metrics.GapCount} " +
                $"nodes={result.Metrics.NodesCreated} memoCells={result.Metrics.MemoCells}");
        }

        // ----- T-MK-3 boundary stability --------------------------------------

        [Test]
        public void ParseContinuous_ExtendingHistoryRight_KeepsLeftInteriorStable()
        {
            // A fixed deviation isolates the stitch's causal property from the optimizer's
            // range dependence: the zigzag pivots share an identical left prefix, so the
            // committed tiles over that prefix must be byte-identical.
            const string file = "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv";
            const double deviation = 0.5;

            var shortMarkup = BuildMarkup(file, deviation, extraBars: 0);
            var longMarkup = BuildMarkup(file, deviation, extraBars: 400);

            int commonSeg = CommonLeadingSegments(shortMarkup.Pivots, longMarkup.Pivots);
            Assert.That(commonSeg, Is.GreaterThan(2 * ElliottWaveExactMarkupV2.STITCH_MAX_WINDOW_SEGMENTS),
                "Expected a sizeable common zigzag prefix to test stability over.");

            IReadOnlyList<TreeNode> shortTiles = shortMarkup.ParseContinuous().Tiles;
            IReadOnlyList<TreeNode> longTiles = longMarkup.ParseContinuous().Tiles;

            // Tiles whose whole window lies inside the common prefix are causally
            // determined by identical segments, hence must match one-for-one.
            int stableEnd = commonSeg - ElliottWaveExactMarkupV2.STITCH_MAX_WINDOW_SEGMENTS;
            int compared = 0;
            for (int k = 0; k < shortTiles.Count && k < longTiles.Count; k++)
            {
                if (shortTiles[k].RangeEndSegment >= stableEnd)
                    break;

                Assert.That(Serialize(longTiles[k]), Is.EqualTo(Serialize(shortTiles[k])),
                    $"T-MK-3: tile #{k} over the common interior changed when bars were appended.");
                compared++;
            }

            Assert.That(compared, Is.GreaterThan(0),
                "Expected at least one committed tile inside the stable interior.");
            TestContext.WriteLine(
                $"Stability: commonSeg={commonSeg} stableEnd={stableEnd} comparedTiles={compared} " +
                $"(short tiles={shortTiles.Count}, long tiles={longTiles.Count})");
        }

        // ----- helpers --------------------------------------------------------

        private static ElliottWaveExactMarkupV2 BuildMarkup(string fileName, double? deviation, int extraBars)
        {
            ITimeFrame tf = fileName.Contains("_m15_") ? TimeFrameHelper.Minute15 : TimeFrameHelper.Hour1;
            var provider = new TestBarsProvider(tf);
            provider.LoadCandles(Path.Combine(FindDataDir(), fileName));
            Assert.That(provider.Count, Is.GreaterThan(100), $"Too few candles in {fileName}.");

            int endIndex = Math.Min(provider.Count - 1, MAX_BARS + extraBars);
            return new ElliottWaveExactMarkupV2(provider, 0, endIndex, deviation);
        }

        /// <summary>T-MK-1: the tiles cover [0..n-1] contiguously with no holes or overlaps.</summary>
        private static void AssertContiguousTiling(IReadOnlyList<TreeNode> tiles, int n)
        {
            Assert.That(tiles, Is.Not.Empty, "A non-empty input must produce at least one tile.");
            Assert.That(tiles[0].RangeStartSegment, Is.EqualTo(0), "The first tile must start at segment 0.");
            Assert.That(tiles[^1].RangeEndSegment, Is.EqualTo(n - 1), "The last tile must end at the final segment.");

            for (int k = 0; k < tiles.Count; k++)
            {
                Assert.That(tiles[k].RangeEndSegment, Is.GreaterThanOrEqualTo(tiles[k].RangeStartSegment),
                    $"Tile #{k} has an inverted range.");
                Assert.That(tiles[k].Status, Is.EqualTo(NodeStatus.COMPLETE),
                    $"Tile #{k} must be COMPLETE.");
                if (k > 0)
                    Assert.That(tiles[k].RangeStartSegment, Is.EqualTo(tiles[k - 1].RangeEndSegment + 1),
                        $"T-MK-1: tile #{k} does not join the previous tile end-to-end.");
            }
        }

        /// <summary>T-MK-2: re-validate every composite node against the hard rules §7–9.</summary>
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
                    $"T-MK-2: {node.Model} tile [{node.RangeStartSegment}..{node.RangeEndSegment}] breaks a price rule.");
                Assert.That(
                    ElliottWaveExactMarkupV2.CheckTimeWindow(node.Model, waves),
                    Is.EqualTo(DeathReason.NONE),
                    $"T-MK-2: {node.Model} tile [{node.RangeStartSegment}..{node.RangeEndSegment}] breaks a time rule.");

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

        /// <summary>Number of leading zigzag segments identical between two pivot lists.</summary>
        private static int CommonLeadingSegments(IReadOnlyList<BarPoint> a, IReadOnlyList<BarPoint> b)
        {
            int max = Math.Min(a.Count, b.Count);
            int i = 0;
            while (i < max && a[i].BarIndex == b[i].BarIndex && a[i].Value.Equals(b[i].Value))
                i++;
            return i - 1; // segment s needs pivots s and s+1
        }

        private static string Serialize(IReadOnlyList<TreeNode> nodes)
        {
            var sb = new System.Text.StringBuilder();
            foreach (TreeNode node in nodes)
                Serialize(node, sb);
            return sb.ToString();
        }

        private static string Serialize(TreeNode node)
        {
            var sb = new System.Text.StringBuilder();
            Serialize(node, sb);
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
