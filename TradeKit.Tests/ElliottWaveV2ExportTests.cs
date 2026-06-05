using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 7 of EW_MARKUP_v2.md §19 — tree export (§17) and bar-by-bar replay (§17.1).
    /// Drives <see cref="EwMarkupTreeExporter"/> over deterministic synthetic pivots and a
    /// bounded slice of real <c>data/</c> candles, asserting:
    /// <list type="bullet">
    /// <item>the §17 snapshot is schema-correct (zigzag covers every pivot, the flat node
    /// list is internally linked, ranges are in-bounds) and round-trips through JSON;</item>
    /// <item>with <c>deadDepth &gt; 0</c> pruned hypotheses appear as well-formed
    /// <c>DEAD</c> nodes (§17 debug option);</item>
    /// <item>the §17.1 replay emits one chronological frame per new pivot with delta events
    /// and an alive-set that always contains the best node;</item>
    /// <item>both exports are deterministic for identical input.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2ExportTests
    {
        private const int MAX_BARS = 400;
        private const int MAX_FILES = 3;
        private const double DEVIATION_PERCENT = 0.5;

        private static readonly ITimeFrame HOUR1 = TimeFrameHelper.Hour1;
        private static readonly DateTime BASE_TIME = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

        // ----- §17 snapshot schema --------------------------------------------

        [Test]
        public void BuildSnapshot_SyntheticImpulse_IsSchemaCorrectAndLinked()
        {
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));
            MarkupSearchResult result = markup.Parse();

            EwTreeSnapshotDto snap = EwMarkupTreeExporter.BuildSnapshot(markup, result);

            Assert.That(snap.Schema, Is.EqualTo("ew-markup-tree/v2"));
            Assert.That(snap.Symbol, Is.EqualTo("SYNTHETIC"));
            Assert.That(snap.RangeStartBar, Is.EqualTo(0));
            Assert.That(snap.RangeEndBar, Is.EqualTo(10));

            // The zigzag must mirror every input pivot, with alternating high/low flags.
            Assert.That(snap.Zigzag, Has.Count.EqualTo(6));
            bool[] expectedHigh = { false, true, false, true, false, true };
            for (int i = 0; i < snap.Zigzag.Count; i++)
            {
                Assert.That(snap.Zigzag[i].BarIndex, Is.EqualTo(i * 2));
                Assert.That(snap.Zigzag[i].IsHigh, Is.EqualTo(expectedHigh[i]), $"pivot {i} high flag");
            }

            Assert.That(snap.Nodes, Is.Not.Empty, "A complete impulse must export at least the root.");
            AssertNodesAreLinked(snap, markup.Pivots.Count);
        }

        [Test]
        public void ToTreeJson_RoundTripsThroughJson()
        {
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));
            MarkupSearchResult result = markup.Parse();

            string json = EwMarkupTreeExporter.ToTreeJson(markup, result);
            var back = JsonConvert.DeserializeObject<EwTreeSnapshotDto>(json);

            Assert.That(back, Is.Not.Null);
            Assert.That(back!.Schema, Is.EqualTo("ew-markup-tree/v2"));
            Assert.That(back.Nodes, Is.Not.Empty);
            Assert.That(JObject.Parse(json)["$schema"]!.Value<string>(),
                Is.EqualTo("ew-markup-tree/v2"), "The $schema property must be emitted verbatim.");

            // The root parentId must serialize as an explicit null, not be omitted.
            JToken rootParent = JArray.FromObject(back.Nodes)[0]["parentId"]!;
            Assert.That(rootParent.Type, Is.EqualTo(JTokenType.Null));
        }

        // ----- §17 dead-branch export -----------------------------------------

        [Test]
        public void BuildSnapshot_WithDeadDepth_AttachesWellFormedDeadNodes()
        {
            // An extended-W3 impulse (W3 = a 5-segment sub-impulse). Over its nine segments
            // the top-level search tries several wave splits, the wrong ones die on hard
            // rules and are captured as dead alternatives of the live root (§17 debug).
            var markup = new ElliottWaveExactMarkupV2(
                null, Pivots(0, 10, 4, 30, 22, 50, 40, 60, 35, 70));

            MarkupSearchResult plain = markup.Parse();
            Assert.That(plain.Roots, Is.Not.Empty, "The extended impulse must be marked up.");
            int plainNodes = EwMarkupTreeExporter.BuildSnapshot(markup, plain).Nodes.Count;

            MarkupSearchResult debug = markup.Parse(deadDepth: 3);
            EwTreeSnapshotDto snap = EwMarkupTreeExporter.BuildSnapshot(markup, debug, deadDepth: 3);

            Assert.That(snap.Nodes.Count, Is.GreaterThanOrEqualTo(plainNodes),
                "Dead-branch export may only add nodes.");
            AssertNodesAreLinked(snap, markup.Pivots.Count);

            var dead = snap.Nodes.Where(n => n.Status == "DEAD").ToList();
            Assert.That(dead, Is.Not.Empty,
                "deadDepth>0 must surface at least one pruned hypothesis for this input.");
            foreach (EwNodeDto d in dead)
            {
                Assert.That(d.DeathReason, Is.Not.Null.And.Not.EqualTo("NONE"),
                    "A DEAD node must carry a real death reason.");
                Assert.That(d.Children, Is.Empty, "A pruned hypothesis is a leaf in the export.");
                Assert.That(d.ParentId, Is.Not.Null, "A DEAD node always hangs off a live parent.");
            }
        }

        [Test]
        public void ToTreeJson_WithDeadDepth_IsDeterministic()
        {
            var a = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));
            var b = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));

            string first = EwMarkupTreeExporter.ToTreeJson(a, a.Parse(deadDepth: 3), deadDepth: 3);
            string second = EwMarkupTreeExporter.ToTreeJson(b, b.Parse(deadDepth: 3), deadDepth: 3);

            Assert.That(second, Is.EqualTo(first), "Tree export must be deterministic (T-MK-4).");
        }

        // ----- §17.1 replay ---------------------------------------------------

        [Test]
        public void BuildReplay_SyntheticImpulse_EmitsChronologicalFramesWithEvents()
        {
            var markup = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));

            EwReplayDto replay = EwMarkupTreeExporter.BuildReplay(markup);

            Assert.That(replay.Schema, Is.EqualTo("ew-markup-tree-replay/v2"));
            // One frame per added pivot: pivots 2..N.
            Assert.That(replay.Frames, Has.Count.EqualTo(markup.Pivots.Count - 1));

            int prevBar = int.MinValue;
            bool sawBorn = false;
            foreach (EwReplayFrameDto frame in replay.Frames)
            {
                Assert.That(frame.BarIndex, Is.GreaterThan(prevBar), "Frames must be chronological.");
                prevBar = frame.BarIndex;

                Assert.That(frame.NewPivot, Is.Not.Null);
                Assert.That(frame.NewPivot.BarIndex, Is.EqualTo(frame.BarIndex),
                    "The new pivot must be the one that produced the frame.");

                if (frame.BestNodeId != null)
                    Assert.That(frame.AliveNodeIds, Does.Contain(frame.BestNodeId),
                        "The best node must be among the alive nodes.");

                sawBorn |= frame.Events.Any(e => e.Type == "BORN");
            }

            Assert.That(sawBorn, Is.True, "A growing structure must produce BORN events.");
        }

        [Test]
        public void ToReplayJson_IsDeterministic()
        {
            var a = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));
            var b = new ElliottWaveExactMarkupV2(null, Pivots(0, 10, 4, 24, 16, 30));

            Assert.That(EwMarkupTreeExporter.ToReplayJson(b),
                Is.EqualTo(EwMarkupTreeExporter.ToReplayJson(a)),
                "Replay export must be deterministic (T-MK-4).");
        }

        // ----- real data, bounded range ---------------------------------------

        [TestCaseSource(nameof(DataFiles))]
        public void Export_RealDataBoundedRange_IsSchemaCorrectAndDeterministic(string filePath)
        {
            var provider = new TestBarsProvider(HOUR1);
            provider.LoadCandles(filePath);
            Assert.That(provider.Count, Is.GreaterThan(10), $"Too few candles in {filePath}.");

            int endIndex = Math.Min(provider.Count - 1, MAX_BARS);
            var markup = new ElliottWaveExactMarkupV2(provider, 0, endIndex, DEVIATION_PERCENT);
            if (markup.Segments.Count < 3)
                Assert.Ignore("Not enough zigzag segments in this slice to export.");

            int endSeg = Math.Min(markup.Segments.Count - 1, 8);

            MarkupSearchResult result = markup.ParseSegmentRange(0, endSeg, deadDepth: 2);
            EwTreeSnapshotDto snap = EwMarkupTreeExporter.BuildSnapshot(markup, result, deadDepth: 2);

            Assert.That(snap.Schema, Is.EqualTo("ew-markup-tree/v2"));
            Assert.That(snap.Symbol, Is.Not.Null.And.Not.Empty);
            Assert.That(snap.Zigzag, Has.Count.EqualTo(markup.Pivots.Count));
            // A bounded real slice may not contain any complete model — an empty markup is a
            // valid (well-formed, but node-less) export.
            AssertNodesAreLinked(snap, markup.Pivots.Count);

            // The serialized JSON must be valid and stable across identical parses.
            string first = EwMarkupTreeExporter.ToTreeJson(markup, result, deadDepth: 2);
            Assert.DoesNotThrow(() => JObject.Parse(first), "Exported tree must be valid JSON.");

            MarkupSearchResult again = markup.ParseSegmentRange(0, endSeg, deadDepth: 2);
            string second = EwMarkupTreeExporter.ToTreeJson(markup, again, deadDepth: 2);
            Assert.That(second, Is.EqualTo(first), "T-MK-4: export over a fixed slice is deterministic.");
        }

        // ----- invariants -----------------------------------------------------

        /// <summary>
        /// Asserts the flat node list is a valid forest: ids are unique, every parentId and
        /// every child id resolves, ranges are in-bounds and child/parent ids agree.
        /// </summary>
        private static void AssertNodesAreLinked(EwTreeSnapshotDto snap, int pivotCount)
        {
            var byId = new Dictionary<string, EwNodeDto>();
            foreach (EwNodeDto n in snap.Nodes)
                Assert.That(byId.TryAdd(n.Id, n), Is.True, $"Duplicate node id {n.Id}.");

            int roots = 0;
            foreach (EwNodeDto n in snap.Nodes)
            {
                Assert.That(n.StartPivot, Is.InRange(0, pivotCount - 1), $"{n.Id} startPivot in bounds");
                Assert.That(n.EndPivot, Is.InRange(0, pivotCount - 1), $"{n.Id} endPivot in bounds");
                Assert.That(n.EndPivot, Is.GreaterThan(n.StartPivot), $"{n.Id} range is non-empty");

                if (n.ParentId == null)
                {
                    roots++;
                    Assert.That(n.WavePos, Is.EqualTo("root"), "A root must be labelled \"root\".");
                }
                else
                {
                    Assert.That(byId.ContainsKey(n.ParentId), Is.True,
                        $"{n.Id} references a missing parent {n.ParentId}.");
                    Assert.That(byId[n.ParentId].Children, Does.Contain(n.Id),
                        $"Parent {n.ParentId} must list child {n.Id}.");
                }

                foreach (string childId in n.Children)
                {
                    Assert.That(byId.ContainsKey(childId), Is.True,
                        $"{n.Id} references a missing child {childId}.");
                    Assert.That(byId[childId].ParentId, Is.EqualTo(n.Id),
                        $"Child {childId} must point back to {n.Id}.");
                }
            }

            // A non-empty export must form a forest; an empty markup exports no roots.
            if (snap.Nodes.Count > 0)
                Assert.That(roots, Is.GreaterThanOrEqualTo(1), "A non-empty tree needs at least one root.");
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
                    .SetName($"Export_{Path.GetFileNameWithoutExtension(file)}");
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
