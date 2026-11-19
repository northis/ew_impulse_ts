using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the corrected diagonal setup (DIAGONAL.md §9.44): the diagonal
    /// is <b>not</b> traded in its own direction. It is a context filter. After a completed
    /// diagonal an impulse A runs <b>against</b> it, the correction B retraces
    /// <c>EnterRatio</c> of A, and the second impulse C is traded in the direction of A
    /// (wave C or 3 of the new move). The stop is behind the start of A - the diagonal's
    /// actual end - and the targets are 1.0 (the end of A) and 1.272 of A measured from the
    /// start of A.
    /// <para>
    /// The simulation is the shared <see cref="DiagonalAbcSimulator"/>, the same code the
    /// ReplayViewer page uses. Writes <c>reports/diagonal_abc_reversal.md</c> and
    /// <c>reports/diagonal_abc_reversal_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalAbcReversal"</c>. Scope is
    /// controlled by <c>ABC_SCOPE</c> (primary/full), <c>ABC_SET</c> and <c>ABC_FILE</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private const string ABC_REVERSAL_REPORT = "diagonal_abc_reversal.md";
        private const string ABC_REVERSAL_CSV = "diagonal_abc_reversal_samples.csv";

        private const double REVERSAL_COST_RATE = 0.0002d;
        private const double REVERSAL_STRESS_COST_RATE = 0.0005d;

        private const int REVERSAL_MAX_WAIT_H1 = 100;
        private const int REVERSAL_MAX_WAIT_M15 = 200;
        private const int REVERSAL_MAX_HOLD_H1 = 200;
        private const int REVERSAL_MAX_HOLD_M15 = 400;

        private static readonly double[] REVERSAL_ENTER_RATIOS = { 0.382d, 0.5d, 0.618d };

        private static readonly AbcTargetSpec[] REVERSAL_TARGETS =
        {
            new("TP1 1.0", Ladder: false, Target1: 1.0d, Target2: 1.0d),
            new("TP1 1.272", Ladder: false, Target1: 1.272d, Target2: 1.272d),
            new("L 1.0/1.272", Ladder: true, Target1: 1.0d, Target2: 1.272d),
            new("L 1.0/1.618", Ladder: true, Target1: 1.0d, Target2: 1.618d)
        };

        private static readonly DiagonalAbcExecution[] REVERSAL_EXECUTIONS =
        {
            DiagonalAbcExecution.TRIGGER,
            DiagonalAbcExecution.NEXT_OPEN,
            DiagonalAbcExecution.CLOSE
        };

        private static readonly DiagonalInitialMode[] REVERSAL_MODES =
        {
            DiagonalInitialMode.ALL,
            DiagonalInitialMode.LEADING,
            DiagonalInitialMode.ENDING
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_abc_reversal.md")]
        [Category("Research")]
        public void DiagonalAbcReversal_Report()
        {
            string dataDir = FindDataDir();
            string scope = (Environment.GetEnvironmentVariable("ABC_SCOPE") ?? "primary")
                .Trim().ToLowerInvariant();
            string? onlySet = Environment.GetEnvironmentVariable("ABC_SET");
            string? onlyFile = Environment.GetEnvironmentVariable("ABC_FILE");

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

            List<AbcReversalSpec> specs = BuildAbcReversalSpecs();
            var results = new List<AbcReversalFileResult>();
            var sync = new object();

            Parallel.ForEach(
                sets.SelectMany(s => s.Files.Select(f => (Set: s.Name, File: f))),
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                item =>
                {
                    AbcReversalFileResult result = RunAbcReversalFile(
                        dataDir, item.Set, item.File, specs);
                    lock (sync)
                    {
                        results.Add(result);
                    }

                    TestContext.Out.WriteLine(
                        $"{item.Set}/{item.File}: bars={result.Bars}, " +
                        $"diagonals={result.Anchors}, a-legs={result.ALegs}, " +
                        $"attempts={result.Trades.Count}");
                });

            Assert.That(results, Is.Not.Empty, "The ABC reversal replay produced no results.");
            Assert.That(results.Sum(r => r.Trades.Count), Is.GreaterThan(0),
                "The ABC reversal simulation produced no attempts.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, ABC_REVERSAL_REPORT);
            File.WriteAllText(reportPath, BuildAbcReversalReport(results, scope, sets.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, ABC_REVERSAL_CSV);
            File.WriteAllText(csvPath, BuildAbcReversalCsv(results));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (AbcReversalFileResult result in results)
                result.Provider = null!;
        }

        #region Replay

        private static AbcReversalFileResult RunAbcReversalFile(
            string dataDir,
            string set,
            string file,
            IReadOnlyList<AbcReversalSpec> specs)
        {
            ITimeFrame timeFrame = file.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(Path.Combine(dataDir, file));

            var result = new AbcReversalFileResult
            {
                Set = set,
                File = file,
                Bars = provider.Count,
                Provider = provider
            };

            bool isM15 = file.Contains("_m15_");
            int impulsePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            DiagonalAbcPivotTimeline timeline =
                DiagonalAbcPivotTimeline.Build(provider, impulsePeriod);

            foreach (DiagonalInitialMode mode in REVERSAL_MODES)
            {
                DiagonalRunConfig runConfig = EXACT_CORRIDOR with
                {
                    Name = mode.ToString(),
                    InitialMode = mode
                };

                List<Sample> samples = RunDiagonalSamples(provider, file, runConfig);
                var seen = new HashSet<(int, int, bool)>();
                samples = samples
                    .Where(s => seen.Add((s.P0Bar, s.SignalBar, s.IsUpDiagonal)))
                    .ToList();

                result.Anchors += samples.Count;
                result.AnchorsByMode.TryGetValue(mode, out int seenInMode);
                result.AnchorsByMode[mode] = seenInMode + samples.Count;

                foreach (Sample sample in samples)
                {
                    var setup = new DiagonalAbcSetup(
                        sample.IsUpDiagonal,
                        sample.SignalBar,
                        sample.EntryTime,
                        sample.P0Bar,
                        sample.V0,
                        sample.P4Bar,
                        sample.V4,
                        sample.P5Bar,
                        sample.V5,
                        sample.W3);

                    foreach (AbcReversalSpec spec in specs)
                    {
                        var parameters = new DiagonalAbcParams(
                            EnterRatio: spec.EnterRatio,
                            MinARatio: 0.236d,
                            MinABars: 3,
                            AStartToleranceW3: 0.10d,
                            AStartBarTolerance: 2,
                            StopBuffer: 0d,
                            Target1Ratio: spec.Target1,
                            Ladder: spec.Ladder,
                            Target2Ratio: spec.Target2,
                            CostRate: REVERSAL_COST_RATE,
                            StressCostRate: REVERSAL_STRESS_COST_RATE,
                            Execution: spec.Execution,
                            MaxWaitBars: isM15 ? REVERSAL_MAX_WAIT_M15 : REVERSAL_MAX_WAIT_H1,
                            MaxHoldBars: isM15 ? REVERSAL_MAX_HOLD_M15 : REVERSAL_MAX_HOLD_H1);

                        DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                            provider, setup, parameters, timeline);

                        if (trade.AStartBar >= 0)
                            result.ALegs++;

                        result.Trades.Add(new AbcReversalTrade
                        {
                            Set = set,
                            Mode = mode,
                            File = file,
                            Symbol = sample.Symbol,
                            Spec = spec,
                            Trade = trade
                        });
                    }
                }
            }

            return result;
        }

        private static List<AbcReversalSpec> BuildAbcReversalSpecs()
        {
            var specs = new List<AbcReversalSpec>();
            foreach (DiagonalAbcExecution execution in REVERSAL_EXECUTIONS)
            {
                foreach (double enterRatio in REVERSAL_ENTER_RATIOS)
                {
                    foreach (AbcTargetSpec target in REVERSAL_TARGETS)
                    {
                        specs.Add(new AbcReversalSpec(
                            execution, enterRatio, target));
                    }
                }
            }

            return specs;
        }

        #endregion

        #region Report

        private static string BuildAbcReversalReport(
            IReadOnlyList<AbcReversalFileResult> results,
            string scope,
            int fileSets)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal → impulse A → correction B → trade C");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(
                "**The setup.** A completed 0-1-2-3-4-5 contracting diagonal is only a " +
                "context filter. After it, the first impulse **A** runs **against** the " +
                "diagonal (starts at the diagonal's actual extreme), the correction **B** " +
                "retraces `EnterRatio` of A, and the second impulse **C** is traded in the " +
                "direction of A (wave C or 3 of the new move, i.e. against the diagonal). " +
                "The stop is behind the start of A = the diagonal end. Targets are measured " +
                "from the start of A: **1.0 = the end of A** (conservative) and **1.272 = " +
                "the usual extension**.");
            builder.AppendLine();
            builder.AppendLine(
                $"Files: {fileSets} set(s), scope `{scope}`. Exact corridor " +
                "(`W2/W1 >= 0.5`, `W4/W3 <= 0.382`), `MinARatio = 0.236`, `MinABars = 3`, " +
                "A start within 10% of `|W3|` and 2 bars of the diagonal extreme. " +
                "Costs 2 bp (5 bp stress), stop-first on a tie. " +
                "`TRIGGER` is a limit at the correction level, `CLOSE`/`NEXT_OPEN` are the " +
                "conservative executions without a same-bar fill.");
            builder.AppendLine();

            AppendAbcReversalMain(builder, results);
            AppendAbcReversalModes(builder, results);
            AppendAbcReversalTrainTest(builder, results);
            AppendAbcReversalSide(builder, results);
            AppendAbcReversalPairs(builder, results);
            AppendAbcReversalVerdict(builder, results);

            return builder.ToString();
        }

        private static void AppendAbcReversalMain(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine("## Main results");
            builder.AppendLine();
            builder.AppendLine("| Set | Exec | B | Targets | Diagonals | A legs | Entries | Fill | Resolved | Skip/C | win | netR | netR 5bp | R:R | MFE/R | MAE/R |");
            builder.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (string set in SetsOf(results))
            {
                List<AbcReversalTrade> setTrades = results
                    .Where(r => r.Set == set)
                    .SelectMany(r => r.Trades)
                    .ToList();
                int anchors = results
                    .Where(r => r.Set == set)
                    .Sum(r => r.AnchorsByMode.TryGetValue(
                        DiagonalInitialMode.ALL, out int count) ? count : 0);

                foreach (DiagonalAbcExecution execution in REVERSAL_EXECUTIONS)
                {
                    foreach (double enterRatio in REVERSAL_ENTER_RATIOS)
                    {
                        foreach (AbcTargetSpec target in REVERSAL_TARGETS)
                        {
                            List<AbcReversalTrade> slice = setTrades
                                .Where(t => t.Mode == DiagonalInitialMode.ALL &&
                                            t.Spec.Execution == execution &&
                                            Math.Abs(t.Spec.EnterRatio - enterRatio) < 1e-9 &&
                                            t.Spec.Target.Name == target.Name)
                                .ToList();
                            if (slice.Count == 0)
                                continue;

                            AbcReversalStats stats = AbcReversalStat(slice);
                            int filled = slice.Count(t => t.Trade.EntryBar >= 0);
                            builder.AppendLine(FormattableString.Invariant(
                                $"| {set} | {Abbrev(execution)} | {enterRatio:F3} | {target.Name} | {anchors} | {slice.Count(t => t.Trade.AStartBar >= 0)} | {filled} | {(double)filled / Math.Max(1, slice.Count):P1} | {stats.Resolved} | {stats.Skipped} | {stats.Win:P1} | {stats.NetR:F3} | {stats.StressNetR:F3} | {stats.Rr:F2} | {stats.Mfe:F2} | {stats.Mae:F2} |"));
                        }
                    }
                }
            }
        }

        private static void AppendAbcReversalModes(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine();
            builder.AppendLine("## By diagonal mode — primary cell (B=0.50, NEXT_OPEN, L 1.0/1.272)");
            builder.AppendLine();
            builder.AppendLine("| Set | Mode | Diagonals | Entries | Resolved | win | netR | netR 5bp |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            foreach (string set in SetsOf(results))
            {
                foreach (DiagonalInitialMode mode in REVERSAL_MODES)
                {
                    List<AbcReversalTrade> slice = PrimarySlice(results, set, mode);
                    if (slice.Count == 0)
                        continue;

                    AbcReversalStats stats = AbcReversalStat(slice);
                    int filled = slice.Count(t => t.Trade.EntryBar >= 0);
                    int anchors = results
                        .Where(r => r.Set == set)
                        .Sum(r => r.AnchorsByMode.TryGetValue(mode, out int count) ? count : 0);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {mode} | {anchors} | {filled} | {stats.Resolved} | {stats.Win:P1} | {stats.NetR:F3} | {stats.StressNetR:F3} |"));
                }
            }
        }

        private static void AppendAbcReversalTrainTest(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine();
            builder.AppendLine("## Train / test — primary cell");
            builder.AppendLine();
            builder.AppendLine("Train 2019–2022, test 2023–2026. Entries before 2019 are shown " +
                               "separately so no trade is dropped.");
            builder.AppendLine();
            builder.AppendLine("| Set | Period | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");

            foreach (string set in SetsOf(results))
            {
                foreach ((string name, int from, int to) in new[]
                         {
                             ("pre-2019", 0, 2018),
                             ("2019–2022", 2019, 2022),
                             ("2023–2026", 2023, 9999)
                         })
                {
                    List<AbcReversalTrade> slice = PrimarySlice(results, set, null)
                        .Where(t => YearOf(t.Trade) >= from && YearOf(t.Trade) <= to)
                        .ToList();
                    if (slice.Count == 0)
                        continue;

                    AbcReversalStats stats = AbcReversalStat(slice);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {set} | {name} | {stats.N} | {stats.Win:P1} | {stats.NetR:F3} |"));
                }
            }
        }

        private static void AppendAbcReversalSide(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine();
            builder.AppendLine("## By trade side — primary cell, all h1 pooled");
            builder.AppendLine();
            builder.AppendLine("The side is the direction of the traded impulse A, i.e. " +
                               "**against** the diagonal.");
            builder.AppendLine();
            builder.AppendLine("| Side | N | Resolved | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");

            List<AbcReversalTrade> h1 = PrimarySlice(results, null, null)
                .Where(t => !t.File.Contains("_m15_"))
                .ToList();

            foreach ((string name, bool up) in new[] { ("long", true), ("short", false) })
            {
                List<AbcReversalTrade> slice = h1
                    .Where(t => !t.Trade.IsUpDiagonal == up)
                    .ToList();
                if (slice.Count == 0)
                    continue;

                AbcReversalStats stats = AbcReversalStat(slice);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {name} | {stats.N} | {stats.Resolved} | {stats.Win:P1} | {stats.NetR:F3} | {stats.StressNetR:F3} |"));
            }
        }

        private static void AppendAbcReversalPairs(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine();
            builder.AppendLine("## By pair — primary cell, all h1 pooled");
            builder.AppendLine();
            builder.AppendLine("| Pair | N | Resolved | win | netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");

            List<AbcReversalTrade> h1 = PrimarySlice(results, null, null)
                .Where(t => !t.File.Contains("_m15_"))
                .ToList();

            foreach (IGrouping<string, AbcReversalTrade> pair in h1
                         .GroupBy(t => t.Symbol)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AbcReversalStats stats = AbcReversalStat(pair.ToList());
                builder.AppendLine(FormattableString.Invariant(
                    $"| {pair.Key} | {stats.N} | {stats.Resolved} | {stats.Win:P1} | {stats.NetR:F3} |"));
            }
        }

        private static void AppendAbcReversalVerdict(
            StringBuilder builder,
            IReadOnlyList<AbcReversalFileResult> results)
        {
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            builder.AppendLine("Primary cell: `B = 0.50`, `NEXT_OPEN`, ladder `TP1 1.0 + TP2 1.272`.");
            builder.AppendLine();
            builder.AppendLine("| Set | N | win | netR | netR 5bp |");
            builder.AppendLine("|---|---:|---:|---:|---:|");

            foreach (string set in SetsOf(results))
            {
                AbcReversalStats stats = AbcReversalStat(PrimarySlice(results, set, null));
                builder.AppendLine(FormattableString.Invariant(
                    $"| {set} | {stats.N} | {stats.Win:P1} | {stats.NetR:F3} | {stats.StressNetR:F3} |"));
            }

            builder.AppendLine();

            List<AbcReversalTrade> h1 = PrimarySlice(results, null, null)
                .Where(t => !t.File.Contains("_m15_"))
                .ToList();
            List<AbcReversalTrade> test = h1
                .Where(t => YearOf(t.Trade) >= 2023)
                .ToList();
            List<AbcReversalTrade> train = h1
                .Where(t => YearOf(t.Trade) is >= 2019 and <= 2022)
                .ToList();

            builder.AppendLine("Checks for a bot:");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"- Unseen sets: fresh `{AbcReversalStat(PrimarySlice(results, "fresh", null)).NetR:F3}R`, holdout `{AbcReversalStat(PrimarySlice(results, "holdout", null)).NetR:F3}R`."));
            builder.AppendLine(FormattableString.Invariant(
                $"- h1 pooled train 2019–2022 `{AbcReversalStat(train).NetR:F3}R` ({train.Count}), test 2023–2026 `{AbcReversalStat(test).NetR:F3}R` ({test.Count})."));
            builder.AppendLine(FormattableString.Invariant(
                $"- m15 fresh `{AbcReversalStat(PrimarySlice(results, "m15-fresh", null)).NetR:F3}R`."));
            builder.AppendLine();
            builder.AppendLine("**How to read this.** The diagonal is only a filter; the actual " +
                               "edge must come from the A-B-C structure. A positive primary cell " +
                               "on tuning, fresh and holdout is the minimum before any bot. " +
                               "`TRIGGER` is the limit order the setup describes; `NEXT_OPEN` and " +
                               "`CLOSE` are the honest checks without an intrabar fill. The " +
                               "5 bp column is the cost stress.");
        }

        #endregion

        #region Helpers

        private static List<AbcReversalTrade> PrimarySlice(
            IReadOnlyList<AbcReversalFileResult> results,
            string? set,
            DiagonalInitialMode? mode)
        {
            return results
                .Where(r => set == null || r.Set == set)
                .SelectMany(r => r.Trades)
                .Where(t => t.Spec.IsPrimary &&
                            (mode == null || t.Mode == mode))
                .ToList();
        }

        private static IEnumerable<string> SetsOf(
            IReadOnlyList<AbcReversalFileResult> results)
        {
            foreach (string set in new[] { "tuning", "fresh", "holdout", "m15-fresh" })
            {
                if (results.Any(r => r.Set == set))
                    yield return set;
            }
        }

        private static int YearOf(DiagonalAbcTrade trade)
        {
            return trade.EntryBar >= 0 ? trade.EntryTime.Year : trade.SignalTime.Year;
        }

        private static string Abbrev(DiagonalAbcExecution execution)
        {
            return execution switch
            {
                DiagonalAbcExecution.TRIGGER => "trigger",
                DiagonalAbcExecution.CLOSE => "close",
                _ => "nextopen"
            };
        }

        private static AbcReversalStats AbcReversalStat(
            IReadOnlyList<AbcReversalTrade> trades)
        {
            List<AbcReversalTrade> filled = trades
                .Where(t => t.Trade.EntryBar >= 0)
                .ToList();
            List<AbcReversalTrade> resolved = filled
                .Where(t => t.Trade.Resolved)
                .ToList();

            return new AbcReversalStats(
                filled.Count,
                resolved.Count,
                trades.Count(t => t.Trade.Outcome == "SKIP"),
                resolved.Count == 0
                    ? 0
                    : resolved.Count(t => t.Trade.NetR > 0) / (double)resolved.Count,
                resolved.Count == 0 ? 0 : resolved.Average(t => t.Trade.NetR),
                resolved.Count == 0 ? 0 : resolved.Average(t => t.Trade.StressNetR),
                resolved.Count == 0 ? 0 : resolved.Average(t => t.Trade.PlannedRr),
                filled.Count == 0 ? 0 : filled.Average(t => t.Trade.MfeR),
                filled.Count == 0 ? 0 : filled.Average(t => t.Trade.MaeR));
        }

        private static string BuildAbcReversalCsv(
            IReadOnlyList<AbcReversalFileResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "set,mode,file,symbol,signal_time,signal_bar,is_up_diagonal,diagonal_height," +
                "a_start_bar,a_end_bar,a_start,a_end,a_height,b_end_bar,b_end,b_depth," +
                "setup_worked,worked_bar,worked_price,a_to_diagonal,execution,enter_ratio," +
                "ladder,target1_ratio,target2_ratio,entry_time,entry_bar,entry,stop,tp1,tp2,risk," +
                "planned_rr,entry_depth,bars_a,bars_to_fill,outcome,exit_bar,exit_price,target1_hit," +
                "target2_hit,gross_r,cost_r,net_r,stress_net_r,resolved,profitable,mfe_r,mae_r," +
                "bars_held,no_fill_reason");

            foreach (AbcReversalFileResult result in results
                         .OrderBy(r => r.Set, StringComparer.Ordinal)
                         .ThenBy(r => r.File, StringComparer.Ordinal))
            {
                foreach (AbcReversalTrade trade in result.Trades
                             .OrderBy(t => t.Trade.SignalTime)
                             .ThenBy(t => t.Mode)
                             .ThenBy(t => t.Spec.Name, StringComparer.Ordinal))
                {
                    DiagonalAbcTrade t = trade.Trade;
                    builder.AppendLine(string.Join(",",
                        RevCsv(trade.Set),
                        RevCsv(trade.Mode.ToString()),
                        RevCsv(trade.File),
                        RevCsv(trade.Symbol),
                        RevCsv(t.SignalTime.ToString("o", CultureInfo.InvariantCulture)),
                        t.SignalBar.ToString(CultureInfo.InvariantCulture),
                        RevB(t.IsUpDiagonal),
                        RevF(t.DiagonalHeight),
                        t.AStartBar.ToString(CultureInfo.InvariantCulture),
                        t.AEndBar.ToString(CultureInfo.InvariantCulture),
                        RevF(t.AStart),
                        RevF(t.AEnd),
                        RevF(t.AHeight),
                        t.BEndBar.ToString(CultureInfo.InvariantCulture),
                        RevF(t.BEnd),
                        RevF(t.BDepth),
                        RevB(t.SetupWorked),
                        t.WorkedBar.ToString(CultureInfo.InvariantCulture),
                        RevF(t.WorkedPrice),
                        RevF(t.DiagonalHeight > 0 ? t.AHeight / t.DiagonalHeight : 0),
                        RevCsv(Abbrev(trade.Spec.Execution)),
                        RevF(trade.Spec.EnterRatio),
                        RevB(trade.Spec.Ladder),
                        RevF(trade.Spec.Target1),
                        RevF(trade.Spec.Target2),
                        RevCsv(t.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                        t.EntryBar.ToString(CultureInfo.InvariantCulture),
                        RevF(t.Entry),
                        RevF(t.Stop),
                        RevF(t.Target1),
                        RevF(t.Target2),
                        RevF(t.Risk),
                        RevF(t.PlannedRr),
                        RevF(t.EntryDepth),
                        t.BarsToEnd.ToString(CultureInfo.InvariantCulture),
                        t.BarsToFill.ToString(CultureInfo.InvariantCulture),
                        RevCsv(t.Outcome),
                        t.ExitBar.ToString(CultureInfo.InvariantCulture),
                        RevF(t.ExitPrice),
                        RevB(t.Target1Hit),
                        RevB(t.Target2Hit),
                        RevF(t.GrossR),
                        RevF(t.CostR),
                        RevF(t.NetR),
                        RevF(t.StressNetR),
                        RevB(t.Resolved),
                        RevB(t.Profitable),
                        RevF(t.MfeR),
                        RevF(t.MaeR),
                        t.BarsHeld.ToString(CultureInfo.InvariantCulture),
                        RevCsv(t.NoFillReason)));
                }
            }

            return builder.ToString();
        }

        private static string RevF(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string RevB(bool value)
        {
            return value ? "1" : "0";
        }

        private static string RevCsv(string value)
        {
            return value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        #endregion

        #region Types

        private sealed record AbcTargetSpec(
            string Name,
            bool Ladder,
            double Target1,
            double Target2);

        private sealed record AbcReversalSpec(
            DiagonalAbcExecution Execution,
            double EnterRatio,
            AbcTargetSpec Target)
        {
            public string Name =>
                $"{Abbrev(Execution)}/{EnterRatio:F3}/{Target.Name}";

            public bool Ladder => Target.Ladder;
            public double Target1 => Target.Target1;
            public double Target2 => Target.Target2;

            /// <summary>
            /// The pre-registered cell for the bot decision: the honest next-open execution
            /// (no same-bar fill), the 50% correction and the 100%+127% ladder.
            /// </summary>
            public bool IsPrimary =>
                Execution == DiagonalAbcExecution.NEXT_OPEN &&
                Math.Abs(EnterRatio - 0.5d) < 1e-9 &&
                Target.Name == "L 1.0/1.272";
        }

        private sealed class AbcReversalFileResult
        {
            public string Set { get; init; } = string.Empty;
            public string File { get; init; } = string.Empty;
            public int Bars { get; init; }
            public TestBarsProvider Provider { get; set; } = null!;
            public int Anchors { get; set; }
            public int ALegs { get; set; }
            public Dictionary<DiagonalInitialMode, int> AnchorsByMode { get; } = new();
            public List<AbcReversalTrade> Trades { get; } = new();
        }

        private sealed class AbcReversalTrade
        {
            public string Set { get; init; } = string.Empty;
            public DiagonalInitialMode Mode { get; init; }
            public string File { get; init; } = string.Empty;
            public string Symbol { get; init; } = string.Empty;
            public AbcReversalSpec Spec { get; init; } = null!;
            public DiagonalAbcTrade Trade { get; init; } = null!;
        }

        private sealed record AbcReversalStats(
            int N,
            int Resolved,
            int Skipped,
            double Win,
            double NetR,
            double StressNetR,
            double Rr,
            double Mfe,
            double Mae);

        #endregion
    }
}
