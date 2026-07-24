using System.Diagnostics;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Full-archive research harness for the harmonic finder. Excluded from the normal test
    /// run; writes <c>reports/harmonic_csv_sweep.md</c>.
    /// <para>
    /// Run: <c>dotnet test --filter "FullyQualifiedName~HarmonicCsvSweep"</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness - run manually to (re)generate reports/harmonic_csv_sweep.md")]
    [Category("Research")]
    public class HarmonicCsvSweepTests
    {
        private sealed class Stats
        {
            public string File { get; init; } = string.Empty;
            public HarmonicPatternType PatternType { get; init; }
            public int Enters { get; set; }
            public int TakeProfits { get; set; }
            public int StopLosses { get; set; }
            public int Open => Enters - TakeProfits - StopLosses;
            public List<double> RiskRewards { get; } = new();
            public List<double> Results { get; } = new();

            public double WinRate => Enters == 0 || TakeProfits + StopLosses == 0
                ? 0d
                : (double)TakeProfits / (TakeProfits + StopLosses);

            public double AverageR => Results.Count == 0 ? 0d : Results.Average();
            public double AverageRiskReward => RiskRewards.Count == 0 ? 0d : RiskRewards.Average();
        }

        [Test]
        public void SweepArchive_WriteReport()
        {
            string? dataDir = HarmonicCsvData.FindDataDir();
            string? repoRoot = HarmonicCsvData.FindRepoRoot();
            if (dataDir == null || repoRoot == null)
            {
                Assert.Inconclusive("The local price archive was not found.");
                return;
            }

            string[] files = Directory.GetFiles(dataDir, "*.csv")
                .Select(Path.GetFileName)
                .Where(a => a != null && (a.Contains("_h1_") || a.Contains("_m15_")))
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray()!;

            var perFile = new List<(string File, int Bars, long Ms, int MaxCandidates, int Enters)>();
            var perModel = new Dictionary<(string, HarmonicPatternType), Stats>();
            var parameters = new HarmonicParams();

            foreach (string file in files)
            {
                var provider = new TestBarsProvider(
                    HarmonicCsvData.GetTimeFrame(file),
                    new SymbolBaseForFile(file));
                provider.LoadCandles(Path.Combine(dataDir, file));

                if (provider.Count < 1000)
                {
                    TestContext.WriteLine($"SKIP (too short): {file}");
                    continue;
                }

                var finder = new HarmonicSetupFinder(provider, provider.BarSymbol, parameters);
                var openMap = new Dictionary<object, HarmonicSignalEventArgs>(
                    ReferenceEqualityComparer.Instance);
                int maxCandidates = 0;
                int enters = 0;

                finder.OnEnter += (_, e) =>
                {
                    enters++;
                    openMap[e.Level] = e;
                    Stats stats = Get(file, e.HarmonicItem.PatternType);
                    stats.Enters++;
                    stats.RiskRewards.Add(e.RiskReward);
                };

                finder.OnTakeProfit += (_, e) =>
                {
                    if (!openMap.TryGetValue(e.FromLevel, out HarmonicSignalEventArgs? entry))
                        return;

                    Stats stats = Get(file, entry.HarmonicItem.PatternType);
                    stats.TakeProfits++;
                    stats.Results.Add(entry.RiskReward);
                    openMap.Remove(e.FromLevel);
                };

                finder.OnStopLoss += (_, e) =>
                {
                    if (!openMap.TryGetValue(e.FromLevel, out HarmonicSignalEventArgs? entry))
                        return;

                    Stats stats = Get(file, entry.HarmonicItem.PatternType);
                    stats.StopLosses++;
                    stats.Results.Add(-1d);
                    openMap.Remove(e.FromLevel);
                };

                var watch = Stopwatch.StartNew();
                for (int i = 0; i < provider.Count; i++)
                {
                    finder.CheckBar(provider.GetOpenTime(i));
                    maxCandidates = Math.Max(maxCandidates, finder.CandidateCount);
                }

                watch.Stop();
                perFile.Add((file, provider.Count, watch.ElapsedMilliseconds, maxCandidates, enters));
                TestContext.WriteLine(
                    $"{file}: bars={provider.Count} ms={watch.ElapsedMilliseconds} " +
                    $"enters={enters} maxCandidates={maxCandidates}");

                Stats Get(string f, HarmonicPatternType type)
                {
                    if (!perModel.TryGetValue((f, type), out Stats? stats))
                    {
                        stats = new Stats { File = f, PatternType = type };
                        perModel[(f, type)] = stats;
                    }

                    return stats;
                }
            }

            Assert.That(perFile, Is.Not.Empty, "No archive file was processed.");

            string reportsDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "harmonic_csv_sweep.md");
            File.WriteAllText(outPath, BuildReport(perFile, perModel));
            TestContext.WriteLine($"Wrote {outPath}");
        }

        private static string BuildReport(
            IReadOnlyList<(string File, int Bars, long Ms, int MaxCandidates, int Enters)> perFile,
            IReadOnlyDictionary<(string, HarmonicPatternType), Stats> perModel)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Harmonic archive sweep");
            builder.AppendLine();
            builder.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            builder.AppendLine();

            long totalBars = perFile.Sum(a => (long)a.Bars);
            long totalMs = perFile.Sum(a => a.Ms);
            builder.AppendLine($"Files: {perFile.Count}, bars: {totalBars}, time: {totalMs} ms.");
            builder.AppendLine(totalBars > 0
                ? $"Baseline: {totalMs * 100_000d / totalBars:F0} ms per 100 000 bars, " +
                  $"peak candidates: {perFile.Max(a => a.MaxCandidates)}."
                : string.Empty);
            builder.AppendLine();

            builder.AppendLine("## Per file");
            builder.AppendLine();
            builder.AppendLine("| File | Bars | Setups | Peak candidates | ms |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach ((string file, int bars, long ms, int maxCandidates, int enters) in perFile)
                builder.AppendLine($"| {file} | {bars} | {enters} | {maxCandidates} | {ms} |");

            builder.AppendLine();
            builder.AppendLine("## Per file and model");
            builder.AppendLine();
            builder.AppendLine("| File | Model | Setups | TP | SL | Open | Win rate | Avg R | Avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (Stats stats in perModel.Values
                         .OrderBy(a => a.File, StringComparer.Ordinal)
                         .ThenBy(a => (int)a.PatternType))
            {
                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3} | {4} | {5} | {6:P1} | {7:F2} | {8:F2} |",
                    stats.File, stats.PatternType, stats.Enters, stats.TakeProfits,
                    stats.StopLosses, stats.Open, stats.WinRate, stats.AverageR,
                    stats.AverageRiskReward));
            }

            return builder.ToString();
        }

        private sealed class SymbolBaseForFile : Core.Common.SymbolBase
        {
            public SymbolBaseForFile(string fileName)
                : base(HarmonicCsvData.GetSymbolName(fileName),
                    HarmonicCsvData.GetSymbolName(fileName), 1, 5, 0.00001, 0.00001, 100_000)
            {
            }
        }
    }
}
