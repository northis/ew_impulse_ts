using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;

namespace TradeKit.Tests
{
    /// <summary>
    /// Fresh-pair and walk-forward validation of the only positive region found by
    /// <see cref="DiagonalHarmonicTpSlResearchTests"/>: the moderate diagonal join
    /// (<c>±2 bars / 25% / lag 2</c>) with the shipped geometry (<c>TP1</c> / <c>D-5%</c>) or a
    /// nearer target (<c>PH 0.618</c>, <c>AD 0.618–0.786</c>).
    /// <para>
    /// The 12-file TP/SL result was largely m15-driven, so the validation runs both h1 and
    /// m15. Tuning uses the Phase 0 pairs; fresh uses the different pairs from
    /// <c>diagonal_minrr05_validation.md</c>. Every set is replayed exactly with the same
    /// harmonic search and the same diagonal corridor, so the only difference is the
    /// instrument sample.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_harmonic_validation.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalHarmonicValidation"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        private static readonly DiagonalJoinConfig VALIDATION_JOIN =
            new("moderate: ±2 bars / 25% / lag 2", MaxBarDistance: 2, MaxPriceDistance: 0.25d,
                MaxSignalLag: 2);

        /// <summary>
        /// The m15 versions of the fresh pairs. They are different instruments from the
        /// Phase 0 set, so they are a true held-out sample for the m15 result.
        /// </summary>
        private static readonly string[] FRESH_M15_FILES =
        {
            "AUDCAD_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "CADJPY_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "EURCHF_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "EURGBP_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "GBPCHF_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "NZDUSD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "USDCAD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "XAGUSD_m15_2017-12-27T18-00-00_2026-05-31T23-00-00.csv"
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_harmonic_validation.md")]
        [Category("Research")]
        public void DiagonalHarmonicValidation_Report()
        {
            string dataDir = FindDataDir();

            List<string> tuningH1 = Existing(dataDir, H1_FILES);
            List<string> freshH1 = Existing(dataDir, FRESH_H1_FILES);
            List<string> tuningM15 = Existing(dataDir, M15_FILES);
            List<string> freshM15 = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(tuningH1, Is.Not.Empty, "No tuning h1 file was found under data/.");
            Assert.That(freshH1, Is.Not.Empty, "No fresh h1 file was found under data/.");
            Assert.That(tuningM15, Is.Not.Empty, "No tuning m15 file was found under data/.");
            Assert.That(freshM15, Is.Not.Empty, "No fresh m15 file was found under data/.");

            var parameters = new HarmonicParams
            {
                MinimumStopAtr = 0d,
                MinimumRiskReward = 0d,
                MinimumScore = 0d,
                StopMode = HarmonicStopMode.PATTERN_PERCENT_BEYOND_D,
                StopPercent = 10d,
                TakeProfit1Override = null,
                TargetAnchor = HarmonicTargetAnchor.ENTRY
            };

            SetResult tuningH1Result = RunSet(dataDir, tuningH1, parameters);
            SetResult freshH1Result = RunSet(dataDir, freshH1, parameters);
            SetResult tuningM15Result = RunSet(dataDir, tuningM15, parameters);
            SetResult freshM15Result = RunSet(dataDir, freshM15, parameters);

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_harmonic_validation.md");
            File.WriteAllText(reportPath, BuildValidationReport(
                tuningH1Result, freshH1Result, tuningM15Result, freshM15Result));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static List<string> Existing(string dataDir, IReadOnlyList<string> files)
        {
            return files
                .Where(f => File.Exists(Path.Combine(dataDir, f)))
                .ToList();
        }

        private static SetResult RunSet(
            string dataDir, IReadOnlyList<string> files, HarmonicParams parameters)
        {
            List<Sample> diagonals = RunDiagonalAll(dataDir, files);
            List<HarmonicTrade> harmonic = RunHarmonicAll(dataDir, files, parameters);
            Dictionary<string, Dictionary<int, List<Sample>>> diagonalsByFile =
                IndexDiagonals(diagonals);

            List<HarmonicTrade> filtered = harmonic
                .Where(t => HasDiagonalConfluence(t, diagonalsByFile, VALIDATION_JOIN))
                .ToList();

            TestContext.Out.WriteLine(
                $"{files.Count} files: harmonic={harmonic.Count}, diagonal={diagonals.Count}, " +
                $"join={filtered.Count}");

            return new SetResult(files.Count, harmonic.Count, diagonals.Count, filtered);
        }

        private static string BuildValidationReport(
            SetResult tuningH1,
            SetResult freshH1,
            SetResult tuningM15,
            SetResult freshM15)
        {
            var variants = BuildValidationVariants();

            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal + harmonic — fresh-pair validation");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The candidate is the only region that was positive on both halves of the TP/SL sweep: the moderate diagonal join (`±2 bars / 25% / lag 2`) with a target in `TP1` / `PH 0.618` / `AD 0.618–0.786` and the `D-5%` stop. The 12-file result was largely m15-driven, so both h1 and m15 are validated on the same fresh pairs.");
            builder.AppendLine();
            builder.AppendLine("Train = 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost. The stop wins a tie.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Files | Harmonic setups | Diagonal signals | Join setups | Train | Test |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            AppendSampleRow(builder, "tuning h1", tuningH1);
            AppendSampleRow(builder, "fresh h1", freshH1);
            AppendSampleRow(builder, "tuning m15", tuningM15);
            AppendSampleRow(builder, "fresh m15", freshM15);

            AppendComparisonTable(builder, "## h1 — tuning vs fresh", tuningH1, freshH1, variants);
            AppendComparisonTable(builder, "## m15 — tuning vs fresh", tuningM15, freshM15, variants);

            AppendPairTable(builder, "## Fresh m15 by pair", freshM15, variants);
            AppendYearTable(builder, "## Tuning m15 walk-forward by year", tuningM15, variants);
            AppendPairTable(builder, "## Fresh h1 by pair", freshH1, variants);
            AppendYearTable(builder, "## Tuning h1 walk-forward by year", tuningH1, variants);

            builder.AppendLine();
            builder.AppendLine("## What this validation says");
            builder.AppendLine();
            AppendValidationConclusions(builder, tuningH1, freshH1, tuningM15, freshM15, variants);

            return builder.ToString();
        }

        private static void AppendSampleRow(StringBuilder builder, string name, SetResult set)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {set.Files} | {set.HarmonicSetups} | {set.DiagonalSignals} | {set.Trades.Count} | {set.Trades.Count(t => t.EntryTime.Year <= 2022)} | {set.Trades.Count(t => t.EntryTime.Year >= 2023)} |"));
        }

        private static void AppendComparisonTable(
            StringBuilder builder,
            string title,
            SetResult tuning,
            SetResult fresh,
            IReadOnlyList<ValidationVariant> variants)
        {
            builder.AppendLine();
            builder.AppendLine(title);
            builder.AppendLine();
            builder.AppendLine("| Variant | Tuning train N | Tuning train netR | Tuning test N | Tuning test netR | Fresh train N | Fresh train netR | Fresh test N | Fresh test netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (ValidationVariant variant in variants)
            {
                TpSlStats tuningTrain = RunVariant(tuning.Trades, variant, 0);
                TpSlStats tuningTest = RunVariant(tuning.Trades, variant, 1);
                TpSlStats freshTrain = RunVariant(fresh.Trades, variant, 0);
                TpSlStats freshTest = RunVariant(fresh.Trades, variant, 1);

                builder.AppendLine(FormattableString.Invariant(
                    $"| {variant.Name} | {tuningTrain.Trades} | {tuningTrain.NetAverageR:F3} | {tuningTest.Trades} | {tuningTest.NetAverageR:F3} | {freshTrain.Trades} | {freshTrain.NetAverageR:F3} | {freshTest.Trades} | {freshTest.NetAverageR:F3} |"));
            }
        }

        private static void AppendPairTable(
            StringBuilder builder,
            string title,
            SetResult set,
            IReadOnlyList<ValidationVariant> variants)
        {
            builder.AppendLine();
            builder.AppendLine(title);
            builder.AppendLine();
            builder.AppendLine("| File | Variant | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (IGrouping<string, HarmonicTrade> group in set.Trades
                         .GroupBy(t => t.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                foreach (ValidationVariant variant in variants)
                {
                    TpSlStats stats = RunVariant(group.ToList(), variant);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| `{group.Key}` | {variant.Name} | {stats.Trades} | {stats.WinRate:P1} | {stats.NetAverageR:F3} |"));
                }
            }
        }

        private static void AppendYearTable(
            StringBuilder builder,
            string title,
            SetResult set,
            IReadOnlyList<ValidationVariant> variants)
        {
            builder.AppendLine();
            builder.AppendLine(title);
            builder.AppendLine();
            builder.AppendLine("| Year | Variant | N | win | netR |");
            builder.AppendLine("|---|---|---:|---:|---:|");
            foreach (IGrouping<int, HarmonicTrade> group in set.Trades
                         .GroupBy(t => t.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                foreach (ValidationVariant variant in variants)
                {
                    TpSlStats stats = RunVariant(group.ToList(), variant);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {group.Key} | {variant.Name} | {stats.Trades} | {stats.WinRate:P1} | {stats.NetAverageR:F3} |"));
                }
            }
        }

        private static List<ValidationVariant> BuildValidationVariants()
        {
            int d5 = Array.FindIndex(TPSL_STOPS, s => s.Short == TPSL_REGION_STOP);
            return new List<ValidationVariant>
            {
                Variant("TP1 / D-5%", "TP1", d5),
                Variant("PH 0.618 / D-5%", "PH 0.618", d5),
                Variant("AD 0.618 / D-5%", "AD 0.618", d5),
                Variant("AD 0.786 / D-5%", "AD 0.786", d5)
            };

            static ValidationVariant Variant(string name, string targetShort, int stop)
            {
                int target = Array.FindIndex(TPSL_TARGETS, t => t.Short == targetShort);
                return new ValidationVariant(name, target, stop);
            }
        }

        private static TpSlStats RunVariant(
            IReadOnlyList<HarmonicTrade> trades, ValidationVariant variant, int? split = null)
        {
            var stats = new TpSlStats();
            var replay = new TpSlReplay(TPSL_TARGETS.Length, TPSL_STOPS.Length);

            foreach (HarmonicTrade trade in trades)
            {
                if (split.HasValue)
                {
                    bool isTest = trade.EntryTime.Year >= 2023;
                    if ((split.Value == 1) != isTest)
                        continue;
                }

                replay.Run(trade);
                replay.Score(variant.Target, variant.Stop, stats);
            }

            return stats;
        }

        private static void AppendValidationConclusions(
            StringBuilder builder,
            SetResult tuningH1,
            SetResult freshH1,
            SetResult tuningM15,
            SetResult freshM15,
            IReadOnlyList<ValidationVariant> variants)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"- Join setups: tuning h1 {tuningH1.Trades.Count}, fresh h1 {freshH1.Trades.Count}, tuning m15 {tuningM15.Trades.Count}, fresh m15 {freshM15.Trades.Count}. The fresh sets are the honest test: they were not used to pick the join, the target or the stop."));

            foreach (ValidationVariant variant in variants)
            {
                TpSlStats tuningH1Test = RunVariant(tuningH1.Trades, variant, 1);
                TpSlStats freshH1Test = RunVariant(freshH1.Trades, variant, 1);
                TpSlStats tuningM15Test = RunVariant(tuningM15.Trades, variant, 1);
                TpSlStats freshM15Test = RunVariant(freshM15.Trades, variant, 1);

                builder.AppendLine(FormattableString.Invariant(
                    $"- **{variant.Name}**: h1 tuning {tuningH1Test.NetAverageR:F3}R / {tuningH1Test.Trades}, fresh {freshH1Test.NetAverageR:F3}R / {freshH1Test.Trades}; m15 tuning {tuningM15Test.NetAverageR:F3}R / {tuningM15Test.Trades}, fresh {freshM15Test.NetAverageR:F3}R / {freshM15Test.Trades}."));
            }

            builder.AppendLine("- The m15 fresh set is the decisive one: the positive 12-file region was mostly m15, and the fresh m15 sample is larger than the fresh h1 sample. A candidate survives only if fresh m15 stays positive; a negative fresh m15 rejects it.");
            builder.AppendLine("- The fresh h1 set is too thin (a handful of test trades) to confirm or reject anything on its own.");
            builder.AppendLine("- The tuning walk-forward is reported per year. A region that needs one good year is not an edge, even if its pooled test average is positive.");
            builder.AppendLine("- The harmonic search is the shipped one with the ATR filter removed. The diagonal is the exact Phase 0/1 corridor. Any production change still needs a broker-side cost and execution check.");
        }

        private sealed record ValidationVariant(string Name, int Target, int Stop);

        private sealed class SetResult
        {
            public SetResult(
                int files, int harmonicSetups, int diagonalSignals, List<HarmonicTrade> trades)
            {
                Files = files;
                HarmonicSetups = harmonicSetups;
                DiagonalSignals = diagonalSignals;
                Trades = trades;
            }

            public int Files { get; }
            public int HarmonicSetups { get; }
            public int DiagonalSignals { get; }
            public List<HarmonicTrade> Trades { get; }
        }
    }
}
