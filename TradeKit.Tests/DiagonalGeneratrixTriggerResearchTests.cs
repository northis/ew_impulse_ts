using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// Trigger research for the diagonal setup (DIAGONAL.md §9.24). The shipped rule enters on
    /// the close of the first candle whose wick breaks the horizontal <c>V(3)</c>; this harness
    /// also replays the same finder with the first candle whose wick crosses the extended
    /// <b>1-3 generatrix</b> — at its close and as a pending limit at the line itself. The
    /// candidate pool, wave-5 tracking and TP/SL mechanics are identical, so the tables isolate
    /// the trigger and the entry price.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_generatrix_trigger.md")]
        [Category("Research")]
        public void DiagonalGeneratrixTrigger_Report()
        {
            string dataDir = FindDataDir();
            var files = new List<string>();

            foreach (string file in H1_FILES.Concat(M15_FILES)
                         .OrderByDescending(f => new FileInfo(Path.Combine(dataDir, f)).Length))
            {
                if (File.Exists(Path.Combine(dataDir, file)))
                    files.Add(file);
                else
                    TestContext.Out.WriteLine($"SKIP (missing): {file}");
            }

            Assert.That(files, Is.Not.Empty, "No archive file was found under data/.");

            List<string> h1Files = files
                .Where(f => !f.Contains("_m15_"))
                .ToList();

            var configs = new[]
            {
                new ExactConfig("permissive", 0d, 0d, 0d, DiagonalTriggerMode.WAVE3_BREAK),
                new ExactConfig("permissive", 0d, 0d, 0d, DiagonalTriggerMode.LINE13_CLOSE),
                new ExactConfig("permissive", 0d, 0d, 0d, DiagonalTriggerMode.LINE13_PENDING),
                new ExactConfig("exact", 0.5d, 0.382d, 0d, DiagonalTriggerMode.WAVE3_BREAK),
                new ExactConfig("exact", 0.5d, 0.382d, 0d, DiagonalTriggerMode.LINE13_CLOSE),
                new ExactConfig("exact", 0.5d, 0.382d, 0d, DiagonalTriggerMode.LINE13_PENDING)
            };

            var results = new List<TriggerRun>();
            foreach (ExactConfig config in configs)
            {
                // The permissive frame is only a sanity sample on the h1 files: the exact
                // corridor on all twelve files is the trading-relevant comparison.
                IReadOnlyList<string> configFiles = config.Name == "permissive"
                    ? h1Files
                    : files;

                var diag = new Dictionary<string, int>();
                List<Sample> samples = RunExactAll(dataDir, configFiles, config, diag, 10);
                results.Add(new TriggerRun(config, samples, diag));

                TestContext.Out.WriteLine(
                    $"{config.Name} / {TriggerName(config.TriggerMode)}: " +
                    $"files={configFiles.Count}, signals={samples.Count}, " +
                    $"entered={diag.GetValueOrDefault("entered")}, " +
                    $"line13Capped={diag.GetValueOrDefault("line13Capped")}, " +
                    $"line13BeyondCap={diag.GetValueOrDefault("line13BeyondCap")}");
            }

            Assert.That(results.Any(r => r.Samples.Count > 0), Is.True,
                "The trigger study produced no signals at all.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_generatrix_trigger.md");
            File.WriteAllText(reportPath, BuildGeneratrixTriggerReport(results, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_generatrix_trigger_samples.csv");
            File.WriteAllText(csvPath, BuildGeneratrixTriggerCsv(results));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        private static string BuildGeneratrixTriggerReport(
            IReadOnlyList<TriggerRun> runs, int fileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal trigger — V(3) break vs 1-3 generatrix");
            builder.AppendLine();
            AppendInv(builder, $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            builder.AppendLine();
            AppendInv(builder, $"Files: {fileCount}. Every row is an exact finder replay with the 23.6% diagonal target. The baseline enters on the close of the first candle whose wick breaks the horizontal `V(3)` (D-W5-BREAK). The research modes enter on the first candle whose wick crosses the extended 1-3 generatrix: `1-3 close` at that candle's close, `1-3 pending` at the line itself (a limit order re-priced on every candle). The `permissive` gate set is the Phase-0 frame replayed on the h1 files only; `exact` is the W2/W4 corridor of §9.20 (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`) on all files.");
            builder.AppendLine();

            builder.AppendLine("## Overall");
            builder.AppendLine();
            builder.AppendLine("| Gate set | Trigger | Signals | worked236 | worked382 | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in runs)
            {
                Phase1Stats stats = Stats(run.Samples);
                AppendInv(builder, $"| {run.Config.Name} | {TriggerName(run.Config.TriggerMode)} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {AvgRr(run.Samples):F2} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Structural cost");
            builder.AppendLine();
            builder.AppendLine("`line13Capped` counts candidates the D-W5-CAP killed on a bar whose wick " +
                               "crossed the line; `line13BeyondCap` counts candidates the cap killed while " +
                               "the line was already beyond the |W3| ceiling, so the trigger was structurally " +
                               "unreachable. Both are diagnostics of the later trigger's structural cost, " +
                               "not counts of lost signals.");
            builder.AppendLine();
            builder.AppendLine("| Gate set | V(3) signals | 1-3 close | retention | 1-3 pending | retention | capped (close / pending) | beyond cap (close / pending) |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (string gateSet in new[] { "permissive", "exact" })
            {
                TriggerRun baseline = GetRun(runs, gateSet, DiagonalTriggerMode.WAVE3_BREAK);
                TriggerRun close = GetRun(runs, gateSet, DiagonalTriggerMode.LINE13_CLOSE);
                TriggerRun pending = GetRun(runs, gateSet, DiagonalTriggerMode.LINE13_PENDING);
                int baselineCount = baseline.Samples.Count;
                double closeRetention = baselineCount == 0
                    ? 0
                    : (double)close.Samples.Count / baselineCount;
                double pendingRetention = baselineCount == 0
                    ? 0
                    : (double)pending.Samples.Count / baselineCount;
                AppendInv(builder, $"| {gateSet} | {baselineCount} | {close.Samples.Count} | {closeRetention:P0} | {pending.Samples.Count} | {pendingRetention:P0} | {close.Diag.GetValueOrDefault("line13Capped")} / {pending.Diag.GetValueOrDefault("line13Capped")} | {close.Diag.GetValueOrDefault("line13BeyondCap")} / {pending.Diag.GetValueOrDefault("line13BeyondCap")} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Exact corridor by timeframe");
            builder.AppendLine();
            builder.AppendLine("| Trigger | Timeframe | Signals | worked236 | worked382 | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in runs.Where(r => r.Config.Name == "exact"))
            {
                foreach (IGrouping<string, Sample> group in run.Samples
                             .GroupBy(s => s.TimeFrame)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    List<Sample> subset = group.ToList();
                    Phase1Stats stats = Stats(subset);
                    AppendInv(builder, $"| {TriggerName(run.Config.TriggerMode)} | {group.Key} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {AvgRr(subset):F2} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Exact corridor by pair");
            builder.AppendLine();
            builder.AppendLine("| File | V(3) N | V(3) profit | V(3) netR | close N | close profit | close netR | pending N | pending profit | pending netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            string[] pairFiles = runs
                .Where(r => r.Config.Name == "exact")
                .SelectMany(r => r.Samples)
                .Select(s => s.File)
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            foreach (string file in pairFiles)
            {
                Phase1Stats baseline = Stats(SamplesOf(runs, "exact",
                    DiagonalTriggerMode.WAVE3_BREAK, file));
                Phase1Stats close = Stats(SamplesOf(runs, "exact",
                    DiagonalTriggerMode.LINE13_CLOSE, file));
                Phase1Stats pending = Stats(SamplesOf(runs, "exact",
                    DiagonalTriggerMode.LINE13_PENDING, file));
                AppendInv(builder, $"| `{file}` | {baseline.N} | {baseline.Profitable:P1} | {baseline.AvgNetR:F3} | {close.N} | {close.Profitable:P1} | {close.AvgNetR:F3} | {pending.N} | {pending.Profitable:P1} | {pending.AvgNetR:F3} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Entry timing and price vs the V(3) break");
            builder.AppendLine();
            builder.AppendLine("Delay is the number of bars between the first V(3) break and the 1-3 crossing. " +
                               "Entry ΔW3 is positive when the actual entry is further in the trade direction than " +
                               "the close of the V(3)-break bar (better for the counter-trend trade), in units of " +
                               "|W3|. `line dist` is how far the generatrix was beyond `V(3)` at the " +
                               "trigger, also in units of |W3|.");
            builder.AppendLine();
            builder.AppendLine("| Gate set | Trigger | N | delay 0 | 1–2 | 3–5 | 6–10 | >10 | median delay | mean entry ΔW3 | better | neutral | worse | mean line dist |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in runs.Where(r => r.Config.TriggerMode != DiagonalTriggerMode.WAVE3_BREAK))
            {
                List<Sample> subset = run.Samples.Where(s => s.V3BreakBar >= 0).ToList();
                int count = subset.Count;
                double meanEntryDelta = count == 0 ? 0 : subset.Average(s => s.EntryDeltaW3);
                double meanLineDistance = count == 0 ? 0 : subset.Average(s => s.LineDistanceW3);
                AppendInv(builder, $"| {run.Config.Name} | {TriggerName(run.Config.TriggerMode)} | {count} | {Share(subset, s => s.DelayBars == 0):P1} | {Share(subset, s => s.DelayBars is >= 1 and <= 2):P1} | {Share(subset, s => s.DelayBars is >= 3 and <= 5):P1} | {Share(subset, s => s.DelayBars is >= 6 and <= 10):P1} | {Share(subset, s => s.DelayBars > 10):P1} | {Median(subset.Select(s => s.DelayBars)):F0} | {meanEntryDelta:F3} | {Share(subset, s => s.EntryDeltaW3 > 0.05d):P1} | {Share(subset, s => Math.Abs(s.EntryDeltaW3) <= 0.05d):P1} | {Share(subset, s => s.EntryDeltaW3 < -0.05d):P1} | {meanLineDistance:F2} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Exact corridor by period");
            builder.AppendLine();
            builder.AppendLine("| Trigger | Period | Signals | worked236 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (TriggerRun run in runs.Where(r => r.Config.Name == "exact"))
            {
                foreach (string period in new[] { "2017–2020", "2021–2023", "2024–2026" })
                {
                    List<Sample> subset = run.Samples
                        .Where(s => PeriodOf(s.EntryTime) == period)
                        .ToList();
                    Phase1Stats stats = Stats(subset);
                    AppendInv(builder, $"| {TriggerName(run.Config.TriggerMode)} | {period} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## What the hypothesis says");
            builder.AppendLine();
            foreach (string gateSet in new[] { "permissive", "exact" })
            {
                Phase1Stats baseline = Stats(GetRun(runs, gateSet,
                    DiagonalTriggerMode.WAVE3_BREAK).Samples);
                Phase1Stats close = Stats(GetRun(runs, gateSet,
                    DiagonalTriggerMode.LINE13_CLOSE).Samples);
                Phase1Stats pending = Stats(GetRun(runs, gateSet,
                    DiagonalTriggerMode.LINE13_PENDING).Samples);
                AppendInv(builder, $"- **{gateSet}:** V(3) {baseline.N} signals / {baseline.Profitable:P1} profitable / {baseline.AvgNetR:F3}R; 1-3 close {close.N} / {close.Profitable:P1} / {close.AvgNetR:F3}R (Δ {close.AvgNetR - baseline.AvgNetR:+0.000;-0.000}R); 1-3 pending {pending.N} / {pending.Profitable:P1} / {pending.AvgNetR:F3}R (Δ {pending.AvgNetR - baseline.AvgNetR:+0.000;-0.000}R).");
            }

            foreach (TriggerRun run in runs
                         .Where(r => r.Config.Name == "exact" &&
                                     r.Config.TriggerMode != DiagonalTriggerMode.WAVE3_BREAK))
            {
                List<Sample> subset = run.Samples.Where(s => s.V3BreakBar >= 0).ToList();
                if (subset.Count == 0)
                    continue;

                AppendInv(builder, $"- **{TriggerName(run.Config.TriggerMode)} timing:** median {Median(subset.Select(s => s.DelayBars)):F0} bars after the V(3) break; mean entry {subset.Average(s => s.EntryDeltaW3):+0.000;-0.000}W3 vs that close; {Share(subset, s => s.EntryDeltaW3 > 0.05d):P0} better / {Share(subset, s => Math.Abs(s.EntryDeltaW3) <= 0.05d):P0} neutral / {Share(subset, s => s.EntryDeltaW3 < -0.05d):P0} worse.");
            }

            TriggerRun baselineExact = GetRun(runs, "exact", DiagonalTriggerMode.WAVE3_BREAK);
            TriggerRun closeExact = GetRun(runs, "exact", DiagonalTriggerMode.LINE13_CLOSE);
            TriggerRun pendingExact = GetRun(runs, "exact", DiagonalTriggerMode.LINE13_PENDING);
            foreach (string timeFrame in new[] { "h1", "m15" })
            {
                Phase1Stats baselineTf = Stats(baselineExact.Samples
                    .Where(s => s.TimeFrame == timeFrame).ToList());
                Phase1Stats closeTf = Stats(closeExact.Samples
                    .Where(s => s.TimeFrame == timeFrame).ToList());
                Phase1Stats pendingTf = Stats(pendingExact.Samples
                    .Where(s => s.TimeFrame == timeFrame).ToList());
                AppendInv(builder, $"- **{timeFrame} split:** V(3) {baselineTf.N} / {baselineTf.Profitable:P1} / {baselineTf.AvgNetR:F3}R; 1-3 close {closeTf.N} / {closeTf.Profitable:P1} / {closeTf.AvgNetR:F3}R; 1-3 pending {pendingTf.N} / {pendingTf.Profitable:P1} / {pendingTf.AvgNetR:F3}R.");
            }

            foreach (string gateSet in new[] { "permissive", "exact" })
            {
                List<Sample> baselineH1 = GetRun(runs, gateSet, DiagonalTriggerMode.WAVE3_BREAK)
                    .Samples.Where(s => s.TimeFrame == "h1").ToList();
                List<Sample> pendingH1 = GetRun(runs, gateSet, DiagonalTriggerMode.LINE13_PENDING)
                    .Samples.Where(s => s.TimeFrame == "h1").ToList();
                int[] years = baselineH1.Select(s => s.EntryTime.Year).Distinct().OrderBy(y => y).ToArray();
                int betterYears = years.Count(year =>
                    Stats(pendingH1.Where(s => s.EntryTime.Year == year).ToList()).AvgNetR >
                    Stats(baselineH1.Where(s => s.EntryTime.Year == year).ToList()).AvgNetR);
                AppendInv(builder, $"- **{gateSet} h1 yearly:** the pending variant beats the V(3) baseline in {betterYears}/{years.Length} years.");
            }

            Phase1Stats baselineExactStats = Stats(baselineExact.Samples);
            TriggerRun best = runs
                .Where(r => r.Config.Name == "exact" &&
                            r.Config.TriggerMode != DiagonalTriggerMode.WAVE3_BREAK)
                .OrderByDescending(r => Stats(r.Samples).AvgNetR)
                .First();
            Phase1Stats bestStats = Stats(best.Samples);
            int positivePeriods = new[] { "2017–2020", "2021–2023", "2024–2026" }
                .Count(period => Stats(best.Samples
                    .Where(s => PeriodOf(s.EntryTime) == period).ToList()).AvgNetR > 0);

            Phase1Stats h1BaselineStats = Stats(baselineExact.Samples
                .Where(s => s.TimeFrame == "h1").ToList());
            Phase1Stats h1CloseStats = Stats(closeExact.Samples
                .Where(s => s.TimeFrame == "h1").ToList());
            Phase1Stats h1PendingStats = Stats(pendingExact.Samples
                .Where(s => s.TimeFrame == "h1").ToList());
            bool h1Improves = h1PendingStats.AvgNetR > h1BaselineStats.AvgNetR + 0.02d;
            bool h1Positive = h1PendingStats.AvgNetR > 0d;

            bool improves = bestStats.AvgNetR > baselineExactStats.AvgNetR + 0.02d;
            bool positive = bestStats.AvgNetR > 0d;
            bool higherWin = bestStats.Profitable > baselineExactStats.Profitable;
            if (improves && positive && higherWin && positivePeriods >= 2)
            {
                AppendInv(builder, $"- **Verdict:** `{TriggerName(best.Config.TriggerMode)}` beats the shipped V(3) break on the exact corridor ({bestStats.N} signals, {bestStats.Profitable:P1} profitable, {bestStats.AvgNetR:F3}R, {positivePeriods}/3 positive periods). This is the first generatrix candidate worth a full walk-forward and a live order-management design.");
            }
            else if (h1Improves && h1Positive && !improves)
            {
                AppendInv(builder, $"- **Verdict:** the full exact corridor rejects the replacement: best mode `{TriggerName(best.Config.TriggerMode)}` gives {bestStats.N} signals / {bestStats.Profitable:P1} / {bestStats.AvgNetR:F3}R against {baselineExactStats.Profitable:P1} / {baselineExactStats.AvgNetR:F3}R. The h1 split is different: the pending variant gives {h1PendingStats.N} signals / {h1PendingStats.Profitable:P1} / {h1PendingStats.AvgNetR:F3}R against {h1BaselineStats.N} / {h1BaselineStats.Profitable:P1} / {h1BaselineStats.AvgNetR:F3}R, while the close variant gives {h1CloseStats.N} / {h1CloseStats.Profitable:P1} / {h1CloseStats.AvgNetR:F3}R. The idea is an **h1 execution tweak**, not a global trigger: keep V(3) as the default and test the pending variant in an h1 walk-forward.");
            }
            else if (improves && higherWin)
            {
                AppendInv(builder, $"- **Verdict:** `{TriggerName(best.Config.TriggerMode)}` improves the exact corridor ({bestStats.N} signals, {bestStats.Profitable:P1} profitable, {bestStats.AvgNetR:F3}R) but is not positive or stable enough ({positivePeriods}/3 positive periods). Keep it as a research preset; do not change the production trigger.");
            }
            else
            {
                AppendInv(builder, $"- **Verdict:** no generatrix trigger beats the shipped V(3) break on the exact corridor. Best research mode: `{TriggerName(best.Config.TriggerMode)}` with {bestStats.N} signals, {bestStats.Profitable:P1} profitable, {bestStats.AvgNetR:F3}R against {baselineExactStats.Profitable:P1} / {baselineExactStats.AvgNetR:F3}R. The later, stricter trigger does not pay for the lost sample.");
            }

            builder.AppendLine("- The pending variant is a **simulation** of a limit order placed on the " +
                               "generatrix and re-priced every candle. A live robot would have to place or " +
                               "modify the order on each bar because the line value moves with time; the close " +
                               "variant needs no order management. The pending fill is modelled at the line, or " +
                               "at the bar open when it gaps beyond the line.");
            builder.AppendLine("- The study reuses the exact finder and the same 12-file archive as §9.20–§9.23. " +
                               "The D-W5-CAP is unchanged, so a line crossing that requires more than |W3| " +
                               "beyond V(4) is structurally impossible; that cost is reported above and is not " +
                               "a parameter of this study.");

            return builder.ToString();
        }

        private static string BuildGeneratrixTriggerCsv(IReadOnlyList<TriggerRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "gate_set,trigger,file,timeframe,entry_time,signal_bar,v3_break_bar,delay_bars," +
                "entry,risk,tp,sl,line13,line_distance_w3,entry_delta_r,entry_delta_w3,entry_vs_line_r," +
                "outcome,net_r,worked236,profitable");

            foreach (TriggerRun run in runs)
            {
                foreach (Sample s in run.Samples
                             .OrderBy(s => s.File, StringComparer.Ordinal)
                             .ThenBy(s => s.EntryTime))
                {
                    builder.AppendLine(string.Join(",",
                        Csv(run.Config.Name),
                        Csv(TriggerName(run.Config.TriggerMode)),
                        Csv(s.File),
                        Csv(s.TimeFrame),
                        Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                        s.SignalBar.ToString(CultureInfo.InvariantCulture),
                        s.V3BreakBar.ToString(CultureInfo.InvariantCulture),
                        s.DelayBars.ToString(CultureInfo.InvariantCulture),
                        F(s.Entry), F(s.Risk), F(s.Tp), F(s.Sl),
                        F(s.Line13AtSignal), F(s.LineDistanceW3),
                        F(s.EntryDeltaR), F(s.EntryDeltaW3), F(s.EntryVsLineR),
                        Csv(s.Outcome), F(s.NetR), B(s.Worked236), B(s.Profitable)));
                }
            }

            return builder.ToString();
        }

        private static List<Sample> SamplesOf(
            IReadOnlyList<TriggerRun> runs,
            string gateSet,
            DiagonalTriggerMode mode,
            string file)
        {
            return GetRun(runs, gateSet, mode).Samples
                .Where(s => s.File == file)
                .ToList();
        }

        private static TriggerRun GetRun(
            IReadOnlyList<TriggerRun> runs, string gateSet, DiagonalTriggerMode mode)
        {
            return runs.First(r =>
                r.Config.Name == gateSet && r.Config.TriggerMode == mode);
        }

        private static string TriggerName(DiagonalTriggerMode mode)
        {
            return mode switch
            {
                DiagonalTriggerMode.LINE13_CLOSE => "1-3 close",
                DiagonalTriggerMode.LINE13_PENDING => "1-3 pending",
                _ => "V(3) break"
            };
        }

        private static double AvgRr(IReadOnlyList<Sample> samples)
        {
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            return resolved.Count == 0
                ? 0
                : resolved.Average(s =>
                    Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12, s.Risk));
        }

        private static double Median(IEnumerable<int> values)
        {
            int[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0)
                return 0;

            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2d
                : sorted[middle];
        }

        private static void AppendInv(StringBuilder builder, FormattableString value)
        {
            builder.AppendLine(FormattableString.Invariant(value));
        }

        private sealed record TriggerRun(
            ExactConfig Config,
            List<Sample> Samples,
            Dictionary<string, int> Diag);
    }
}
