using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the post-diagonal correction (DIAGONAL.md §9.27). The existing
    /// Phase 0 labels (<c>worked236</c>) measure the retrace from the <b>running</b> wave-5
    /// extreme and stop at the theoretical invalidation, so a shallow wiggle inside an
    /// extending wave 5 counts as a "worked" diagonal and a correction that arrives after
    /// the stop is invisible.
    /// <para>
    /// This harness asks a different question: after the diagonal has actually ended, how
    /// deep did the correction go, and can that depth be told apart ex ante from the ones
    /// that produced no noticeable correction? The diagonal end is the last extreme in the
    /// diagonal's direction over a forward window (3× the skeleton duration, 50–1000 bars);
    /// the depth is the maximum retrace from that end as a share of the actual diagonal
    /// height, of <c>|W3|</c> and of the signal risk. A setup is <c>worked</c> when the
    /// retrace reaches 23.6% of the height and <c>tradeable</c> when it does so without the
    /// theoretical stop <c>V(4) ± |W3|</c> being hit first.
    /// </para>
    /// <para>
    /// The permissive sample (all beauty gates off) is the unbiased classifier sample; the
    /// exact corridor (<c>W2/W1 ≥ 0.5</c>, <c>W4/W3 ≤ 0.382</c>) is the tradeable subset.
    /// Tuning pairs are the Phase 0/1 files, fresh pairs are the §9.23 holdout. Writes
    /// <c>reports/diagonal_correction_outcome.md</c> and
    /// <c>reports/diagonal_correction_outcome_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalCorrectionOutcome"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        /// <summary>Forward window as a multiple of the skeleton duration (P0 → P4).</summary>
        private const double CORRECTION_HORIZON_MULTIPLIER = 3.0d;

        private const int CORRECTION_MIN_HORIZON = 50;
        private const int CORRECTION_MAX_HORIZON = 1000;

        /// <summary>Signals with fewer forward bars than this are not labelled at all.</summary>
        private const int CORRECTION_MIN_FORWARD_BARS = 50;

        /// <summary>
        /// The last extreme must have at least this many bars after it; otherwise the depth is
        /// right-censored (the window ended before the correction could develop).
        /// </summary>
        private const int CORRECTION_MIN_POST_END_BARS = 20;

        private const double CORRECTION_TARGET_236 = 0.236d;
        private const double CORRECTION_TARGET_382 = 0.382d;
        private const double CORRECTION_TARGET_50 = 0.5d;
        private const double CORRECTION_TARGET_618 = 0.618d;
        private const double CORRECTION_TARGET_786 = 0.786d;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_correction_outcome.md")]
        [Category("Research")]
        public void DiagonalCorrectionOutcome_Report()
        {
            string dataDir = FindDataDir();
            // h1 only. The m15 replay is four times the bars per file and the earlier
            // diagonal work put the edge in h1, not m15 (§9.21); the m15 check is a
            // separate follow-up. This keeps the research run in minutes, not hours.
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning archive file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh archive file was found under data/.");

            var runs = new List<CorrectionRun>();
            runs.AddRange(RunBothConfigs(dataDir, tuningFiles, "tuning"));
            runs.AddRange(RunBothConfigs(dataDir, freshFiles, "fresh"));

            Assert.That(runs.Sum(r => r.Samples.Count), Is.GreaterThan(0),
                "The correction replay produced no diagonal setups.");

            CorrectionRun tuningPermissive = Run(runs, "permissive", "tuning");
            CorrectionRun freshPermissive = Run(runs, "permissive", "fresh");

            List<SensitivityRow> horizonRows = BuildHorizonSensitivity(
                tuningPermissive.Samples.Select(x => x.Base).ToList());
            List<SensitivityRow> targetRows = BuildTargetSensitivity(
                tuningPermissive.Samples.Select(x => x.Base).ToList());

            List<Feature> features = FeatureList();
            List<CorrectionRuleRow> rules = BuildRules(tuningPermissive, freshPermissive, features);

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_correction_outcome.md");
            File.WriteAllText(
                reportPath,
                BuildCorrectionReport(
                    runs, tuningPermissive, freshPermissive, horizonRows, targetRows, rules,
                    features, tuningFiles.Count + freshFiles.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_correction_outcome_samples.csv");
            File.WriteAllText(csvPath, BuildCorrectionCsv(runs));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            // The providers are only needed for the forward walks; drop them before the
            // report lists are handed to the GC.
            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        #region Replay and labelling

        private static List<CorrectionRun> RunBothConfigs(
            string dataDir, IReadOnlyList<string> files, string set)
        {
            var permissive = new List<CorrectionSample>();
            var exact = new List<CorrectionSample>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    ITimeFrame timeFrame = file.Contains("_m15_")
                        ? TimeFrameHelper.Minute15
                        : TimeFrameHelper.Hour1;

                    var provider = new TestBarsProvider(timeFrame);
                    provider.LoadCandles(Path.Combine(dataDir, file));

                    List<Sample> permissiveSamples =
                        RunDiagonalSamples(provider, file, PERMISSIVE);
                    List<Sample> exactSamples =
                        RunDiagonalSamples(provider, file, EXACT_CORRIDOR);

                    var localPermissive = new List<CorrectionSample>(permissiveSamples.Count);
                    foreach (Sample sample in permissiveSamples)
                    {
                        localPermissive.Add(new CorrectionSample(
                            sample,
                            MeasureCorrection(sample, CORRECTION_HORIZON_MULTIPLIER),
                            "permissive",
                            set));
                    }

                    var localExact = new List<CorrectionSample>(exactSamples.Count);
                    foreach (Sample sample in exactSamples)
                    {
                        localExact.Add(new CorrectionSample(
                            sample,
                            MeasureCorrection(sample, CORRECTION_HORIZON_MULTIPLIER),
                            "exact",
                            set));
                    }

                    lock (sync)
                    {
                        permissive.AddRange(localPermissive);
                        exact.AddRange(localExact);
                    }

                    TestContext.Out.WriteLine(
                        $"{set}/{file}: permissive={localPermissive.Count}, exact={localExact.Count}, " +
                        $"elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
                });

            return new List<CorrectionRun>
            {
                new("permissive", set, permissive),
                new("exact", set, exact)
            };
        }

        /// <summary>
        /// Measures the correction after the actual diagonal end. The end is the last extreme
        /// in the diagonal's direction over the forward window; the depth is the maximum
        /// retrace from it. This deliberately does not stop at the theoretical invalidation:
        /// the pattern question ("did the diagonal correct?") is separate from the trade
        /// question ("did the stop survive?"), and both are reported.
        /// </summary>
        private static CorrectionOutcome MeasureCorrection(
            Sample sample, double horizonMultiplier)
        {
            TestBarsProvider provider = sample.Provider;
            int sgn = sample.IsUpDiagonal ? 1 : -1;
            int signalBar = sample.SignalBar;
            int skeletonBars = Math.Max(1, sample.P4Bar - sample.P0Bar);
            int horizon = Math.Clamp(
                (int)Math.Round(horizonMultiplier * skeletonBars, MidpointRounding.AwayFromZero),
                CORRECTION_MIN_HORIZON,
                CORRECTION_MAX_HORIZON);
            int end = Math.Min(provider.Count - 1, signalBar + horizon);
            int forwardBars = end - signalBar;
            bool hasForwardData = forwardBars >= CORRECTION_MIN_FORWARD_BARS;

            double endPrice = sample.V5;
            int endBar = Math.Clamp(sample.P5Bar, 0, Math.Max(0, end));
            double cap = sample.IsUpDiagonal
                ? sample.V4 + sample.W3
                : sample.V4 - sample.W3;

            int invalidationBar = -1;
            double mfeR = 0;
            double maeR = 0;
            double mfeAtInvalidation = 0;

            for (int i = signalBar + 1; i <= end; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                bool invalidated = sample.IsUpDiagonal ? high >= cap : low <= cap;
                if (invalidated && invalidationBar < 0)
                {
                    invalidationBar = i;
                    mfeAtInvalidation = mfeR;
                    maeR = Math.Max(maeR, 1d);
                }

                if (!invalidated)
                {
                    double favorable = sample.IsUpDiagonal
                        ? sample.Entry - low
                        : high - sample.Entry;
                    double adverse = sample.IsUpDiagonal
                        ? high - sample.Entry
                        : sample.Entry - low;
                    mfeR = Math.Max(mfeR, favorable / sample.Risk);
                    maeR = Math.Max(maeR, adverse / sample.Risk);
                }

                double barExtreme = sample.IsUpDiagonal ? high : low;
                if (sgn * (barExtreme - endPrice) > 0)
                {
                    endPrice = barExtreme;
                    endBar = i;
                }
            }

            double height = Math.Abs(endPrice - sample.V0);
            double maxDepth = 0;
            int maxDepthBar = -1;
            int first236 = -1;
            int first382 = -1;
            int first50 = -1;
            int first618 = -1;

            if (height > 1e-12)
            {
                for (int i = endBar + 1; i <= end; i++)
                {
                    double high = provider.GetHighPrice(i);
                    double low = provider.GetLowPrice(i);
                    double depth = sgn * (endPrice - (sample.IsUpDiagonal ? low : high));
                    if (depth <= 0)
                        continue;

                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                        maxDepthBar = i;
                    }

                    if (first236 < 0 && depth >= CORRECTION_TARGET_236 * height)
                        first236 = i;
                    if (first382 < 0 && depth >= CORRECTION_TARGET_382 * height)
                        first382 = i;
                    if (first50 < 0 && depth >= CORRECTION_TARGET_50 * height)
                        first50 = i;
                    if (first618 < 0 && depth >= CORRECTION_TARGET_618 * height)
                        first618 = i;
                }
            }

            int referenceBar = Math.Max(endBar, signalBar);
            int BarsFrom(int bar) => bar < 0 ? -1 : Math.Max(0, bar - referenceBar);

            double depthDiag = height > 1e-12 ? maxDepth / height : 0;
            double depthW3 = sample.W3 > 1e-12 ? maxDepth / sample.W3 : 0;
            double depthR = sample.Risk > 1e-12 ? maxDepth / sample.Risk : 0;
            int postEndBars = end - endBar;
            bool windowFull = signalBar + horizon <= provider.Count - 1;
            // A full window whose last extreme sits at its right edge is a genuine
            // "no correction within the horizon", not censoring: the diagonal simply
            // kept extending. Only a window cut short by the end of the archive is
            // censored, because then the correction had no room to develop.
            bool censored = !hasForwardData ||
                            (!windowFull && postEndBars < CORRECTION_MIN_POST_END_BARS);
            double capOvershootW3 = sample.W3 > 1e-12
                ? sgn * (endPrice - cap) / sample.W3
                : 0;
            double mfeBeforeInvalidation = invalidationBar < 0 ? mfeR : mfeAtInvalidation;

            return new CorrectionOutcome(
                HasForwardData: hasForwardData,
                Censored: censored,
                EndPrice: endPrice,
                EndBar: endBar,
                BarsToEnd: endBar - signalBar,
                Height: height,
                DepthPrice: maxDepth,
                DepthDiag: depthDiag,
                DepthW3: depthW3,
                DepthR: depthR,
                MfeR: mfeR,
                MaeR: maeR,
                MfeBeforeInvalidationR: mfeBeforeInvalidation,
                BarsToMax: BarsFrom(maxDepthBar),
                BarsTo236: BarsFrom(first236),
                BarsTo382: BarsFrom(first382),
                BarsTo50: BarsFrom(first50),
                BarsTo618: BarsFrom(first618),
                InvalidationBar: invalidationBar,
                InvalidatedBeforeEnd: invalidationBar >= 0 && invalidationBar <= endBar,
                CapOvershootW3: capOvershootW3,
                PostEndBars: postEndBars,
                Horizon: horizon,
                ForwardBars: forwardBars,
                Worked236: depthDiag >= CORRECTION_TARGET_236,
                Worked382: depthDiag >= CORRECTION_TARGET_382,
                Worked50: depthDiag >= CORRECTION_TARGET_50,
                Worked618: depthDiag >= CORRECTION_TARGET_618,
                Worked786: depthDiag >= CORRECTION_TARGET_786,
                NoCorrection: depthDiag < 0.1d,
                TradeableR1: mfeBeforeInvalidation >= 1d,
                ExtensionW3: sample.W3 > 1e-12
                    ? sgn * (endPrice - sample.V5) / sample.W3
                    : 0);
        }

        private static double PrecedingDepthRatio(Sample sample)
        {
            double height = Math.Abs(sample.V5 - sample.V0);
            if (height <= 1e-12)
                return double.NaN;

            double depth;
            if (sample.IsUpDiagonal)
            {
                double maxHigh = 0;
                for (int i = sample.P0Bar - 1; i >= 0; i--)
                {
                    if (sample.Provider.GetLowPrice(i) <= sample.V0)
                        break;
                    maxHigh = Math.Max(maxHigh, sample.Provider.GetHighPrice(i));
                }

                depth = maxHigh > 0 ? maxHigh - sample.V0 : 0;
            }
            else
            {
                double minLow = double.MaxValue;
                for (int i = sample.P0Bar - 1; i >= 0; i--)
                {
                    if (sample.Provider.GetHighPrice(i) >= sample.V0)
                        break;
                    minLow = Math.Min(minLow, sample.Provider.GetLowPrice(i));
                }

                depth = minLow < double.MaxValue ? sample.V0 - minLow : 0;
            }

            return depth / height;
        }

        private static double Rr236(Sample sample)
        {
            double height = Math.Abs(sample.V5 - sample.V0);
            double tp = sample.IsUpDiagonal
                ? sample.V5 - CORRECTION_TARGET_236 * height
                : sample.V5 + CORRECTION_TARGET_236 * height;
            int tradeDirection = sample.IsUpDiagonal ? 1 : -1;
            return sample.Risk > 1e-12
                ? tradeDirection * (sample.Entry - tp) / sample.Risk
                : double.NaN;
        }

        #endregion

        #region Sensitivity

        private static List<SensitivityRow> BuildHorizonSensitivity(
            IReadOnlyList<Sample> samples)
        {
            var rows = new List<SensitivityRow>();
            foreach ((string name, double multiplier) in new[]
                     {
                         ("1× skeleton", 1d),
                         ("2× skeleton", 2d),
                         ("3× skeleton (default)", 3d),
                         ("4× skeleton", 4d)
                     })
            {
                List<CorrectionSample> labelled = samples
                    .Select(s => new CorrectionSample(
                        s, MeasureCorrection(s, multiplier), "permissive", "tuning"))
                    .Where(x => x.Outcome.Labeled)
                    .ToList();

                rows.Add(SensitivityRow.Create(
                    name, labelled, x => x.Outcome.Tradeable236));
            }

            return rows;
        }

        private static List<SensitivityRow> BuildTargetSensitivity(
            IReadOnlyList<Sample> samples)
        {
            List<CorrectionSample> labelled = samples
                .Select(s => new CorrectionSample(
                    s, MeasureCorrection(s, CORRECTION_HORIZON_MULTIPLIER),
                    "permissive", "tuning"))
                .Where(x => x.Outcome.Labeled)
                .ToList();

            return new List<SensitivityRow>
            {
                SensitivityRow.Create("target 23.6%", labelled, x => x.Outcome.Tradeable236),
                SensitivityRow.Create("target 38.2%", labelled, x => x.Outcome.Tradeable382),
                SensitivityRow.Create("target 50.0%", labelled, x => x.Outcome.Tradeable50),
                SensitivityRow.Create("target 61.8%", labelled, x => x.Outcome.Tradeable618)
            };
        }

        #endregion

        #region Rules

        private static List<CorrectionRuleRow> BuildRules(
            CorrectionRun tuning,
            CorrectionRun fresh,
            IReadOnlyList<Feature> features)
        {
            List<CorrectionSample> tuningLabelled = tuning.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            List<CorrectionSample> freshLabelled = fresh.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();

            if (tuningLabelled.Count == 0 || freshLabelled.Count == 0)
                return new List<CorrectionRuleRow>();

            var scored = features
                .Select(f => new
                {
                    Feature = f,
                    TuningAuc = AucOf(tuningLabelled, f.Value, x => x.Outcome.Tradeable236),
                    FreshAuc = AucOf(freshLabelled, f.Value, x => x.Outcome.Tradeable236)
                })
                .Where(x => !double.IsNaN(x.TuningAuc) && !double.IsNaN(x.FreshAuc))
                .ToList();

            // A rule is only worth testing when the feature points the same way on both
            // samples; otherwise the search just picks a flipper and the fresh table
            // exposes it as a holdout failure.
            var consistent = scored
                .Where(x => (x.TuningAuc - 0.5d) * (x.FreshAuc - 0.5d) > 0)
                .ToList();
            List<Feature> candidates = (consistent.Count > 0 ? consistent : scored)
                .OrderByDescending(x => Math.Min(
                    Math.Abs(x.TuningAuc - 0.5), Math.Abs(x.FreshAuc - 0.5)))
                .Take(4)
                .Select(x => x.Feature)
                .ToList();

            var rows = new List<CorrectionRuleRow>();
            foreach (Feature feature in candidates)
            {
                List<CorrectionSample> tuningValid = tuningLabelled
                    .Where(x => IsFinite(feature.Value(x)))
                    .ToList();
                List<CorrectionSample> freshValid = freshLabelled
                    .Where(x => IsFinite(feature.Value(x)))
                    .ToList();
                List<double> tuningValues = tuningValid
                    .Select(feature.Value)
                    .ToList();
                if (tuningValues.Count < 30)
                    continue;

                foreach (double q in new[] { 0.25d, 0.5d, 0.75d })
                {
                    double threshold = Quantile(tuningValues, q);
                    foreach (bool greater in new[] { true, false })
                    {
                        Func<CorrectionSample, bool> rule = greater
                            ? x => feature.Value(x) >= threshold
                            : x => feature.Value(x) <= threshold;

                        CorrectionRuleRow row = CorrectionRuleRow.Create(
                            feature.Name,
                            $"{(greater ? ">=" : "<=")} {threshold.ToString("G4", CultureInfo.InvariantCulture)}",
                            tuningValid,
                            freshValid,
                            rule);
                        if (row.TuningN >= 100 && row.FreshN >= 100)
                            rows.Add(row);
                    }
                }
            }

            return rows
                .OrderByDescending(r => Math.Min(r.TuningLift, r.FreshLift))
                .ThenByDescending(r => r.FreshLift)
                .Take(10)
                .ToList();
        }

        #endregion

        #region Report

        private static string BuildCorrectionReport(
            IReadOnlyList<CorrectionRun> runs,
            CorrectionRun tuningPermissive,
            CorrectionRun freshPermissive,
            IReadOnlyList<SensitivityRow> horizonRows,
            IReadOnlyList<SensitivityRow> targetRows,
            IReadOnlyList<CorrectionRuleRow> rules,
            IReadOnlyList<Feature> features,
            int fileCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — post-diagonal correction depth and what separates it");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Archive: {fileCount} h1 files. The permissive sample (all beauty gates off) is the unbiased classifier sample; the exact corridor (`W2/W1 >= 0.5`, `W4/W3 <= 0.382`, 23.6% target) is the tradeable subset. Tuning = the Phase 0/1 h1 pairs (8 files); fresh = the §9.23 holdout (8 h1 pairs). m15 is omitted on purpose: the finder replay is four times the bars per file and the earlier diagonal work put the edge in h1, not m15 (§9.21)."));
            builder.AppendLine();
            builder.AppendLine("## Label");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"The diagonal end is the last extreme in the diagonal's direction over `{CORRECTION_HORIZON_MULTIPLIER:F0}×` the skeleton duration (P0→P4), clamped to {CORRECTION_MIN_HORIZON}–{CORRECTION_MAX_HORIZON} bars. The depth is the maximum retrace from that end, measured as a share of the actual diagonal height `|V(end) − V(0)|`, of `|W3|`, and of the signal risk. `worked` = depth >= 23.6% of the height; `tradeable` = worked and the theoretical stop `V(4) ± |W3|` was not hit before the end. Signals with fewer than {CORRECTION_MIN_FORWARD_BARS} forward bars or fewer than {CORRECTION_MIN_POST_END_BARS} bars after the end are censored. The label is ex-post (it needs the future); it is a research label, not a trading signal."));
            builder.AppendLine();

            AppendSampleTable(builder, runs);
            AppendDepthDistribution(builder, runs);
            AppendQuantiles(builder, runs);
            AppendTiming(builder, runs);
            AppendTradeability(builder, runs);
            AppendFeatureTable(
                builder,
                "Feature separation — primary target `tradeable 23.6%` (permissive)",
                tuningPermissive,
                freshPermissive,
                features,
                x => x.Outcome.Tradeable236);
            AppendFeatureTable(
                builder,
                "Feature separation — pattern target `worked 23.6%` (permissive)",
                tuningPermissive,
                freshPermissive,
                features,
                x => x.Outcome.Worked236);
            AppendFeatureTable(
                builder,
                "Feature separation — `tradeable 23.6%` inside the exact corridor",
                Run(runs, "exact", "tuning"),
                Run(runs, "exact", "fresh"),
                features,
                x => x.Outcome.Tradeable236);
            AppendBuckets(builder, tuningPermissive, freshPermissive, features);
            AppendRules(builder, rules);
            AppendSensitivity(builder, horizonRows, targetRows);
            AppendGroups(builder, tuningPermissive, freshPermissive);
            AppendConclusions(
                builder, tuningPermissive, freshPermissive, horizonRows, targetRows, rules,
                features);

            return builder.ToString();
        }

        private static void AppendSampleTable(
            StringBuilder builder, IReadOnlyList<CorrectionRun> runs)
        {
            builder.AppendLine("## Sample");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | TF | Signals | Labelled | Censored | worked236 | tradeable236 | worked50 | median depth |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                foreach (IGrouping<string, CorrectionSample> group in run.Samples
                             .GroupBy(x => x.Base.TimeFrame)
                             .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    List<CorrectionSample> all = group.ToList();
                    List<CorrectionSample> labelled = all
                        .Where(x => x.Outcome.Labeled)
                        .ToList();
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Set} | {group.Key} | {all.Count} | {labelled.Count} | {all.Count - labelled.Count} | {RateOf(labelled, x => x.Outcome.Worked236):P1} | {RateOf(labelled, x => x.Outcome.Tradeable236):P1} | {RateOf(labelled, x => x.Outcome.Worked50):P1} | {MedianOf(labelled.Select(x => x.Outcome.DepthDiag)):F3} |"));
                }

                List<CorrectionSample> runAll = run.Samples;
                List<CorrectionSample> runLabelled = runAll
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                builder.AppendLine(FormattableString.Invariant(
                    $"| **{run.Config}** | **{run.Set}** | **all** | **{runAll.Count}** | **{runLabelled.Count}** | **{runAll.Count - runLabelled.Count}** | **{RateOf(runLabelled, x => x.Outcome.Worked236):P1}** | **{RateOf(runLabelled, x => x.Outcome.Tradeable236):P1}** | **{RateOf(runLabelled, x => x.Outcome.Worked50):P1}** | **{MedianOf(runLabelled.Select(x => x.Outcome.DepthDiag)):F3}** |"));
            }

            builder.AppendLine();
        }

        private static void AppendDepthDistribution(
            StringBuilder builder, IReadOnlyList<CorrectionRun> runs)
        {
            builder.AppendLine("## Depth distribution");
            builder.AppendLine();
            builder.AppendLine("Share of labelled setups in each retrace bucket, as a fraction of the actual diagonal height.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | N | <10% | 10–23.6% | 23.6–38.2% | 38.2–50% | 50–61.8% | 61.8–78.6% | >=78.6% |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            (double Lower, double Upper, string Name)[] bins =
            {
                (0d, 0.1d, "<10%"),
                (0.1d, CORRECTION_TARGET_236, "10–23.6%"),
                (CORRECTION_TARGET_236, CORRECTION_TARGET_382, "23.6–38.2%"),
                (CORRECTION_TARGET_382, CORRECTION_TARGET_50, "38.2–50%"),
                (CORRECTION_TARGET_50, CORRECTION_TARGET_618, "50–61.8%"),
                (CORRECTION_TARGET_618, CORRECTION_TARGET_786, "61.8–78.6%"),
                (CORRECTION_TARGET_786, double.PositiveInfinity, ">=78.6%")
            };

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                if (labelled.Count == 0)
                    continue;

                var cells = new List<string>();
                foreach ((double lower, double upper, string _) in bins)
                {
                    int count = labelled.Count(x =>
                        x.Outcome.DepthDiag >= lower && x.Outcome.DepthDiag < upper);
                    cells.Add((count / (double)labelled.Count).ToString("P1", CultureInfo.InvariantCulture));
                }

                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Set} | {labelled.Count} | {string.Join(" | ", cells)} |"));
            }

            builder.AppendLine();
        }

        private static void AppendQuantiles(
            StringBuilder builder, IReadOnlyList<CorrectionRun> runs)
        {
            builder.AppendLine("## Depth quantiles");
            builder.AppendLine();
            builder.AppendLine("`depth/R` is the pattern depth from the end in units of the original signal risk; `MFE/R` is the actual favourable excursion from the signal entry before the stop.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Metric | p10 | p25 | p50 | p75 | p90 |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                if (labelled.Count == 0)
                    continue;

                foreach ((string name, Func<CorrectionSample, double> value) in new[]
                         {
                             ("depth / diagonal", (Func<CorrectionSample, double>)(x => x.Outcome.DepthDiag)),
                             ("depth / W3", x => x.Outcome.DepthW3),
                             ("depth / R", x => x.Outcome.DepthR),
                             ("MFE / R", x => x.Outcome.MfeR)
                         })
                {
                    List<double> values = labelled
                        .Select(value)
                        .Where(IsFinite)
                        .ToList();
                    if (values.Count == 0)
                        continue;

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Set} | {name} | {Quantile(values, 0.1):F2} | {Quantile(values, 0.25):F2} | {Quantile(values, 0.5):F2} | {Quantile(values, 0.75):F2} | {Quantile(values, 0.9):F2} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendTiming(
            StringBuilder builder, IReadOnlyList<CorrectionRun> runs)
        {
            builder.AppendLine("## Timing");
            builder.AppendLine();
            builder.AppendLine("Bars are counted from the signal bar (end, post-end window) or from the reference bar `max(end, signal)` (thresholds). The last column normalises the time to 23.6% by the skeleton duration.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | N | median bars to end | median post-end bars | median bars to 23.6% | median 23.6% / skeleton |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                if (labelled.Count == 0)
                    continue;

                List<double> barsToEnd = labelled
                    .Select(x => (double)Math.Max(0, x.Outcome.BarsToEnd))
                    .ToList();
                List<double> postEnd = labelled
                    .Select(x => (double)x.Outcome.PostEndBars)
                    .ToList();
                List<CorrectionSample> reached236 = labelled
                    .Where(x => x.Outcome.BarsTo236 >= 0)
                    .ToList();
                List<double> bars236 = reached236
                    .Select(x => (double)x.Outcome.BarsTo236)
                    .ToList();
                List<double> bars236Normalised = reached236
                    .Select(x => x.Outcome.BarsTo236 /
                                 (double)Math.Max(1, x.Base.P4Bar - x.Base.P0Bar))
                    .ToList();

                builder.AppendLine(FormattableString.Invariant(
                    $"| {run.Config} | {run.Set} | {labelled.Count} | {MedianOf(barsToEnd):F1} | {MedianOf(postEnd):F1} | {MedianOf(bars236):F1} | {MedianOf(bars236Normalised):F2} |"));
            }

            builder.AppendLine();
        }

        private static void AppendTradeability(
            StringBuilder builder, IReadOnlyList<CorrectionRun> runs)
        {
            builder.AppendLine("## Pattern outcome vs trade outcome");
            builder.AppendLine();
            builder.AppendLine("`invalidated` means the theoretical stop was hit at or before the actual diagonal end. `worked` without invalidation is the tradeable correction; a worked pattern that was already stopped is a structural correction the trade could not capture.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Group | N | invalidated | TP | SL | avg netR | median MFE/R | median depth/R |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                if (labelled.Count == 0)
                    continue;

                var groups = new (string Name, Func<CorrectionSample, bool> Predicate)[]
                {
                    ("all", _ => true),
                    ("worked236", x => x.Outcome.Worked236),
                    ("tradeable236", x => x.Outcome.Tradeable236),
                    ("not worked", x => !x.Outcome.Worked236),
                    ("deep 50%", x => x.Outcome.Worked50),
                    ("cap overshoot", x => x.Outcome.CapOvershootW3 > 0),
                    ("within cap", x => x.Outcome.CapOvershootW3 <= 0)
                };

                foreach ((string name, Func<CorrectionSample, bool> predicate) in groups)
                {
                    List<CorrectionSample> subset = labelled.Where(predicate).ToList();
                    if (subset.Count == 0)
                        continue;

                    List<CorrectionSample> resolved = subset
                        .Where(x => x.Base.Resolved)
                        .ToList();

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {run.Config} | {run.Set} | {name} | {subset.Count} | {RateOf(subset, x => x.Outcome.InvalidatedBeforeEnd):P1} | {RateOf(resolved, x => x.Base.Outcome == "TP"):P1} | {RateOf(resolved, x => x.Base.Outcome == "SL"):P1} | {(resolved.Count == 0 ? 0 : resolved.Average(x => x.Base.NetR)):F3} | {MedianOf(subset.Select(x => x.Outcome.MfeR)):F2} | {MedianOf(subset.Select(x => x.Outcome.DepthR)):F2} |"));
                }
            }

            builder.AppendLine();
            builder.AppendLine("### 2×2");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | worked236 | survived | N | TP | avg netR |");
            builder.AppendLine("|---|---|---|---|---:|---:|---:|");

            foreach (CorrectionRun run in runs)
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                foreach (bool worked in new[] { true, false })
                {
                    foreach (bool survived in new[] { true, false })
                    {
                        List<CorrectionSample> subset = labelled
                            .Where(x => x.Outcome.Worked236 == worked &&
                                        !x.Outcome.InvalidatedBeforeEnd == survived)
                            .ToList();
                        if (subset.Count == 0)
                            continue;

                        List<CorrectionSample> resolved = subset
                            .Where(x => x.Base.Resolved)
                            .ToList();
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {run.Config} | {run.Set} | {(worked ? "yes" : "no")} | {(survived ? "yes" : "no")} | {subset.Count} | {RateOf(resolved, x => x.Base.Outcome == "TP"):P1} | {(resolved.Count == 0 ? 0 : resolved.Average(x => x.Base.NetR)):F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendFeatureTable(
            StringBuilder builder,
            string title,
            CorrectionRun tuning,
            CorrectionRun fresh,
            IReadOnlyList<Feature> features,
            Func<CorrectionSample, bool> target)
        {
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            builder.AppendLine("AUC > 0.5 means the feature is higher in the target group. `stable` requires the same side of 0.5 (0.55/0.45) on both the tuning and the fresh set. A feature that only works on tuning is a holdout failure.");
            builder.AppendLine();
            builder.AppendLine("| Feature | Tuning N | Tuning AUC | Tuning train | Tuning test | Fresh N | Fresh AUC | Fresh train | Fresh test | Verdict |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");

            List<CorrectionSample> tuningLabelled = tuning.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            List<CorrectionSample> freshLabelled = fresh.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();

            var rows = new List<(Feature Feature, int TuningN, double TuningAuc,
                double TuningTrain, double TuningTest, int FreshN, double FreshAuc,
                double FreshTrain, double FreshTest)>();
            foreach (Feature feature in features)
            {
                int tuningN = tuningLabelled.Count(x => IsFinite(feature.Value(x)));
                int freshN = freshLabelled.Count(x => IsFinite(feature.Value(x)));
                double tuningAuc = AucOf(tuningLabelled, feature.Value, target);
                double freshAuc = AucOf(freshLabelled, feature.Value, target);
                if (double.IsNaN(tuningAuc) || double.IsNaN(freshAuc))
                    continue;

                rows.Add((
                    feature,
                    tuningN,
                    tuningAuc,
                    AucOf(tuningLabelled.Where(x => x.Base.EntryTime.Year <= 2022).ToList(),
                        feature.Value, target),
                    AucOf(tuningLabelled.Where(x => x.Base.EntryTime.Year >= 2023).ToList(),
                        feature.Value, target),
                    freshN,
                    freshAuc,
                    AucOf(freshLabelled.Where(x => x.Base.EntryTime.Year <= 2022).ToList(),
                        feature.Value, target),
                    AucOf(freshLabelled.Where(x => x.Base.EntryTime.Year >= 2023).ToList(),
                        feature.Value, target)));
            }

            foreach (var row in rows
                         .OrderByDescending(x => Math.Min(
                             Math.Abs(x.TuningAuc - 0.5), Math.Abs(x.FreshAuc - 0.5)))
                         .ThenByDescending(x => Math.Abs(x.TuningAuc - 0.5))
                         .Take(20))
            {
                bool stable = IsStable(row.TuningAuc, row.FreshAuc);
                string verdict = stable
                    ? "stable"
                    : IsStable(row.TuningAuc, row.TuningAuc)
                        ? "tuning only"
                        : IsStable(row.FreshAuc, row.FreshAuc) ? "fresh only" : "none";
                builder.AppendLine(FormattableString.Invariant(
                    $"| {row.Feature.Name} | {row.TuningN} | {row.TuningAuc:F3} | {row.TuningTrain:F3} | {row.TuningTest:F3} | {row.FreshN} | {row.FreshAuc:F3} | {row.FreshTrain:F3} | {row.FreshTest:F3} | {verdict} |"));
            }

            builder.AppendLine();
        }

        private static void AppendBuckets(
            StringBuilder builder,
            CorrectionRun tuning,
            CorrectionRun fresh,
            IReadOnlyList<Feature> features)
        {
            List<CorrectionSample> tuningLabelled = tuning.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            List<CorrectionSample> freshLabelled = fresh.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            if (tuningLabelled.Count == 0 || freshLabelled.Count == 0)
                return;

            List<Feature> top = features
                .Select(f => new
                {
                    Feature = f,
                    Auc = Math.Min(
                        Math.Abs(AucOf(tuningLabelled, f.Value, x => x.Outcome.Tradeable236) - 0.5),
                        Math.Abs(AucOf(freshLabelled, f.Value, x => x.Outcome.Tradeable236) - 0.5))
                })
                .OrderByDescending(x => x.Auc)
                .Take(3)
                .Select(x => x.Feature)
                .ToList();

            builder.AppendLine("## Buckets of the best separators");
            builder.AppendLine();
            builder.AppendLine("Quantile edges are built on tuning; the same edges are applied to fresh. A monotone tuning curve that survives on fresh is the useful case.");
            builder.AppendLine();
            builder.AppendLine("| Feature | Bucket | Tuning N | Tuning tradeable236 | Fresh N | Fresh tradeable236 | Tuning median depth |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");

            foreach (Feature feature in top)
            {
                List<double> tuningValues = tuningLabelled
                    .Select(feature.Value)
                    .Where(IsFinite)
                    .OrderBy(v => v)
                    .ToList();
                if (tuningValues.Count < 30)
                    continue;

                double[] edges = Enumerable.Range(1, 4)
                    .Select(k => Quantile(tuningValues, k / 5d))
                    .ToArray();

                for (int bucket = 0; bucket < 5; bucket++)
                {
                    double lower = bucket == 0 ? double.NegativeInfinity : edges[bucket - 1];
                    double upper = bucket == 4 ? double.PositiveInfinity : edges[bucket];
                    List<CorrectionSample> tuningBucket = tuningLabelled
                        .Where(x => InBucket(feature.Value(x), lower, upper))
                        .ToList();
                    List<CorrectionSample> freshBucket = freshLabelled
                        .Where(x => InBucket(feature.Value(x), lower, upper))
                        .ToList();
                    if (tuningBucket.Count == 0 && freshBucket.Count == 0)
                        continue;

                    string bucketName = bucket == 0
                        ? $"< {edges[0]:G4}"
                        : bucket == 4
                            ? $">= {edges[3]:G4}"
                            : $"{edges[bucket - 1]:G4}–{edges[bucket]:G4}";

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {feature.Name} | {bucketName} | {tuningBucket.Count} | {RateOf(tuningBucket, x => x.Outcome.Tradeable236):P1} | {freshBucket.Count} | {RateOf(freshBucket, x => x.Outcome.Tradeable236):P1} | {MedianOf(tuningBucket.Select(x => x.Outcome.DepthDiag)):F3} |"));
                }
            }

            builder.AppendLine();
        }

        private static void AppendRules(
            StringBuilder builder, IReadOnlyList<CorrectionRuleRow> rules)
        {
            builder.AppendLine("## Threshold rules");
            builder.AppendLine();
            builder.AppendLine("Thresholds are chosen on tuning (quantiles 25/50/75), evaluated on fresh; only rules with at least 100 signals on both sides are shown. `pairs improved` compares the rule subset with the file's own base rate on fresh (at least 10 signals on both sides).");
            builder.AppendLine();
            builder.AppendLine("| Feature | Rule | Tuning N | Tuning tradeable236 | Tuning lift | Fresh N | Fresh tradeable236 | Fresh lift | Pairs improved |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (CorrectionRuleRow row in rules)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {row.Feature} | `{row.Rule}` | {row.TuningN} | {row.TuningRate:P1} | {row.TuningLift:+0.0%;-0.0%} | {row.FreshN} | {row.FreshRate:P1} | {row.FreshLift:+0.0%;-0.0%} | {row.PairsImproved}/{row.PairsEligible} |"));
            }

            builder.AppendLine();
        }

        private static void AppendSensitivity(
            StringBuilder builder,
            IReadOnlyList<SensitivityRow> horizonRows,
            IReadOnlyList<SensitivityRow> targetRows)
        {
            builder.AppendLine("## Sensitivity of the label");
            builder.AppendLine();
            builder.AppendLine("The label must not be an artefact of one horizon or one target. AUCs are for the row's target on the tuning permissive sample.");
            builder.AppendLine();
            builder.AppendLine("| Variant | N | worked236 | tradeable | median depth | AUC W2/W1 | AUC W3/W1 | AUC W5/W3 | AUC c |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (SensitivityRow row in horizonRows.Concat(targetRows))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {row.Variant} | {row.N} | {row.Worked236:P1} | {row.Tradeable:P1} | {row.MedianDepth:F3} | {row.AucW2:F3} | {row.AucW3:F3} | {row.AucW5:F3} | {row.AucC:F3} |"));
            }

            builder.AppendLine();
        }

        private static void AppendGroups(
            StringBuilder builder,
            CorrectionRun tuning,
            CorrectionRun fresh)
        {
            builder.AppendLine("## Groups");
            builder.AppendLine();
            builder.AppendLine("Permissive sample only. `worked236` is the structural label; `tradeable236` additionally requires that the stop was not hit before the end.");
            builder.AppendLine();
            builder.AppendLine("| Set | Group | Value | N | worked236 | tradeable236 | median depth |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|");

            var dimensions = new (string Name, Func<CorrectionSample, string> Key)[]
            {
                ("timeframe", x => x.Base.TimeFrame),
                ("session", x => SessionOf(x.Base.EntryTime)),
                ("year", x => x.Base.EntryTime.Year.ToString(CultureInfo.InvariantCulture)),
                ("pair", x => x.Base.Symbol)
            };

            foreach (CorrectionRun run in new[] { tuning, fresh })
            {
                List<CorrectionSample> labelled = run.Samples
                    .Where(x => x.Outcome.Labeled)
                    .ToList();
                foreach ((string name, Func<CorrectionSample, string> key) in dimensions)
                {
                    foreach (IGrouping<string, CorrectionSample> group in labelled
                                 .GroupBy(key)
                                 .OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        List<CorrectionSample> subset = group.ToList();
                        builder.AppendLine(FormattableString.Invariant(
                            $"| {run.Set} | {name} | {group.Key} | {subset.Count} | {RateOf(subset, x => x.Outcome.Worked236):P1} | {RateOf(subset, x => x.Outcome.Tradeable236):P1} | {MedianOf(subset.Select(x => x.Outcome.DepthDiag)):F3} |"));
                    }
                }
            }

            builder.AppendLine();
        }

        private static void AppendConclusions(
            StringBuilder builder,
            CorrectionRun tuning,
            CorrectionRun fresh,
            IReadOnlyList<SensitivityRow> horizonRows,
            IReadOnlyList<SensitivityRow> targetRows,
            IReadOnlyList<CorrectionRuleRow> rules,
            IReadOnlyList<Feature> features)
        {
            builder.AppendLine("## What it says");
            builder.AppendLine();

            List<CorrectionSample> tuningLabelled = tuning.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            List<CorrectionSample> freshLabelled = fresh.Samples
                .Where(x => x.Outcome.Labeled)
                .ToList();
            if (tuningLabelled.Count == 0 || freshLabelled.Count == 0)
            {
                builder.AppendLine("- No labelled sample; nothing to conclude.");
                return;
            }

            builder.AppendLine(FormattableString.Invariant(
                $"- **Base rates.** Tuning: {tuningLabelled.Count} labelled setups, `worked236` {RateOf(tuningLabelled, x => x.Outcome.Worked236):P1}, `tradeable236` {RateOf(tuningLabelled, x => x.Outcome.Tradeable236):P1}, median depth {MedianOf(tuningLabelled.Select(x => x.Outcome.DepthDiag)):F3}, no noticeable correction (<10%) {RateOf(tuningLabelled, x => x.Outcome.NoCorrection):P1}. Fresh: {freshLabelled.Count}, {RateOf(freshLabelled, x => x.Outcome.Worked236):P1}, {RateOf(freshLabelled, x => x.Outcome.Tradeable236):P1}, {MedianOf(freshLabelled.Select(x => x.Outcome.DepthDiag)):F3}, {RateOf(freshLabelled, x => x.Outcome.NoCorrection):P1}."));

            double stoppedWorked = RateOf(
                tuningLabelled.Where(x => x.Outcome.Worked236).ToList(),
                x => x.Outcome.InvalidatedBeforeEnd);
            builder.AppendLine(FormattableString.Invariant(
                $"- **Pattern is not trade.** {stoppedWorked:P1} of the tuning setups that eventually produced a 23.6% correction had already hit the theoretical stop before the diagonal end. The correction was real, but the trade could not hold it; this is exactly the gap the old `worked236` label hides."));

            builder.AppendLine(FormattableString.Invariant(
                $"- **Depth is deep and universal.** Median depth {MedianOf(tuningLabelled.Select(x => x.Outcome.DepthDiag)):F3} on tuning and {MedianOf(freshLabelled.Select(x => x.Outcome.DepthDiag)):F3} on fresh; {RateOf(tuningLabelled, x => x.Outcome.Worked236):P1}/{RateOf(freshLabelled, x => x.Outcome.Worked236):P1} reach 23.6%, {RateOf(tuningLabelled, x => x.Outcome.Worked50):P1}/{RateOf(freshLabelled, x => x.Outcome.Worked50):P1} reach 50%, {RateOf(tuningLabelled, x => x.Outcome.Worked786):P1}/{RateOf(freshLabelled, x => x.Outcome.Worked786):P1} reach 78.6%. The post-diagonal correction is usually a half-to-full retrace, not a rare 23.6% event; 23.6% is a conservative floor, not a typical outcome."));

            var patternStable = features
                .Select(f => new
                {
                    Feature = f,
                    Tuning = AucOf(tuningLabelled, f.Value, x => x.Outcome.Worked236),
                    Fresh = AucOf(freshLabelled, f.Value, x => x.Outcome.Worked236)
                })
                .Where(x => IsStable(x.Tuning, x.Fresh))
                .ToList();
            builder.AppendLine(patternStable.Count == 0
                ? "- **The pattern label has no stable separator.** For `worked236` (does the correction happen at all) no feature passes the 0.55/0.45 stability bar on both tuning and fresh; the strongest stay within 0.45–0.55. The Phase 0 `W2/W1` (0.641) and `W3/W1` (0.639) separators do not replicate once the depth is measured from the actual end over a longer window. Geometry tells whether the original trade survives to the end, not whether the correction is deep."
                : FormattableString.Invariant(
                    $"- **Pattern-label separators.** {string.Join(", ", patternStable.Take(5).Select(x => $"`{x.Feature.Name}` (tuning {x.Tuning:F3}, fresh {x.Fresh:F3})"))}. These predict whether the correction happens at all, separately from trade survival."));

            builder.AppendLine("- **Exact corridor raises survival, not depth.** The exact corridor's `tradeable236` is higher than the permissive one (see the sample table), while `worked236` and median depth are similar. The stable structural separators are `W4/W2` and `W4/W3` (lower is better), not `W2/W1`; the W4 upper bound is the part of the exact corridor that the correction label supports.");
            builder.AppendLine("- **The mechanical caveat.** `Risk/W3` and the signal R:R are tied to the stop by construction: a wider stop is harder to hit. The best rule (`Risk/W3 >= 0.646`) is therefore an execution rule (do not enter when wave 5 is already close to the cap), not evidence that the wedge shape is better.");

            var stableFeatures = features
                .Select(f => new
                {
                    Feature = f,
                    Tuning = AucOf(tuningLabelled, f.Value, x => x.Outcome.Tradeable236),
                    Fresh = AucOf(freshLabelled, f.Value, x => x.Outcome.Tradeable236)
                })
                .Where(x => IsStable(x.Tuning, x.Fresh))
                .OrderByDescending(x => Math.Min(
                    Math.Abs(x.Tuning - 0.5), Math.Abs(x.Fresh - 0.5)))
                .ToList();

            if (stableFeatures.Count == 0)
            {
                builder.AppendLine("- **No stable separator.** No feature keeps the same side of 0.5 with at least a 0.55/0.45 AUC on both tuning and fresh. On this archive the geometry does not tell a tradeable post-diagonal correction from a non-correction; do not add another geometric gate on the strength of the tuning table.");
            }
            else
            {
                string list = string.Join(", ", stableFeatures.Take(5).Select(x =>
                    FormattableString.Invariant(
                        $"`{x.Feature.Name}` (tuning {x.Tuning:F3}, fresh {x.Fresh:F3})")));
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Stable separators.** {list}. The direction is the side of 0.5: above 0.5 means the feature is higher in the tradeable group. These are candidates for a holdout-tested gate, not yet a default."));
            }

            if (rules.Count > 0)
            {
                CorrectionRuleRow best = rules[0];
                builder.AppendLine(FormattableString.Invariant(
                    $"- **Best simple rule.** `{best.Feature} {best.Rule}`: tuning {best.TuningRate:P1} vs base {best.TuningRate - best.TuningLift:P1} ({best.TuningN} signals), fresh {best.FreshRate:P1} vs base {best.FreshRate - best.FreshLift:P1} ({best.FreshN} signals), pairs improved {best.PairsImproved}/{best.PairsEligible}. The fresh lift is the number that matters; a tuning-only lift is noise."));
            }

            string horizonRange = string.Join(", ", horizonRows.Select(r =>
                FormattableString.Invariant($"{r.Variant} {r.Tradeable:P1}")));
            string targetRange = string.Join(", ", targetRows.Select(r =>
                FormattableString.Invariant($"{r.Variant} {r.Tradeable:P1}")));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Label sensitivity.** Tradeable rate by horizon: {horizonRange}. By target: {targetRange}. A conclusion that only appears at one horizon/target is not a conclusion."));

            builder.AppendLine("- **Caveats.** The label uses future bars and is not a signal. The archive is bid-only and has no spread; the trade outcome is the finder's own TP/SL simulation. The fresh set is a pair holdout, not a new time period. Multiple features were tested, so only the tuning+fresh agreement is evidence.");
            builder.AppendLine("- **Next step.** If a stable separator survives a walk-forward and a true fresh-period check, wire it as a research gate in the exact finder and re-run the TP/SL grid. If not, stop adding geometric filters: the next candidates are context (higher timeframe, volatility regime) and execution, not more wedge shape.");
            builder.AppendLine();
        }

        #endregion

        #region CSV

        private static string BuildCorrectionCsv(IReadOnlyList<CorrectionRun> runs)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,timeframe,entry_time,signal_bar,is_up,entry,risk,sl,tp," +
                "outcome,net_r,resolved,profitable,labelled,censored,has_forward," +
                "end_price,end_bar,bars_to_end,height,depth_price,depth_diag,depth_w3,depth_r," +
                "mfe_r,mae_r,mfe_before_sl_r,bars_to_max,bars_to_236,bars_to_382,bars_to_50," +
                "bars_to_618,invalidation_bar,invalidated_before_end,cap_overshoot_w3," +
                "post_end_bars,horizon,forward_bars,worked236,worked382,worked50,worked618," +
                "worked786,tradeable236,tradeable_r1,no_correction,extension_w3," +
                "preceding_depth_ratio,rr236," +
                "w2_retr,w3_w1,w4_w3,w4_w2,w5_w3,overlap,w3_pen,c,spill234,spill_max," +
                "spill5_early,max_outside_run,bars_w4_w2,bars_w3_w1,bars_w5_w3,bars_w2_w1," +
                "skeleton_bars,w3_range,height_range,risk_w3,entry_vs_v3_w3,entry_vs_line_r," +
                "activity_w3_w1,activity_w5_w3");

            foreach (CorrectionSample x in runs
                         .SelectMany(r => r.Samples)
                         .OrderBy(x => x.Config, StringComparer.Ordinal)
                         .ThenBy(x => x.Set, StringComparer.Ordinal)
                         .ThenBy(x => x.Base.File, StringComparer.Ordinal)
                         .ThenBy(x => x.Base.EntryTime))
            {
                Sample s = x.Base;
                CorrectionOutcome o = x.Outcome;
                double skeletonBars = Math.Max(1, s.P4Bar - s.P0Bar);
                double heightAtSignal = Math.Abs(s.V5 - s.V0);
                int sgn = s.IsUpDiagonal ? 1 : -1;

                builder.AppendLine(string.Join(",",
                    Csv(x.Config),
                    Csv(x.Set),
                    Csv(s.File),
                    Csv(s.Symbol),
                    Csv(s.TimeFrame),
                    Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                    s.SignalBar.ToString(CultureInfo.InvariantCulture),
                    B(s.IsUpDiagonal),
                    F(s.Entry), F(s.Risk), F(s.Sl), F(s.Tp),
                    Csv(s.Outcome), F(s.NetR), B(s.Resolved), B(s.Profitable),
                    B(o.Labeled), B(o.Censored), B(o.HasForwardData),
                    F(o.EndPrice), o.EndBar.ToString(CultureInfo.InvariantCulture),
                    o.BarsToEnd.ToString(CultureInfo.InvariantCulture),
                    F(o.Height), F(o.DepthPrice), F(o.DepthDiag), F(o.DepthW3), F(o.DepthR),
                    F(o.MfeR), F(o.MaeR), F(o.MfeBeforeInvalidationR),
                    o.BarsToMax.ToString(CultureInfo.InvariantCulture),
                    o.BarsTo236.ToString(CultureInfo.InvariantCulture),
                    o.BarsTo382.ToString(CultureInfo.InvariantCulture),
                    o.BarsTo50.ToString(CultureInfo.InvariantCulture),
                    o.BarsTo618.ToString(CultureInfo.InvariantCulture),
                    o.InvalidationBar.ToString(CultureInfo.InvariantCulture),
                    B(o.InvalidatedBeforeEnd), F(o.CapOvershootW3),
                    o.PostEndBars.ToString(CultureInfo.InvariantCulture),
                    o.Horizon.ToString(CultureInfo.InvariantCulture),
                    o.ForwardBars.ToString(CultureInfo.InvariantCulture),
                    B(o.Worked236), B(o.Worked382), B(o.Worked50), B(o.Worked618),
                    B(o.Worked786), B(o.Tradeable236), B(o.TradeableR1), B(o.NoCorrection),
                    F(o.ExtensionW3), F(PrecedingDepthRatio(s)), F(Rr236(s)),
                    F(s.W2Retr), F(s.W3W1), F(s.W4W3), F(s.W4W2), F(s.W5W3),
                    F(s.OverlapDepth), F(s.W3Pen), F(s.C), F(s.A234), F(s.AMax),
                    F(s.A5Early), s.MaxOutsideRun.ToString(CultureInfo.InvariantCulture),
                    F((double)s.BarsW4 / Math.Max(1, s.BarsW2)),
                    F((double)s.BarsW3 / Math.Max(1, s.BarsW1)),
                    F((double)s.BarsW5 / Math.Max(1, s.BarsW3)),
                    F((double)s.BarsW2 / Math.Max(1, s.BarsW1)),
                    F(skeletonBars),
                    F(s.W3 / Math.Max(1e-12, s.MedianRangeW3)),
                    F(heightAtSignal / Math.Max(1e-12, s.MedianRangeW3)),
                    F(s.Risk / Math.Max(1e-12, s.W3)),
                    F(sgn * (s.Entry - s.V3) / Math.Max(1e-12, s.W3)),
                    F(s.EntryVsLineR),
                    F(s.ActivityW3W1), F(s.ActivityW5W3)));
            }

            return builder.ToString();
        }

        #endregion

        #region Helpers and types

        private static CorrectionRun Run(
            IReadOnlyList<CorrectionRun> runs, string config, string set)
        {
            return runs.FirstOrDefault(r => r.Config == config && r.Set == set) ??
                   new CorrectionRun(config, set, new List<CorrectionSample>());
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsStable(double a, double b) =>
            IsFinite(a) && IsFinite(b) &&
            ((a >= 0.55d && b >= 0.55d) || (a <= 0.45d && b <= 0.45d));

        private static double RateOf(
            IReadOnlyList<CorrectionSample> samples, Func<CorrectionSample, bool> predicate)
        {
            return samples.Count == 0 ? 0 : samples.Count(predicate) / (double)samples.Count;
        }

        private static double MedianOf(IEnumerable<double> values)
        {
            List<double> finite = values.Where(IsFinite).ToList();
            return finite.Count == 0 ? 0 : Quantile(finite, 0.5);
        }

        private static bool InBucket(double value, double lower, double upper)
        {
            return IsFinite(value) && value >= lower && value < upper;
        }

        private static double AucOf(
            IReadOnlyList<CorrectionSample> samples,
            Func<CorrectionSample, double> metric,
            Func<CorrectionSample, bool> positive)
        {
            var values = samples
                .Select(s => (Value: metric(s), Positive: positive(s)))
                .Where(x => IsFinite(x.Value))
                .OrderBy(x => x.Value)
                .ToList();

            int posCount = values.Count(x => x.Positive);
            int negCount = values.Count - posCount;
            if (posCount == 0 || negCount == 0)
                return double.NaN;

            double rankSumPositive = 0;
            int i = 0;
            while (i < values.Count)
            {
                int j = i;
                while (j + 1 < values.Count &&
                       Math.Abs(values[j + 1].Value - values[i].Value) < 1e-12)
                {
                    j++;
                }

                double averageRank = (i + 1 + j + 1) / 2d;
                for (int k = i; k <= j; k++)
                {
                    if (values[k].Positive)
                        rankSumPositive += averageRank;
                }

                i = j + 1;
            }

            return (rankSumPositive - posCount * (posCount + 1) / 2d) / (posCount * negCount);
        }

        private static List<Feature> FeatureList()
        {
            return new List<Feature>
            {
                new("W2/W1", x => x.Base.W2Retr),
                new("W3/W1", x => x.Base.W3W1),
                new("W4/W3", x => x.Base.W4W3),
                new("W4/W2", x => x.Base.W4W2),
                new("W5/W3 at signal", x => x.Base.W5W3),
                new("Overlap depth / W1", x => x.Base.OverlapDepth),
                new("W3 penetration / W1", x => x.Base.W3Pen),
                new("Convergence c", x => x.Base.C),
                new("Spill A(2-4)", x => x.Base.A234),
                new("Spill A max", x => x.Base.AMax),
                new("Spill A5 early", x => x.Base.A5Early),
                new("Max outside run", x => x.Base.MaxOutsideRun),
                new("Bars W2/W1", x => (double)x.Base.BarsW2 / Math.Max(1, x.Base.BarsW1)),
                new("Bars W3/W1", x => (double)x.Base.BarsW3 / Math.Max(1, x.Base.BarsW1)),
                new("Bars W4/W2", x => (double)x.Base.BarsW4 / Math.Max(1, x.Base.BarsW2)),
                new("Bars W5/W3", x => (double)x.Base.BarsW5 / Math.Max(1, x.Base.BarsW3)),
                new("Bars W5 / skeleton", x => (double)x.Base.BarsW5 /
                                               Math.Max(1, x.Base.P4Bar - x.Base.P0Bar)),
                new("Skeleton bars", x => x.Base.P4Bar - x.Base.P0Bar),
                new("W3 / median range W3", x => x.Base.W3 /
                                                 Math.Max(1e-12, x.Base.MedianRangeW3)),
                new("Height / median range W3", x => Math.Abs(x.Base.V5 - x.Base.V0) /
                                                     Math.Max(1e-12, x.Base.MedianRangeW3)),
                new("Risk / W3", x => x.Base.Risk / Math.Max(1e-12, x.Base.W3)),
                new("Entry vs V3 / W3", x => (x.Base.IsUpDiagonal ? 1 : -1) *
                                                (x.Base.Entry - x.Base.V3) /
                                                Math.Max(1e-12, x.Base.W3)),
                new("Entry vs line 1-3 / R", x => x.Base.EntryVsLineR),
                new("R:R 23.6% at signal", x => Rr236(x.Base)),
                new("Preceding correction / height", x => PrecedingDepthRatio(x.Base)),
                new("Activity W3/W1", x => x.Base.ActivityW3W1),
                new("Activity W5/W3", x => x.Base.ActivityW5W3),
                new("Year", x => x.Base.EntryTime.Year)
            };
        }

        private sealed record CorrectionRun(
            string Config,
            string Set,
            List<CorrectionSample> Samples);

        private sealed record CorrectionSample(
            Sample Base,
            CorrectionOutcome Outcome,
            string Config,
            string Set);

        private sealed record CorrectionOutcome(
            bool HasForwardData,
            bool Censored,
            double EndPrice,
            int EndBar,
            int BarsToEnd,
            double Height,
            double DepthPrice,
            double DepthDiag,
            double DepthW3,
            double DepthR,
            double MfeR,
            double MaeR,
            double MfeBeforeInvalidationR,
            int BarsToMax,
            int BarsTo236,
            int BarsTo382,
            int BarsTo50,
            int BarsTo618,
            int InvalidationBar,
            bool InvalidatedBeforeEnd,
            double CapOvershootW3,
            int PostEndBars,
            int Horizon,
            int ForwardBars,
            bool Worked236,
            bool Worked382,
            bool Worked50,
            bool Worked618,
            bool Worked786,
            bool NoCorrection,
            bool TradeableR1,
            double ExtensionW3)
        {
            public bool Labeled => HasForwardData && !Censored;

            public bool Tradeable236 => Worked236 && !InvalidatedBeforeEnd;
            public bool Tradeable382 => Worked382 && !InvalidatedBeforeEnd;
            public bool Tradeable50 => Worked50 && !InvalidatedBeforeEnd;
            public bool Tradeable618 => Worked618 && !InvalidatedBeforeEnd;
        }

        private sealed record Feature(string Name, Func<CorrectionSample, double> Value);

        private sealed record SensitivityRow(
            string Variant,
            int N,
            double Worked236,
            double Tradeable,
            double MedianDepth,
            double AucW2,
            double AucW3,
            double AucW5,
            double AucC)
        {
            public static SensitivityRow Create(
                string variant,
                IReadOnlyList<CorrectionSample> samples,
                Func<CorrectionSample, bool> target)
            {
                return new SensitivityRow(
                    variant,
                    samples.Count,
                    RateOf(samples, x => x.Outcome.Worked236),
                    RateOf(samples, target),
                    MedianOf(samples.Select(x => x.Outcome.DepthDiag)),
                    AucOf(samples, x => x.Base.W2Retr, target),
                    AucOf(samples, x => x.Base.W3W1, target),
                    AucOf(samples, x => x.Base.W5W3, target),
                    AucOf(samples, x => x.Base.C, target));
            }
        }

        private sealed record CorrectionRuleRow(
            string Feature,
            string Rule,
            int TuningN,
            double TuningRate,
            double TuningLift,
            int FreshN,
            double FreshRate,
            double FreshLift,
            int PairsImproved,
            int PairsEligible)
        {
            public static CorrectionRuleRow Create(
                string feature,
                string rule,
                IReadOnlyList<CorrectionSample> tuning,
                IReadOnlyList<CorrectionSample> fresh,
                Func<CorrectionSample, bool> predicate)
            {
                List<CorrectionSample> tuningSubset = tuning.Where(predicate).ToList();
                List<CorrectionSample> freshSubset = fresh.Where(predicate).ToList();

                int improved = 0;
                int eligible = 0;
                foreach (IGrouping<string, CorrectionSample> group in fresh.GroupBy(x => x.Base.File))
                {
                    List<CorrectionSample> fileAll = group.ToList();
                    List<CorrectionSample> fileSubset = fileAll.Where(predicate).ToList();
                    if (fileAll.Count < 10 || fileSubset.Count < 10)
                        continue;

                    eligible++;
                    if (RateOf(fileSubset, x => x.Outcome.Tradeable236) >
                        RateOf(fileAll, x => x.Outcome.Tradeable236) + 0.02d)
                    {
                        improved++;
                    }
                }

                return new CorrectionRuleRow(
                    feature,
                    rule,
                    tuningSubset.Count,
                    RateOf(tuningSubset, x => x.Outcome.Tradeable236),
                    RateOf(tuningSubset, x => x.Outcome.Tradeable236) -
                    RateOf(tuning, x => x.Outcome.Tradeable236),
                    freshSubset.Count,
                    RateOf(freshSubset, x => x.Outcome.Tradeable236),
                    RateOf(freshSubset, x => x.Outcome.Tradeable236) -
                    RateOf(fresh, x => x.Outcome.Tradeable236),
                    improved,
                    eligible);
            }
        }

        #endregion
    }
}
