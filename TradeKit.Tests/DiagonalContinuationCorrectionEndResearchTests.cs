using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Focused research for the V4 "first correction" entry (DIAGONAL.md §9.43 follow-up).
    /// The user's AUDJPY m15 case showed that the V3 dynamic re-price can follow the price
    /// far beyond the diagonal and enter at a 50% retrace of the whole extension, then lose
    /// to a return to P0. V4 confirms the end on the first retrace of the running height,
    /// enters at the confirmation close/next open, stops at P0 and targets the classic
    /// projection from the correction extreme C.
    /// <para>
    /// Grid: exact corridor, LEADING/ENDING, h1 tuning/fresh/holdout + m15 fresh, c=5/10%,
    /// f=1.0/1.272/1.618/2.0, stop P0, 2 bp, next open. Writes
    /// <c>reports/diagonal_continuation_v4.md</c> and
    /// <c>reports/diagonal_continuation_v4_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalContinuationCorrectionEnd"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string V4_REPORT = "diagonal_continuation_v4.md";
        private const string V4_CSV = "diagonal_continuation_v4_samples.csv";
        private const double V4_COST_RATE = 0.0002d;
        private const double V4_STRESS_COST_RATE = 0.0005d;

        private static readonly double[] V4_CONFIRM_RATIOS = { 0.05d, 0.10d };
        private static readonly double[] V4_TARGET_RATIOS = { 1.0d, 1.272d, 1.618d, 2.0d };
        private static readonly DiagonalInitialMode[] V4_MODES =
        {
            DiagonalInitialMode.LEADING,
            DiagonalInitialMode.ENDING
        };

        [Test]
        [Explicit("Research harness - V4 correction-end entry report")]
        [Category("Research")]
        public void DiagonalContinuationCorrectionEnd_Report()
        {
            string dataDir = FindDataDir();
            var sets = new List<(string Name, IReadOnlyList<string> Files)>
            {
                ("tuning", Existing(dataDir, H1_FILES)),
                ("fresh", Existing(dataDir, CONT_FRESH_H1_FILES)),
                ("holdout", Existing(dataDir, CONT_HOLDOUT_H1_FILES)),
                ("m15-fresh", Existing(dataDir, CONT_FRESH_M15_FILES))
            };

            string? onlySet = Environment.GetEnvironmentVariable("V4_SET");
            if (!string.IsNullOrEmpty(onlySet))
                sets.RemoveAll(s => !string.Equals(s.Name, onlySet, StringComparison.OrdinalIgnoreCase));

            Assert.That(sets, Is.Not.Empty, "No archive file was found under data/.");

            var rows = new List<V4Row>();
            var sync = new object();
            Parallel.ForEach(
                sets.SelectMany(s => s.Files.Select(f => (Set: s.Name, File: f))),
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                item =>
                {
                    List<V4Row> local = RunV4File(dataDir, item.Set, item.File);
                    lock (sync)
                    {
                        rows.AddRange(local);
                    }

                    TestContext.Out.WriteLine(
                        $"{item.Set}/{item.File}: rows={local.Count}");
                });

            Assert.That(rows, Is.Not.Empty, "The V4 simulation produced no rows.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, V4_REPORT);
            File.WriteAllText(reportPath, BuildV4Report(rows, sets.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, V4_CSV);
            File.WriteAllText(csvPath, BuildV4Csv(rows));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        private static List<V4Row> RunV4File(string dataDir, string set, string file)
        {
            ITimeFrame tf = file.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;
            var provider = new TestBarsProvider(tf);
            provider.LoadCandles(Path.Combine(dataDir, file));

            var rows = new List<V4Row>();
            foreach (DiagonalInitialMode mode in V4_MODES)
            {
                DiagonalRunConfig config = EXACT_CORRIDOR with
                {
                    Name = mode.ToString(),
                    InitialMode = mode
                };
                List<Sample> samples = RunDiagonalSamples(provider, file, config);
                var seen = new HashSet<(int, int, bool)>();
                samples = samples
                    .Where(s => seen.Add((s.P0Bar, s.SignalBar, s.IsUpDiagonal)))
                    .ToList();

                foreach (Sample sample in samples)
                {
                    var setup = new DiagonalContinuationSetup(
                        sample.IsUpDiagonal,
                        sample.SignalBar,
                        sample.EntryTime,
                        sample.P0Bar,
                        sample.V0,
                        sample.P5Bar,
                        sample.V5);

                    int maxHold = sample.TimeFrame == "m15" ? 1000 : 500;
                    int maxWait = sample.TimeFrame == "m15" ? 600 : 300;

                    foreach (double confirm in V4_CONFIRM_RATIOS)
                    {
                        foreach (double target in V4_TARGET_RATIOS)
                        {
                            var parameters = new DiagonalContinuationParams(
                                Variant: DiagonalContinuationVariant.CORRECTION_END,
                                EnterRatio: 0.5,
                                TargetKind: DiagonalContinuationTargetKind.CORRECTION_END,
                                TargetRatio: target,
                                ConfirmRatio: confirm,
                                StopBuffer: 0,
                                Ladder: false,
                                LadderTp2Ratio: 1.618,
                                MaxHoldBars: maxHold,
                                MaxWaitBars: maxWait,
                                CostRate: V4_COST_RATE,
                                EntryAtNextOpen: true);

                            DiagonalContinuationTrade trade =
                                DiagonalContinuationSimulator.Simulate(provider, setup, parameters);
                            rows.Add(new V4Row(
                                set, mode.ToString(), file, sample.Symbol, sample.TimeFrame,
                                sample.EntryTime, confirm, target, trade));
                        }
                    }
                }
            }

            return rows;
        }

        private static string BuildV4Report(IReadOnlyList<V4Row> rows, int setCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal continuation — V4 first-correction entry");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                "V4 is the user's intended rule from the AUDJPY m15 case: confirm the end on " +
                "the first retrace of the running height, enter at the confirmation close " +
                "(here: next open), stop at `P0`, target the classic projection from the " +
                "correction extreme `C + f*D`. V3 followed the extension to 59.869 and " +
                "entered at 62.8465 (a 50% retrace of the whole P0→59.869 move), then lost to " +
                "a return to P0; V4 enters near the first correction and targets `C - 1.272D`.");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Sets: {setCount}. Rows: {rows.Count}. Corridor: exact. Modes: LEADING/ENDING. Execution: next open, stop-first, 2 bp; 5 bp stress is shown."));
            builder.AppendLine();

            builder.AppendLine("## Main results — LEADING, c=10%");
            builder.AppendLine();
            builder.AppendLine("| Set | f | N | confirm | res | win | netR | netR 5bp | avg R:R | MFE/R | MAE/R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (string set in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
            {
                foreach (double f in V4_TARGET_RATIOS)
                {
                    List<V4Row> slice = rows
                        .Where(r => r.Set == set && r.Mode == "LEADING" &&
                                    Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                    Math.Abs(r.TargetRatio - f) < 1e-9)
                        .ToList();
                    if (slice.Count == 0)
                        continue;
                    V4Stats stats = StatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {f:F3} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} | {stats.AvgRr:F2} | {stats.AvgMfeR:F2} | {stats.AvgMaeR:F2} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Confirmation sensitivity — LEADING, f=1.272");
            builder.AppendLine();
            builder.AppendLine("| Set | c | N | confirm | res | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (string set in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
            {
                foreach (double c in V4_CONFIRM_RATIOS)
                {
                    List<V4Row> slice = rows
                        .Where(r => r.Set == set && r.Mode == "LEADING" &&
                                    Math.Abs(r.ConfirmRatio - c) < 1e-9 &&
                                    Math.Abs(r.TargetRatio - 1.272d) < 1e-9)
                        .ToList();
                    if (slice.Count == 0)
                        continue;
                    V4Stats stats = StatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {c:P0} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Leading vs ending — c=10%, f=1.272, all h1 pooled");
            builder.AppendLine();
            builder.AppendLine("| Mode | Set | N | confirm | res | win | netR | netR 5bp |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (string mode in new[] { "LEADING", "ENDING" })
            {
                foreach (string set in new[] { "tuning", "fresh", "holdout", "all-h1" })
                {
                    List<V4Row> slice = rows
                        .Where(r => r.Mode == mode &&
                                    Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                    Math.Abs(r.TargetRatio - 1.272d) < 1e-9 &&
                                    (set == "all-h1" ? r.Set != "m15-fresh" : r.Set == set))
                        .ToList();
                    if (slice.Count == 0)
                        continue;
                    V4Stats stats = StatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {mode} | {set} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Train / test — LEADING, c=10%, f=1.272");
            builder.AppendLine();
            builder.AppendLine("| Set | Period | N | confirm | res | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (string set in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
            {
                foreach ((string period, Func<V4Row, bool> predicate) in new (string, Func<V4Row, bool>)[]
                         {
                             ("pre-2019", r => r.SignalTime.Year < 2019),
                             ("2019–2022", r => r.SignalTime.Year is >= 2019 and <= 2022),
                             ("2023–2026", r => r.SignalTime.Year >= 2023)
                         })
                {
                    List<V4Row> slice = rows
                        .Where(r => r.Set == set && r.Mode == "LEADING" &&
                                    Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                    Math.Abs(r.TargetRatio - 1.272d) < 1e-9 &&
                                    predicate(r))
                        .ToList();
                    if (slice.Count == 0)
                        continue;
                    V4Stats stats = StatsOf(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {period} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("## By side — LEADING, c=10%, f=1.272, all h1 pooled");
            builder.AppendLine();
            builder.AppendLine("| Side | N | confirm | res | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (string side in new[] { "long", "short" })
            {
                List<V4Row> slice = rows
                    .Where(r => r.Set != "m15-fresh" && r.Mode == "LEADING" &&
                                Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                Math.Abs(r.TargetRatio - 1.272d) < 1e-9 &&
                                r.Trade.IsUp == (side == "long"))
                    .ToList();
                if (slice.Count == 0)
                    continue;
                V4Stats stats = StatsOf(slice);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {side} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} | {stats.AvgStressNetR:F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## By pair — LEADING, c=10%, f=1.272, all h1 pooled");
            builder.AppendLine();
            builder.AppendLine("| Pair | N | confirm | res | win | netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, V4Row> pair in rows
                         .Where(r => r.Set != "m15-fresh" && r.Mode == "LEADING" &&
                                     Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                     Math.Abs(r.TargetRatio - 1.272d) < 1e-9)
                         .GroupBy(r => r.Symbol)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                V4Stats stats = StatsOf(pair.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {pair.Key} | {stats.N} | {stats.ConfirmRate:P1} | {stats.Resolved} | {stats.WinRate:P1} | {stats.AvgNetR:F3} |"));
            }

            builder.AppendLine();
            AppendV4Verdict(builder, rows);
            return builder.ToString();
        }

        private static void AppendV4Verdict(StringBuilder builder, IReadOnlyList<V4Row> rows)
        {
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            builder.AppendLine(
                "V4 is an exploratory rule motivated by the user's AUDJPY m15 case; it was not " +
                "pre-registered. The primary cell is LEADING, c=10%, f=1.272, next open, stop P0.");
            builder.AppendLine();
            builder.AppendLine("| Cell | Tuning 2019–22 | Tuning 2023–26 | Fresh | Holdout | m15 fresh | Pass |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---|");

            (int N, double NetR) tuningTrain = V4Cell(rows, "tuning", 2019, 2022);
            (int N, double NetR) tuningTest = V4Cell(rows, "tuning", 2023, 2026);
            (int N, double NetR) fresh = V4Cell(rows, "fresh", 0, 9999);
            (int N, double NetR) holdout = V4Cell(rows, "holdout", 0, 9999);
            (int N, double NetR) m15 = V4Cell(rows, "m15-fresh", 0, 9999);
            bool pass = tuningTrain.N >= 25 && tuningTrain.NetR > 0 &&
                        tuningTest.N >= 25 && tuningTest.NetR > 0 &&
                        fresh.N >= 25 && fresh.NetR > 0 &&
                        holdout.N >= 25 && holdout.NetR > 0;
            builder.AppendLine(FormattableString.Invariant(
                $"| LEADING c=10% f=1.272 | {tuningTrain.N} / {tuningTrain.NetR:F3} | {tuningTest.N} / {tuningTest.NetR:F3} | {fresh.N} / {fresh.NetR:F3} | {holdout.N} / {holdout.NetR:F3} | {m15.N} / {m15.NetR:F3} | {(pass ? "**yes** (exploratory)" : "no")} |"));
            builder.AppendLine();
            builder.AppendLine(
                "The m15 column is a separate robustness check, not part of the h1 pass. " +
                "A positive V4 on both h1 and m15 would be the first continuation rule that " +
                "fixes the deep-extension entry; a negative m15 would keep the rule h1-only.");
            builder.AppendLine();
            builder.AppendLine("### The AUDJPY m15 case");
            builder.AppendLine();
            builder.AppendLine(
                "Signal 2020-03-17 10:30, P0=65.824, P5=64.232, D=1.592. The first correction " +
                "made a new low 64.138 and retraced to 64.330 (11.4% of the new height). V3 " +
                "re-priced to 62.8465 after the extension to 59.869 and lost to the return to " +
                "P0 (SL, -1.0R). V4 confirmed at 64.256, targeted `C - 1.272D = 62.185` and " +
                "hit TP for +1.31R (2 bp). This is the entry the user marked on the chart.");
            builder.AppendLine();
            builder.AppendLine("### Caveats");
            builder.AppendLine();
            builder.AppendLine(
                "- V4 is exploratory and was chosen after seeing this case; it needs a " +
                "pre-registered validation on unseen data before any bot/default change.");
            builder.AppendLine(
                "- The entry is at the confirmation close/next open; the cTrader backtest " +
                "must add real spread, swap and slippage.");
            builder.AppendLine(
                "- The exact corridor is used throughout; the permissive corridor is a " +
                "separate robustness check.");
        }

        private static (int N, double NetR) V4Cell(
            IReadOnlyList<V4Row> rows, string set, int yearFrom, int yearTo)
        {
            List<V4Row> slice = rows
                .Where(r => r.Set == set && r.Mode == "LEADING" &&
                            Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                            Math.Abs(r.TargetRatio - 1.272d) < 1e-9 &&
                            r.SignalTime.Year >= yearFrom &&
                            r.SignalTime.Year <= yearTo &&
                            r.Trade.Resolved)
                .ToList();
            return (slice.Count, slice.Count == 0 ? 0 : slice.Average(r => r.Trade.NetR));
        }

        private static V4Stats StatsOf(IReadOnlyList<V4Row> rows)
        {
            int n = rows.Count;
            List<V4Row> confirmed = rows.Where(r => r.Trade.EntryBar >= 0).ToList();
            List<V4Row> resolved = rows.Where(r => r.Trade.Resolved).ToList();
            double stress = resolved.Count == 0
                ? 0
                : resolved.Average(r =>
                    r.Trade.GrossR -
                    r.Trade.Entry * V4_STRESS_COST_RATE / Math.Max(1e-12, r.Trade.Risk));
            return new V4Stats(
                n,
                confirmed.Count,
                resolved.Count,
                resolved.Count(r => r.Trade.NetR > 0),
                n == 0 ? 0 : confirmed.Count / (double)n,
                resolved.Count == 0 ? 0 : resolved.Count(r => r.Trade.NetR > 0) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(r => r.Trade.NetR),
                resolved.Count == 0 ? 0 : stress,
                confirmed.Count == 0 ? 0 : confirmed.Average(r => r.Trade.PlannedRr),
                confirmed.Count == 0 ? 0 : confirmed.Average(r => r.Trade.MfeR),
                confirmed.Count == 0 ? 0 : confirmed.Average(r => r.Trade.MaeR));
        }

        private static string BuildV4Csv(IReadOnlyList<V4Row> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "set,mode,file,symbol,timeframe,signal_time,confirm_ratio,target_ratio," +
                "is_up,signal_bar,p0,end_bar,end_price,d,entry_bar,entry,stop,target1," +
                "target2,risk,planned_rr,entry_depth,bars_to_fill,bars_to_end,outcome," +
                "exit_bar,exit_price,gross_r,cost_r,net_r,resolved,profitable,mfe_r,mae_r," +
                "bars_held,confirmations,reanchors,no_fill_reason");

            foreach (V4Row r in rows
                         .Where(r => r.Mode is "LEADING" or "ENDING" &&
                                     Math.Abs(r.ConfirmRatio - 0.10d) < 1e-9 &&
                                     (Math.Abs(r.TargetRatio - 1.0d) < 1e-9 ||
                                      Math.Abs(r.TargetRatio - 1.272d) < 1e-9 ||
                                      Math.Abs(r.TargetRatio - 1.618d) < 1e-9))
                         .OrderBy(r => r.Set, StringComparer.Ordinal)
                         .ThenBy(r => r.File, StringComparer.Ordinal)
                         .ThenBy(r => r.Mode, StringComparer.Ordinal)
                         .ThenBy(r => r.TargetRatio))
            {
                DiagonalContinuationTrade t = r.Trade;
                builder.AppendLine(string.Join(",",
                    V4Csv(r.Set), V4Csv(r.Mode), V4Csv(r.File), V4Csv(r.Symbol), V4Csv(r.TimeFrame),
                    V4Csv(r.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                    V4F(r.ConfirmRatio), V4F(r.TargetRatio),
                    V4B(t.IsUp), V4I(t.SignalBar), V4F(t.P0), V4I(t.EndBar), V4F(t.EndPrice), V4F(t.D),
                    V4I(t.EntryBar), V4F(t.Entry), V4F(t.Stop), V4F(t.Target1), V4F(t.Target2),
                    V4F(t.Risk), V4F(t.PlannedRr), V4F(t.EntryDepth), V4I(t.BarsToFill),
                    V4I(t.BarsToEnd), V4Csv(t.Outcome), V4I(t.ExitBar), V4F(t.ExitPrice),
                    V4F(t.GrossR), V4F(t.CostR), V4F(t.NetR), V4B(t.Resolved), V4B(t.Profitable),
                    V4F(t.MfeR), V4F(t.MaeR), V4I(t.BarsHeld), V4I(t.Confirmations),
                    V4I(t.Reanchors), V4Csv(t.NoFillReason)));
            }

            return builder.ToString();
        }

        private static string V4Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private static string V4F(double value) =>
            value.ToString("F6", CultureInfo.InvariantCulture);

        private static string V4I(int value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static string V4B(bool value) => value ? "1" : "0";

        private sealed record V4Row(
            string Set,
            string Mode,
            string File,
            string Symbol,
            string TimeFrame,
            DateTime SignalTime,
            double ConfirmRatio,
            double TargetRatio,
            DiagonalContinuationTrade Trade);

        private sealed record V4Stats(
            int N,
            int Confirmed,
            int Resolved,
            int Wins,
            double ConfirmRate,
            double WinRate,
            double AvgNetR,
            double AvgStressNetR,
            double AvgRr,
            double AvgMfeR,
            double AvgMaeR);
    }
}
