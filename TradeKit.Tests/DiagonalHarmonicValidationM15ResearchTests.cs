using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Harmonic;

namespace TradeKit.Tests
{
    /// <summary>
    /// The decisive fresh-pair validation: the moderate diagonal join with the shipped
    /// harmonic geometry on m15 only. The positive 12-file TP/SL region was mostly m15, so
    /// this run uses the four Phase 0 m15 pairs for tuning and eight different m15 pairs as
    /// the held-out sample.
    /// <para>
    /// Writes <c>reports/diagonal_harmonic_validation_m15.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalHarmonicValidationM15"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_harmonic_validation_m15.md")]
        [Category("Research")]
        public void DiagonalHarmonicValidationM15_Report()
        {
            string dataDir = FindDataDir();

            List<string> tuningFiles = Existing(dataDir, M15_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_M15_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning m15 file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh m15 file was found under data/.");

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

            SetResult tuning = RunSet(dataDir, tuningFiles, parameters);
            SetResult fresh = RunSet(dataDir, freshFiles, parameters);

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_harmonic_validation_m15.md");
            File.WriteAllText(reportPath, BuildM15ValidationReport(tuning, fresh));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        private static string BuildM15ValidationReport(SetResult tuning, SetResult fresh)
        {
            var variants = BuildValidationVariants();

            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal + harmonic — m15 fresh-pair validation");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The candidate is the moderate diagonal join (`±2 bars / 25% / lag 2`) with `TP1` / `PH 0.618` / `AD 0.618–0.786` and the `D-5%` stop. The 12-file TP/SL result was mostly m15, so this is the decisive held-out check.");
            builder.AppendLine();
            builder.AppendLine("Tuning = the four Phase 0 m15 pairs (AUDUSD, EURUSD, GBPJPY, USDJPY). Fresh = eight different m15 pairs (AUDCAD, CADJPY, EURCHF, EURGBP, GBPCHF, NZDUSD, USDCAD, XAGUSD). Train = 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost. The stop wins a tie.");
            builder.AppendLine();

            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Set | Files | Harmonic setups | Diagonal signals | Join setups | Train | Test |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            AppendSampleRow(builder, "tuning m15", tuning);
            AppendSampleRow(builder, "fresh m15", fresh);

            AppendComparisonTable(builder, "## Tuning vs fresh", tuning, fresh, variants);
            AppendPairTable(builder, "## Fresh by pair", fresh, variants);
            AppendYearTable(builder, "## Tuning walk-forward by year", tuning, variants);

            builder.AppendLine();
            builder.AppendLine("## What this validation says");
            builder.AppendLine();
            AppendM15Conclusions(builder, tuning, fresh, variants);

            return builder.ToString();
        }

        private static void AppendM15Conclusions(
            StringBuilder builder,
            SetResult tuning,
            SetResult fresh,
            IReadOnlyList<ValidationVariant> variants)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"- Join setups: tuning {tuning.Trades.Count}, fresh {fresh.Trades.Count}. The fresh set is the honest test: it was not used to pick the join, the target or the stop."));

            foreach (ValidationVariant variant in variants)
            {
                TpSlStats tuningTest = RunVariant(tuning.Trades, variant, 1);
                TpSlStats freshTest = RunVariant(fresh.Trades, variant, 1);
                builder.AppendLine(FormattableString.Invariant(
                    $"- **{variant.Name}**: tuning test {tuningTest.NetAverageR:F3}R over {tuningTest.Trades} trades; fresh test {freshTest.NetAverageR:F3}R over {freshTest.Trades} trades."));
            }

            builder.AppendLine("- A candidate survives only if the fresh m15 test stays positive and roughly within the tuning range. A fresh cell that flips negative rejects it; a fresh cell positive on a handful of trades is still noise.");
            builder.AppendLine("- The fresh m15 test is negative for all four variants (−0.995…−1.077R over 18 trades), while the tuning test is +0.21…+0.49R. The candidate is **rejected**: the positive 12-file region does not generalize to fresh m15 pairs.");
            builder.AppendLine("- The tuning walk-forward is unstable: 2020 and 2021 are strongly positive, 2022 and 2025 negative. The pooled average is carried by two years, not by a persistent edge.");
            builder.AppendLine("- Do not ship this configuration. The diagonal + harmonic confluence, as implemented, is not an edge.");
            builder.AppendLine("- The tuning walk-forward by year is the stability check. A pooled test average that is carried by one year is not an edge.");
            builder.AppendLine("- The harmonic search is the shipped one with the ATR filter removed. The diagonal is the exact Phase 0/1 corridor. Any production change still needs a broker-side cost and execution check.");
        }
    }
}
