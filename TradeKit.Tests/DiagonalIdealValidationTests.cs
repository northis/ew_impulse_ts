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
    /// Out-of-sample check of the impulse-A filters found on the reference USDCHF m15
    /// 2018-08-03 setup (<see cref="DiagonalIdealFilter_Sweep"/>). The in-sample file is
    /// USDCHF m15; the out-of-sample sets are the same ones the A-B-C report uses:
    /// m15-fresh, h1-fresh, h1-holdout. The diagonal finder is replayed once per file.
    /// Run manually: <c>dotnet test --filter "FullyQualifiedName~DiagonalIdealValidate"</c>.
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private static readonly IdealCandidate[] IDEAL_CANDIDATES =
        {
            new("base 3/0.236", 3, 0.236d),
            new("MinABars=10", 10, 0.236d),
            new("MaxABars=40", 3, 0.236d, MaxABars: 40),
            new("SmoothA", 3, 0.236d, RequireSmoothA: true),
            new("MinABars=10 & MaxABars=40", 10, 0.236d, MaxABars: 40),
            new("MinABars=10 & SmoothA", 10, 0.236d, RequireSmoothA: true),
            new("MinABars=10 & max40 & smooth", 10, 0.236d, 40, true),
            new("MinABars=6 & MaxABars=40", 6, 0.236d, MaxABars: 40),
            new("W4W3<=0.236", 3, 0.236d, PostFilter: (s, _) => s.W4W3 <= 0.236d),
            new("proper wedge", 3, 0.236d,
                PostFilter: (s, _) => s.W4W3 <= 0.236d && s.W2Retr >= 0.618d)
        };

        [Test]
        [Explicit("Research harness - out-of-sample validation of the impulse-A filters")]
        [Category("Research")]
        public void DiagonalIdealValidate_Report()
        {
            string dataDir = FindDataDir();
            string? onlySet = Environment.GetEnvironmentVariable("VALIDATE_SET");

            var sets = new List<(string Name, IReadOnlyList<string> Files)>
            {
                ("in-sample", new[] { IDEAL_FILE }),
                ("m15-fresh", CONT_FRESH_M15_FILES),
                ("h1-fresh", CONT_FRESH_H1_FILES),
                ("h1-holdout", CONT_HOLDOUT_H1_FILES)
            };

            if (onlySet != null)
                sets = sets.Where(s => s.Name == onlySet).ToList();

            List<(string Set, string File)> files = sets
                .SelectMany(s => s.Files.Select(f => (Set: s.Name, File: f)))
                .Where(x => File.Exists(Path.Combine(dataDir, x.File)))
                .ToList();

            Assert.That(files, Is.Not.Empty, "No archive file was found under data/.");

            var results = new List<IdealValidationFile>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 6 },
                item =>
                {
                    IdealValidationFile result = RunIdealValidationFile(
                        dataDir, item.Set, item.File);
                    lock (sync)
                    {
                        results.Add(result);
                    }

                    TestContext.Out.WriteLine(
                        $"{item.Set}/{item.File}: bars={result.Bars} anchors={result.Anchors}");
                });

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, "diagonal_ideal_validation.md");
            File.WriteAllText(reportPath, BuildIdealValidationReport(results));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            foreach (string set in new[] { "in-sample", "m15-fresh", "h1-fresh", "h1-holdout" })
            {
                foreach (IdealCandidate candidate in IDEAL_CANDIDATES)
                {
                    IdealStats stats = IdealStatOf(results
                        .Where(r => r.Set == set)
                        .SelectMany(r => r.Attempts)
                        .Where(a => a.Candidate == candidate.Name &&
                                    a.Spec == IDEAL_SPECS[0].Name)
                        .ToList());
                    if (stats.Resolved == 0)
                        continue;
                    TestContext.Out.WriteLine(
                        $"{set,-10} {candidate.Name,-32} N={stats.Resolved,4} " +
                        $"win={stats.Win,6:P1} netR={stats.NetR,7:F3} 5bp={stats.StressNetR,7:F3}");
                }
            }
        }

        private static IdealValidationFile RunIdealValidationFile(
            string dataDir,
            string set,
            string file)
        {
            ITimeFrame timeFrame = file.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(Path.Combine(dataDir, file));

            int impulsePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            DiagonalAbcPivotTimeline timeline =
                DiagonalAbcPivotTimeline.Build(provider, impulsePeriod);

            List<Sample> samples = RunDiagonalSamples(provider, file, EXACT_CORRIDOR);
            var seen = new HashSet<(int, int, bool)>();
            samples = samples.Where(s => seen.Add((s.P0Bar, s.SignalBar, s.IsUpDiagonal)))
                .ToList();

            var result = new IdealValidationFile
            {
                Set = set,
                File = file,
                Bars = provider.Count,
                Anchors = samples.Count
            };

            // The three cells that matter for robustness.
            IdealTradeSpec[] specs =
            {
                IDEAL_SPECS[0],
                IDEAL_SPECS[1],
                IDEAL_SPECS[5]
            };

            foreach (Sample sample in samples)
            {
                var setup = new DiagonalAbcSetup(
                    sample.IsUpDiagonal, sample.SignalBar, sample.EntryTime,
                    sample.P0Bar, sample.V0, sample.P4Bar, sample.V4,
                    sample.P5Bar, sample.V5, sample.W3);

                foreach (IdealCandidate candidate in IDEAL_CANDIDATES)
                {
                    // The A leg belongs to the (candidate, spec) simulation, so it is measured
                    // on the first spec of the candidate whose attempt has an A.
                    var attempts = new List<DiagonalAbcTrade>(specs.Length);
                    ALegMetrics? aLeg = null;

                    foreach (IdealTradeSpec spec in specs)
                    {
                        var parameters = new DiagonalAbcParams(
                            EnterRatio: spec.EnterRatio,
                            MinARatio: candidate.MinARatio,
                            MinABars: candidate.MinABars,
                            MaxABars: candidate.MaxABars,
                            RequireSmoothA: candidate.RequireSmoothA,
                            AStartToleranceW3: 0.10d,
                            AStartBarTolerance: 2,
                            StopBuffer: 0d,
                            Target1Ratio: spec.Target1,
                            Ladder: spec.Ladder,
                            Target2Ratio: spec.Target2,
                            CostRate: 0.0002d,
                            StressCostRate: 0.0005d,
                            Execution: spec.Execution,
                            MaxWaitBars: 300,
                            MaxHoldBars: 500);

                        DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                            provider, setup, parameters, timeline);

                        if (aLeg == null && trade.AStartBar >= 0)
                            aLeg = MeasureALeg(provider, trade);

                        attempts.Add(trade);
                    }

                    if (candidate.PostFilter != null &&
                        !candidate.PostFilter(sample, aLeg))
                        continue;

                    for (int i = 0; i < specs.Length; i++)
                    {
                        result.Attempts.Add(new IdealValidationAttempt(
                            candidate.Name, specs[i].Name, attempts[i]));
                    }
                }
            }

            return result;
        }

        #region Report

        private static IdealStats IdealStatOf(IReadOnlyList<IdealValidationAttempt> attempts)
        {
            return IdealStatOfTrades(attempts.Select(a => a.Trade).ToList());
        }

        private static string BuildIdealValidationReport(
            IReadOnlyList<IdealValidationFile> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal → impulse — out-of-sample check of the impulse-A filters");
            builder.AppendLine();
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Generated: {0:yyyy-MM-dd HH:mm} UTC. Primary cell " +
                "`trigger/0.50/L1.0-1.272`; costs 2 bp (5 bp stress).",
                DateTime.UtcNow));
            builder.AppendLine();

            foreach (string spec in new[]
                     {
                         IDEAL_SPECS[0].Name,
                         IDEAL_SPECS[1].Name,
                         IDEAL_SPECS[5].Name
                     })
            {
                builder.AppendLine($"## Cell `{spec}`");
                builder.AppendLine();
                builder.AppendLine("| Set | Filter | Files | N | win | netR | netR 5bp |");
                builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");

                foreach (string set in new[] { "in-sample", "m15-fresh", "h1-fresh", "h1-holdout" })
                {
                    List<IdealValidationFile> setFiles = results
                        .Where(r => r.Set == set)
                        .ToList();
                    if (setFiles.Count == 0)
                        continue;

                    foreach (IdealCandidate candidate in IDEAL_CANDIDATES)
                    {
                        List<IdealValidationAttempt> slice = setFiles
                            .SelectMany(r => r.Attempts)
                            .Where(a => a.Candidate == candidate.Name && a.Spec == spec)
                            .ToList();
                        IdealStats stats = IdealStatOf(slice);
                        if (stats.N == 0)
                            continue;
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "| {0} | {1} | {2} | {3} | {4:P1} | {5:F3} | {6:F3} |",
                            set, candidate.Name, setFiles.Count, stats.Resolved,
                            stats.Win, stats.NetR, stats.StressNetR));
                    }
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed record IdealCandidate(
            string Name,
            int MinABars,
            double MinARatio,
            int MaxABars = 0,
            bool RequireSmoothA = false,
            Func<Sample, ALegMetrics?, bool>? PostFilter = null);

        private sealed class IdealValidationFile
        {
            public string Set { get; init; } = string.Empty;
            public string File { get; init; } = string.Empty;
            public int Bars { get; init; }
            public int Anchors { get; init; }
            public List<IdealValidationAttempt> Attempts { get; } = new();
        }

        private sealed record IdealValidationAttempt(
            string Candidate,
            string Spec,
            DiagonalAbcTrade Trade);

        #endregion
    }
}
