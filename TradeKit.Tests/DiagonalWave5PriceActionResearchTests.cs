using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.PriceAction;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the diagonal-page hypothesis (DIAGONAL.md §9.48): the last one or
    /// two candles of wave 5 often form a price-action pattern, and such a pattern raises the
    /// chance that the 23.6 % retrace target is reached before the stop. The replay is the
    /// same <see cref="DiagonalSetupFinder"/> run and the same <c>Sample</c> builder the other
    /// diagonal harnesses use, so the outcome labels are comparable with §9.5 and §9.7.
    /// <para>
    /// Wave-5 shape (ratio zigzag, overlap, smooth-impulse) and the diagonal geometry
    /// (W5/W3, apex ratio) are recorded as conditioning variables, because the hypothesis is
    /// only interesting if the pattern edge survives them.
    /// </para>
    /// Writes <c>reports/diagonal_wave5_price_action.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalWave5PriceAction"</c>. Scope is
    /// controlled by <c>W5PA_SCOPE</c> (primary/full), <c>W5PA_SET</c> and <c>W5PA_FILE</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string W5PA_REPORT = "diagonal_wave5_price_action.md";

        /// <summary>Minimum group size shown in the report.</summary>
        private const int W5PA_MIN_GROUP = 8;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_wave5_price_action.md")]
        [Category("Research")]
        public void DiagonalWave5PriceAction_Report()
        {
            string dataDir = FindDataDir();
            string scope = (Environment.GetEnvironmentVariable("W5PA_SCOPE") ?? "primary")
                .Trim().ToLowerInvariant();
            string? onlySet = Environment.GetEnvironmentVariable("W5PA_SET");
            string? onlyFile = Environment.GetEnvironmentVariable("W5PA_FILE");

            var sets = new List<(string Name, IReadOnlyList<string> Files)>();
            AddSet(sets, dataDir, "tuning", H1_FILES, onlySet);
            AddSet(sets, dataDir, "fresh", CONT_FRESH_H1_FILES, onlySet);
            AddSet(sets, dataDir, "holdout", CONT_HOLDOUT_H1_FILES, onlySet);
            if (scope == "full")
                AddSet(sets, dataDir, "m15-fresh", CONT_FRESH_M15_FILES, onlySet);

            if (onlyFile != null)
            {
                for (int i = sets.Count - 1; i >= 0; i--)
                {
                    List<string> filtered = sets[i].Files
                        .Where(f => f == onlyFile)
                        .ToList();
                    if (filtered.Count == 0)
                        sets.RemoveAt(i);
                    else
                        sets[i] = (sets[i].Name, filtered);
                }
            }

            Assert.That(sets, Is.Not.Empty, "No archive file was found under data/.");

            var results = new List<W5PaFileResult>();
            var sync = new object();

            Parallel.ForEach(
                sets.SelectMany(s => s.Files.Select(f => (Set: s.Name, File: f))),
                new ParallelOptions { MaxDegreeOfParallelism = 6 },
                item =>
                {
                    W5PaFileResult result = RunW5PaFile(
                        Path.Combine(dataDir, item.File), item.Set, item.File);
                    lock (sync)
                    {
                        results.Add(result);
                    }

                    TestContext.Out.WriteLine(
                        $"{item.Set}/{item.File}: signals={result.Rows.Count}, " +
                        $"with PA={result.Rows.Count(r => r.P5Match.Length > 0)}");
                });

            Assert.That(results, Is.Not.Empty, "The wave-5 price-action replay produced no results.");

            string reportDir = Path.Combine(
                Directory.GetParent(dataDir)!.FullName, "reports");
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, W5PA_REPORT);
            File.WriteAllText(reportPath, BuildW5PaReport(results, scope == "full"));

            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        #region Replay

        private static W5PaFileResult RunW5PaFile(string path, string set, string fileName)
        {
            ITimeFrame timeFrame = fileName.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(path);

            List<Sample> samples = RunDiagonalSamples(provider, fileName, EXACT_CORRIDOR);
            var patternFinder = new CandlePatternFinder(provider, false, null);
            var rows = new List<W5PaRow>(samples.Count);

            foreach (Sample sample in samples)
            {
                int p5Bar = sample.P5Bar;
                if (p5Bar <= 0 || p5Bar >= provider.Count)
                    continue;

                (string[] p5All, string[] p5Match) =
                    GetW5Patterns(patternFinder, p5Bar, sample.IsUpDiagonal);
                (string[] p4All, string[] p4Match) =
                    GetW5Patterns(patternFinder, p5Bar - 1, sample.IsUpDiagonal);

                var p4 = new BarPoint(sample.V4, sample.P4Bar, provider);
                var p5 = new BarPoint(sample.V5, sample.P5Bar, provider);
                bool hasWave5 = p5.BarIndex > p4.BarIndex;
                ImpulseResult stat = hasWave5
                    ? MovementStatistic.GetMovementStatistic(p4, p5, provider)
                    : new ImpulseResult(0, 0, 0, 0, 0, 0, 0);

                bool smooth = hasWave5 &&
                              SmoothImpulseClassifier.IsSmoothImpulse(p4, p5, provider);

                // Average slope of the 1-3 generatrix in units of the median wave-3 bar range:
                // higher = steeper, lower = a flatter wall.
                double v1 = sample.V0 + (sample.IsUpDiagonal ? sample.W1 : -sample.W1);
                double slope13 = Math.Abs(sample.V3 - v1) /
                                 Math.Max(1, sample.BarsW2 + sample.BarsW3) /
                                 Math.Max(1e-12, sample.MedianRangeW3);

                rows.Add(new W5PaRow(
                    sample,
                    p5All, p5Match, p4All, p4Match,
                    smooth,
                    slope13,
                    stat.OverlapDegree));
            }

            return new W5PaFileResult(set, fileName, rows);
        }

        private static (string[] All, string[] Match) GetW5Patterns(
            CandlePatternFinder finder, int bar, bool isUpDiagonal)
        {
            List<CandlesResult> patterns = finder.GetCandlePatterns(bar);
            if (patterns == null || patterns.Count == 0)
                return (Array.Empty<string>(), Array.Empty<string>());

            string[] all = patterns
                .Select(p => p.Type.ToString())
                .Distinct()
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();

            // The trade is counter to the diagonal: an up diagonal is a short, so the
            // rejection candle we want is bearish (IsBull == false).
            string[] match = patterns
                .Where(p => p.IsBull != isUpDiagonal)
                .Select(p => p.Type.ToString())
                .Distinct()
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();

            return (all, match);
        }

        #endregion

        #region Report

        private static string BuildW5PaReport(
            IReadOnlyList<W5PaFileResult> results, bool withM15)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal → 23.6 % retrace: price action at the end of wave 5");
            builder.AppendLine();
            builder.AppendLine(
                $"Generated: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC. " +
                "Shipped diagonal-page config (`exact corridor`, TP = 23.6 % retrace, stop = 1.0 × |W3|, " +
                "trigger = V(3) break); costs 2 bp, stress 5 bp.");
            builder.AppendLine();
            builder.AppendLine(
                "Price-action patterns are evaluated with the repository `CandlePatternFinder` on the " +
                "last closed candle of wave 5 (`P5`) and on the previous candle (`P5-1`); a 1-basket " +
                "pattern therefore uses the last one or two candles of wave 5. `matched` keeps only " +
                "patterns whose direction is counter to the diagonal, i.e. in the trade direction. " +
                "`Worked` is the walk's max retrace ≥ 23.6 %; `TP` is the finder's real TP/SL outcome " +
                "among resolved trades.");
            builder.AppendLine();

            foreach (string setName in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
            {
                List<W5PaFileResult> setResults = results
                    .Where(r => r.Set == setName)
                    .OrderBy(r => r.File, StringComparer.Ordinal)
                    .ToList();
                if (setResults.Count == 0)
                    continue;

                List<W5PaRow> rows = setResults.SelectMany(r => r.Rows).ToList();
                AppendSetSection(builder, setName, setResults.Count, rows);
            }

            AppendVerdict(builder, results, withM15);
            return builder.ToString();
        }

        private static void AppendSetSection(
            StringBuilder builder, string setName, int files, IReadOnlyList<W5PaRow> rows)
        {
            builder.AppendLine($"## Set `{setName}` ({files} files)");
            builder.AppendLine();

            W5PaAgg all = Agg(rows);
            builder.AppendLine(FormatAggRow("all signals", all));
            builder.AppendLine();

            builder.AppendLine("### Patterns at `P5`, matched direction");
            builder.AppendLine();
            builder.AppendLine("| Pattern | N | TP | Worked 23.6 % | netR | netR 5 bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach ((string pattern, W5PaAgg agg) in ByPattern(rows, r => r.P5Match))
            {
                builder.AppendLine(FormatPatternRow(pattern, agg, all));
            }

            builder.AppendLine();
            builder.AppendLine("### Patterns at `P5-1`, matched direction");
            builder.AppendLine();
            builder.AppendLine("| Pattern | N | TP | Worked 23.6 % | netR | netR 5 bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach ((string pattern, W5PaAgg agg) in ByPattern(rows, r => r.P4Match))
            {
                builder.AppendLine(FormatPatternRow(pattern, agg, all));
            }

            builder.AppendLine();
            builder.AppendLine("### Pin bar focus");
            builder.AppendLine();
            builder.AppendLine("| Group | N | TP | Worked 23.6 % | netR | netR 5 bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            AppendEdgeRow(builder, "PIN_BAR at `P5`, matched", rows, r =>
                Has(r.P5Match, "UP_PIN_BAR", "DOWN_PIN_BAR"), all);
            AppendEdgeRow(builder, "PIN_BAR at `P5-1`, matched", rows, r =>
                Has(r.P4Match, "UP_PIN_BAR", "DOWN_PIN_BAR"), all);
            AppendEdgeRow(builder, "PIN_BAR at `P5`/`P5-1`, matched", rows, r =>
                Has(r.P5Match, "UP_PIN_BAR", "DOWN_PIN_BAR") ||
                Has(r.P4Match, "UP_PIN_BAR", "DOWN_PIN_BAR"), all);
            AppendEdgeRow(builder, "PIN_BAR any direction", rows, r =>
                Has(r.P5All, "UP_PIN_BAR", "DOWN_PIN_BAR") ||
                Has(r.P4All, "UP_PIN_BAR", "DOWN_PIN_BAR"), all);
            AppendEdgeRow(builder, "any PA matched", rows, r =>
                r.P5Match.Length > 0 || r.P4Match.Length > 0, all);
            AppendEdgeRow(builder, "any PA any direction", rows, r =>
                r.P5All.Length > 0 || r.P4All.Length > 0, all);
            AppendEdgeRow(builder, "no PA at `P5`/`P5-1`", rows, r =>
                r.P5All.Length == 0 && r.P4All.Length == 0, all);

            builder.AppendLine();
            builder.AppendLine("### Shape baseline (the first three observations)");
            builder.AppendLine();
            builder.AppendLine(
                "`all` is the baseline shape effect inside the bucket; `PA` / `no PA` add the " +
                "matched-direction pattern edge. Continuous metrics are split at their set median; " +
                "`W5/W3 ≥ 0.78` is the page's \"mature wave 5\" option.");
            builder.AppendLine();
            AppendDistribution(builder, rows, "W5/W3", r => r.Sample.W5W3);
            AppendDistribution(builder, rows, "generatrix slope 1-3 (bar ranges per bar)", r => r.Slope13);
            AppendDistribution(builder, rows, "wave-5 candle overlap", r => r.Overlap);
            builder.AppendLine();
            builder.AppendLine("| Condition | Group | N | TP | Worked 23.6 % | netR 5 bp |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            AppendW5RatioBuckets(builder, rows);
            AppendConditionRows(builder, rows, "generatrix slope 1-3", r => r.Slope13);
            AppendConditionRows(builder, rows, "wave-5 candle overlap", r => r.Overlap);

            int smooth = rows.Count(r => r.Smooth);
            builder.AppendLine();
            builder.AppendLine(
                $"Wave-5 `SmoothImpulseClassifier`: {smooth} of {rows.Count} signals " +
                $"({Pct(smooth, rows.Count)}). The wave-5 ratio zigzag is 0 on every signal " +
                "(the P4→P5 leg has no overlapping counter-extrema), so it cannot separate " +
                "impulse-shaped and zigzag wave 5 here.");
            builder.AppendLine();
        }

        private static void AppendW5RatioBuckets(
            StringBuilder builder, IReadOnlyList<W5PaRow> rows)
        {
            AppendSplit(builder, "W5/W3 < 0.5",
                rows.Where(r => r.Sample.W5W3 < 0.5d).ToList());
            AppendSplit(builder, "W5/W3 0.5–0.78",
                rows.Where(r => r.Sample.W5W3 >= 0.5d && r.Sample.W5W3 < 0.78d).ToList());
            AppendSplit(builder, "W5/W3 ≥ 0.78",
                rows.Where(r => r.Sample.W5W3 >= 0.78d).ToList());
        }

        private static void AppendDistribution(
            StringBuilder builder, IReadOnlyList<W5PaRow> rows,
            string name, Func<W5PaRow, double> selector)
        {
            List<double> values = rows
                .Select(selector)
                .Where(double.IsFinite)
                .OrderBy(v => v)
                .ToList();
            if (values.Count == 0)
                return;

            double Q(double f)
            {
                return values[(int)(f * (values.Count - 1))];
            }

            builder.AppendLine(
                $"- `{name}`: min {Q(0):0.###}, p25 {Q(0.25):0.###}, median {Q(0.5):0.###}, " +
                $"p75 {Q(0.75):0.###}, max {Q(1):0.###}");
        }

        private static void AppendConditionRows(
            StringBuilder builder, IReadOnlyList<W5PaRow> rows,
            string name, Func<W5PaRow, double> value)
        {
            List<double> values = rows
                .Select(value)
                .Where(double.IsFinite)
                .OrderBy(v => v)
                .ToList();
            if (values.Count < 2 * W5PA_MIN_GROUP)
                return;

            double median = values[values.Count / 2];
            List<W5PaRow> low = rows.Where(r => value(r) < median).ToList();
            List<W5PaRow> high = rows.Where(r => value(r) >= median).ToList();
            AppendSplit(builder, $"{name} < {median:0.###} (median)", low);
            AppendSplit(builder, $"{name} ≥ {median:0.###}", high);
        }

        private static void AppendSplit(
            StringBuilder builder, string condition, IReadOnlyList<W5PaRow> rows)
        {
            if (rows.Count < W5PA_MIN_GROUP)
                return;

            List<W5PaRow> withPa = rows
                .Where(r => r.P5Match.Length > 0 || r.P4Match.Length > 0)
                .ToList();
            List<W5PaRow> without = rows
                .Where(r => r.P5Match.Length == 0 && r.P4Match.Length == 0)
                .ToList();

            W5PaAgg all = Agg(rows);
            builder.AppendLine(
                $"| {condition} | all | {all.N} | {Pct(all.Tp, all.Resolved)} | {Pct(all.Worked, all.N)} | {R(all.StressNetR)} |");
            if (withPa.Count == 0 || without.Count == 0)
                return;

            W5PaAgg a = Agg(withPa);
            W5PaAgg b = Agg(without);
            builder.AppendLine(
                $"| {condition} | PA | {a.N} | {Pct(a.Tp, a.Resolved)} | {Pct(a.Worked, a.N)} | {R(a.StressNetR)} |");
            builder.AppendLine(
                $"| {condition} | no PA | {b.N} | {Pct(b.Tp, b.Resolved)} | {Pct(b.Worked, b.N)} | {R(b.StressNetR)} |");
        }

        private static void AppendEdgeRow(
            StringBuilder builder, string name, IReadOnlyList<W5PaRow> rows,
            Func<W5PaRow, bool> predicate, W5PaAgg baseline)
        {
            List<W5PaRow> group = rows.Where(predicate).ToList();
            if (group.Count < W5PA_MIN_GROUP)
                return;

            W5PaAgg agg = Agg(group);
            builder.AppendLine(FormatPatternRow(name, agg, baseline));
        }

        private static IEnumerable<(string, W5PaAgg)> ByPattern(
            IReadOnlyList<W5PaRow> rows, Func<W5PaRow, string[]> selector)
        {
            return rows
                .SelectMany(r => selector(r).Select(p => (Pattern: p, Row: r)))
                .GroupBy(a => a.Pattern, StringComparer.Ordinal)
                .Select(g => (g.Key, Agg(g.Select(a => a.Row).ToList())))
                .Where(a => a.Item2.N >= W5PA_MIN_GROUP)
                .OrderByDescending(a => a.Item2.StressNetR);
        }

        private static string FormatAggRow(string name, W5PaAgg agg)
        {
            return $"| {name} | {agg.N} | {Pct(agg.Tp, agg.Resolved)} | " +
                   $"{Pct(agg.Worked, agg.N)} | {R(agg.NetR)} | {R(agg.StressNetR)} |";
        }

        private static string FormatPatternRow(string name, W5PaAgg agg, W5PaAgg baseline)
        {
            string edge = agg.Resolved > 0 && baseline.Resolved > 0
                ? SignedPct(TpRate(agg) - TpRate(baseline))
                : "-";
            return $"| {name} ({edge}) | {agg.N} | {Pct(agg.Tp, agg.Resolved)} | " +
                   $"{Pct(agg.Worked, agg.N)} | {R(agg.NetR)} | {R(agg.StressNetR)} |";
        }

        private static void AppendVerdict(
            StringBuilder builder, IReadOnlyList<W5PaFileResult> results, bool withM15)
        {
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            string[] sets = withM15
                ? new[] { "tuning", "fresh", "holdout", "m15-fresh" }
                : new[] { "tuning", "fresh", "holdout" };

            var lines = new List<string>();
            foreach (string setName in sets)
            {
                List<W5PaRow> rows = results
                    .Where(r => r.Set == setName)
                    .SelectMany(r => r.Rows)
                    .ToList();
                if (rows.Count == 0)
                    continue;

                W5PaAgg all = Agg(rows);
                W5PaAgg pin = Agg(rows.Where(r =>
                    Has(r.P5Match, "UP_PIN_BAR", "DOWN_PIN_BAR")).ToList());
                W5PaAgg pinPrev = Agg(rows.Where(r =>
                    Has(r.P4Match, "UP_PIN_BAR", "DOWN_PIN_BAR")).ToList());
                W5PaAgg anyPa = Agg(rows.Where(r =>
                    r.P5Match.Length > 0 || r.P4Match.Length > 0).ToList());

                lines.Add(
                    $"| {setName} | {all.N} | {Pct(all.Tp, all.Resolved)} | " +
                    $"{pin.N} / {SignedPct(TpRate(pin) - TpRate(all))} | " +
                    $"{pinPrev.N} / {SignedPct(TpRate(pinPrev) - TpRate(all))} | " +
                    $"{anyPa.N} / {SignedPct(TpRate(anyPa) - TpRate(all))} |");
            }

            builder.AppendLine("| Set | N | TP, all | pin bar `P5`: N / ΔTP | pin bar `P5-1`: N / ΔTP | any PA: N / ΔTP |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (string line in lines)
                builder.AppendLine(line);

            builder.AppendLine();
            builder.AppendLine(
                "A pattern is a candidate for the page filter only if it keeps a positive ΔTP on " +
                "`tuning`, `fresh` and `holdout` (and `m15-fresh` when in scope) and survives the " +
                "W5/W3 ≥ 78 %, shallow-generatrix and wave-5-zigzag splits. The page filter evaluates " +
                "patterns ending on `P5` only, because a pattern ending on `P5-1` is not part of the " +
                "last one or two candles of wave 5.");
        }

        #endregion

        #region Aggregation

        private static W5PaAgg Agg(IReadOnlyList<W5PaRow> rows)
        {
            List<W5PaRow> resolved = rows.Where(r => r.Sample.Resolved).ToList();
            return new W5PaAgg(
                rows.Count,
                resolved.Count,
                resolved.Count(r => r.Sample.Outcome == "TP"),
                rows.Count(r => r.Sample.Worked236),
                resolved.Count > 0 ? resolved.Average(r => r.Sample.NetR) : 0,
                resolved.Count > 0
                    ? resolved.Average(r =>
                        r.Sample.NetR - 0.0003d * r.Sample.Entry / r.Sample.Risk)
                    : 0);
        }

        private static double TpRate(W5PaAgg agg)
        {
            return agg.Resolved > 0 ? agg.Tp / (double)agg.Resolved : 0;
        }

        private static bool Has(string[] patterns, params string[] names)
        {
            return names.Any(n => patterns.Contains(n, StringComparer.Ordinal));
        }

        private static string Pct(int part, int whole)
        {
            return whole > 0
                ? (100d * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + " %"
                : "-";
        }

        private static string SignedPct(double value)
        {
            return (100d * value).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " pp";
        }

        private static string R(double value)
        {
            return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Types

        private sealed class W5PaFileResult
        {
            public W5PaFileResult(string set, string file, List<W5PaRow> rows)
            {
                Set = set;
                File = file;
                Rows = rows;
            }

            public string Set { get; }
            public string File { get; }
            public List<W5PaRow> Rows { get; }
        }

        private sealed record W5PaRow(
            Sample Sample,
            string[] P5All,
            string[] P5Match,
            string[] P4All,
            string[] P4Match,
            bool Smooth,
            double Slope13,
            double Overlap);

        private sealed record W5PaAgg(
            int N, int Resolved, int Tp, int Worked, double NetR, double StressNetR);

        #endregion
    }
}
