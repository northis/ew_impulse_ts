using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 8 follow-up — calibration of the §16.2/§16.3 conditional model probabilities
    /// <c>P(child | parent, wavePos)</c>.
    /// <para>
    /// This fixture is <see cref="ExplicitAttribute">[Explicit]</see>: it is a one-off
    /// generator, not a CI assertion. It runs the windowed whole-history stitch
    /// (<see cref="ElliottWaveExactMarkupV2.ParseContinuous"/>, Step 9) at the per-symbol
    /// auto deviation over every file in <c>data/</c>, walks the COMPLETE tiles, and
    /// counts each materialized parent→child edge by wave position (§16.2 step 2).
    /// </para>
    /// <para>
    /// The empirical conditional value baked into the engine is the observed frequency
    /// renormalised to preserve each <c>(parent, wavePos)</c> slot's base-coefficient
    /// budget (§16.2 step 3):
    /// </para>
    /// <code>
    /// P(child | parent, pos) = (count / Σcount) × Σ_observed ModelProbability(child)
    /// </code>
    /// so the calibrated values stay on the same scale as the position-free fallback
    /// (<c>ModelRules[child].ProbabilityCoefficient</c>) and a singleton slot is left at
    /// its prior. Run with:
    /// <code>
    /// dotnet test --filter "FullyQualifiedName~ElliottWaveV2Calibration"
    /// </code>
    /// then paste the emitted initializer into <c>ElliottWaveExactMarkupV2.Scoring.cs</c>.
    /// </summary>
    [TestFixture, Explicit, Category("Calibration")]
    public class ElliottWaveV2CalibrationTests
    {
        // Bars analyzed per file. Bounded so a full 56-file sweep stays a few minutes
        // while each slice still spans a long, multi-pattern stretch of real history.
        private const int MAX_BARS = 4000;

        [Test]
        public void GeneratePositionProbabilityTable()
        {
            string dataDir = FindDataDir();
            string[] files = Directory.GetFiles(dataDir, "*.csv");
            Array.Sort(files, StringComparer.Ordinal);

            // edge counts: (parent, wavePos, child) -> occurrences across all COMPLETE tiles.
            var counts = new Dictionary<(ElliottModelType, string, ElliottModelType), int>();
            int filesUsed = 0;
            int tilesSeen = 0;

            foreach (string path in files)
            {
                string fileName = Path.GetFileName(path);
                ElliottWaveExactMarkupV2 markup = BuildMarkup(path, fileName);
                if (markup.Segments.Count < ElliottWaveExactMarkupV2.STITCH_MIN_PATTERN_SEGMENTS)
                    continue;

                ContinuousMarkupResult result = markup.ParseContinuous();
                filesUsed++;
                tilesSeen += result.Tiles.Count;
                foreach (TreeNode tile in result.Tiles)
                    CollectEdges(tile, counts);

                TestContext.WriteLine(
                    $"{fileName}: dev={markup.DeviationPercent:F4}% segs={markup.Segments.Count} " +
                    $"tiles={result.Tiles.Count} coverage={result.Metrics.Coverage:P1}");
            }

            Assert.That(filesUsed, Is.GreaterThan(0), "No usable data files were stitched.");
            Assert.That(counts, Is.Not.Empty, "No parent→child edges were observed.");

            // Group by (parent, wavePos); renormalise to preserve the slot's base budget.
            var groups = counts
                .GroupBy(kv => (kv.Key.Item1, kv.Key.Item2))
                .OrderBy(g => g.Key.Item1.ToString(), StringComparer.Ordinal)
                .ThenBy(g => g.Key.Item2, StringComparer.Ordinal);

            var calibrated = new List<(ElliottModelType Parent, string Pos, ElliottModelType Child, int Count, int Total, double Value)>();
            foreach (var g in groups)
            {
                int total = g.Sum(kv => kv.Value);
                double budget = g.Sum(kv => ModelProbability(kv.Key.Item3));
                foreach (var kv in g.OrderByDescending(kv => kv.Value)
                             .ThenBy(kv => kv.Key.Item3.ToString(), StringComparer.Ordinal))
                {
                    double value = (double)kv.Value / total * budget;
                    calibrated.Add((kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value, total, value));
                }
            }

            string reportsDir = Path.Combine(Directory.GetParent(dataDir)!.FullName, "reports");
            Directory.CreateDirectory(reportsDir);
            string mdPath = Path.Combine(reportsDir, "ew_v2_position_probability.md");
            string csPath = Path.Combine(reportsDir, "ew_v2_position_probability.g.cs.txt");

            File.WriteAllText(mdPath, BuildMarkdown(calibrated, filesUsed, tilesSeen));
            File.WriteAllText(csPath, BuildInitializer(calibrated));

            TestContext.WriteLine(
                $"\nfiles={filesUsed} tiles={tilesSeen} edges={counts.Count} groups={calibrated.Select(c => (c.Parent, c.Pos)).Distinct().Count()}");
            TestContext.WriteLine($"Markdown table : {mdPath}");
            TestContext.WriteLine($"C# initializer : {csPath}");
            TestContext.WriteLine("\n" + BuildInitializer(calibrated));
        }

        // ----- collection -----------------------------------------------------

        private static void CollectEdges(
            TreeNode node,
            IDictionary<(ElliottModelType, string, ElliottModelType), int> counts)
        {
            foreach (TreeNode child in node.Children)
            {
                if (child.WavePos != null)
                {
                    var key = (node.Model, child.WavePos, child.Model);
                    counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                }

                CollectEdges(child, counts);
            }
        }

        private static double ModelProbability(ElliottModelType model) =>
            ElliottWavePatternHelper.ModelRules.TryGetValue(model, out ModelRules r)
                ? r.ProbabilityCoefficient
                : 1.0;

        // ----- emit -----------------------------------------------------------

        private static string BuildMarkdown(
            IReadOnlyList<(ElliottModelType Parent, string Pos, ElliottModelType Child, int Count, int Total, double Value)> rows,
            int files, int tiles)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# EW v2 §16.3 — calibrated P(child | parent, wavePos)");
            sb.AppendLine();
            sb.AppendLine($"Generated from a single-pass stitch over {files} `data/` files " +
                          $"({tiles} top-level tiles), auto deviation, first {MAX_BARS} bars each.");
            sb.AppendLine();
            sb.AppendLine("| Позиция | Модель | Старт. коэф. (v1) | Эмпирич. P | n / total |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in rows)
            {
                sb.AppendLine(
                    $"| {r.Parent}.{r.Pos} | {r.Child} | {ModelProbability(r.Child):0.###} | " +
                    $"{r.Value:0.####} | {r.Count}/{r.Total} |");
            }

            return sb.ToString();
        }

        private static string BuildInitializer(
            IReadOnlyList<(ElliottModelType Parent, string Pos, ElliottModelType Child, int Count, int Total, double Value)> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("S_POSITION_PROBABILITY = new()");
            sb.AppendLine("{");
            foreach (var r in rows)
            {
                string v = r.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                sb.AppendLine(
                    $"    {{ (ElliottModelType.{r.Parent}, \"{r.Pos}\", ElliottModelType.{r.Child}), {v} }}, // {r.Count}/{r.Total}");
            }

            sb.AppendLine("};");
            return sb.ToString();
        }

        // ----- helpers --------------------------------------------------------

        private static ElliottWaveExactMarkupV2 BuildMarkup(string path, string fileName)
        {
            ITimeFrame tf = fileName.Contains("_m15_") ? TimeFrameHelper.Minute15 : TimeFrameHelper.Hour1;
            var provider = new TestBarsProvider(tf);
            provider.LoadCandles(path);
            int endIndex = Math.Min(provider.Count - 1, MAX_BARS);
            return new ElliottWaveExactMarkupV2(provider, 0, endIndex, null);
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
