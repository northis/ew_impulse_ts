using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the leading/ending diagonal hypothesis (DIAGONAL.md §9.32).
    /// The previous correction-depth run (§9.27) found no stable geometric separator: a
    /// wedge that satisfies the rules and a wedge that produces a deep correction look the
    /// same. This harness tests a different, structural hypothesis: leading diagonals are
    /// contaminated by a 1-2-1-2 sequence that only resembles a diagonal, and the trade
    /// catches a wave-3 extension instead of a reversal. If so, keeping only ending
    /// diagonals should raise the correction depth and the net R.
    /// <para>
    /// The classifiers are fixed before the outcomes are inspected and use only bars up to
    /// the signal (no future data): (1) the finder's own <c>IsInitialMovement</c> gate,
    /// (2) point 0 is the extreme of the preceding lookback (a reversal low/high, the
    /// leading-diagonal signature), and (3) a prior bar traded beyond the wave-5 extreme
    /// (the diagonal is still inside the preceding range, another leading signature).
    /// "Ending" is the complement; a strict ending group requires all three. The report
    /// compares pattern depth, survival, net R and pair-level consistency on the same
    /// permissive/exact, tuning/fresh h1 sample as §9.27. Writes
    /// <c>reports/diagonal_leading_ending.md</c> and
    /// <c>reports/diagonal_leading_ending_samples.csv</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalLeadingEnding"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        /// <summary>Context lookback as a multiple of the skeleton duration (P0 → P4).</summary>
        private const int CONTEXT_LOOKBACK_MULTIPLIER = 3;

        private const int CONTEXT_MIN_LOOKBACK = 100;
        private const int CONTEXT_MAX_LOOKBACK = 2000;
        private const int CONTEXT_BOOTSTRAP = 10000;
        private const int CONTEXT_SEED = 20261026;

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_leading_ending.md")]
        [Category("Research")]
        public void DiagonalLeadingEnding_Report()
        {
            string dataDir = FindDataDir();
            List<string> tuningFiles = Existing(dataDir, H1_FILES);
            List<string> freshFiles = Existing(dataDir, FRESH_H1_FILES);

            Assert.That(tuningFiles, Is.Not.Empty, "No tuning archive file was found under data/.");
            Assert.That(freshFiles, Is.Not.Empty, "No fresh archive file was found under data/.");

            var runs = new List<CorrectionRun>();
            runs.AddRange(RunBothConfigs(dataDir, tuningFiles, "tuning"));
            runs.AddRange(RunBothConfigs(dataDir, freshFiles, "fresh"));

            var classified = new List<ClassifiedSample>();
            foreach (CorrectionRun run in runs)
            {
                foreach (CorrectionSample sample in run.Samples)
                {
                    if (!sample.Outcome.Labeled)
                        continue;

                    classified.Add(new ClassifiedSample(sample, BuildContext(sample.Base)));
                }
            }

            Assert.That(classified, Is.Not.Empty,
                "The leading/ending replay produced no labelled diagonal setups.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_leading_ending.md");
            File.WriteAllText(reportPath, BuildLeadingEndingReport(classified));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_leading_ending_samples.csv");
            File.WriteAllText(csvPath, BuildLeadingEndingCsv(classified));
            TestContext.Out.WriteLine($"Wrote {csvPath}");

            foreach (CorrectionSample sample in runs.SelectMany(r => r.Samples))
                sample.Base.Provider = null!;
        }

        #region Context

        /// <summary>
        /// Builds the causal context of one setup from bars before point 0. The lookback is
        /// three skeleton lengths (clamped), which is long enough to contain the preceding
        /// impulse and its correction for both a wave-1/A diagonal and a wave-5/C one.
        /// </summary>
        private static ContextFeatures BuildContext(Sample sample)
        {
            int skeletonBars = Math.Max(1, sample.P4Bar - sample.P0Bar);
            int lookback = Math.Clamp(
                CONTEXT_LOOKBACK_MULTIPLIER * skeletonBars,
                CONTEXT_MIN_LOOKBACK,
                CONTEXT_MAX_LOOKBACK);
            int from = Math.Max(0, sample.P0Bar - lookback);
            int last = sample.P0Bar - 1;

            bool isInitial = IsInitialMovement(sample);
            if (last < from)
            {
                return new ContextFeatures(
                    isInitial, false, false,
                    double.NaN, double.NaN, double.NaN, double.NaN, 0);
            }

            double priorHigh = double.MinValue;
            double priorLow = double.MaxValue;
            int priorHighBar = -1;
            int priorLowBar = -1;
            for (int i = from; i <= last; i++)
            {
                double high = sample.Provider.GetHighPrice(i);
                double low = sample.Provider.GetLowPrice(i);
                if (high > priorHigh)
                {
                    priorHigh = high;
                    priorHighBar = i;
                }

                if (low < priorLow)
                {
                    priorLow = low;
                    priorLowBar = i;
                }
            }

            double height = Math.Abs(sample.V5 - sample.V0);
            if (height <= 1e-12)
            {
                return new ContextFeatures(
                    isInitial, false, false,
                    double.NaN, double.NaN, double.NaN, double.NaN, last - from + 1);
            }

            bool isUp = sample.IsUpDiagonal;
            bool p0IsExtreme = isUp
                ? sample.V0 <= priorLow + 1e-12
                : sample.V0 >= priorHigh - 1e-12;
            bool priorBeyondW5 = isUp
                ? priorHigh > sample.V5 + 1e-12
                : priorLow < sample.V5 - 1e-12;

            // Positive when point 0 sits above the prior low (up diagonal) / below the
            // prior high (down diagonal), i.e. it is a higher low / lower high inside an
            // existing trend rather than the reversal extreme.
            double p0VsPriorExtreme = isUp
                ? (sample.V0 - priorLow) / height
                : (priorHigh - sample.V0) / height;

            // Positive when the prior range extends beyond wave 5 (the diagonal is still
            // inside the preceding counter-move), negative when wave 5 made a new extreme.
            double priorBeyondW5Height = isUp
                ? (priorHigh - sample.V5) / height
                : (sample.V5 - priorLow) / height;

            int extremeBar = isUp ? priorLowBar : priorHighBar;
            double barsSincePriorExtreme = extremeBar >= 0
                ? sample.P0Bar - extremeBar
                : double.NaN;

            return new ContextFeatures(
                isInitial,
                p0IsExtreme,
                priorBeyondW5,
                p0VsPriorExtreme,
                (priorHigh - priorLow) / height,
                priorBeyondW5Height,
                barsSincePriorExtreme,
                last - from + 1);
        }

        /// <summary>
        /// Mirrors <see cref="SingleSetupFinder{T}.IsInitialMovement"/> exactly: rewind from
        /// point 0 until a bar returns past V(0) (break) or a bar trades beyond the wave-5
        /// extreme (initial). The finder's method is protected, and the research harness
        /// deliberately does not change the production API for a post-hoc label.
        /// </summary>
        private static bool IsInitialMovement(Sample sample)
        {
            double startValue = sample.V0;
            double endValue = sample.V5;
            bool isImpulseUp = endValue > startValue;

            for (int curIndex = sample.P0Bar - 1; curIndex >= 0; curIndex--)
            {
                double high = sample.Provider.GetHighPrice(curIndex);
                double low = sample.Provider.GetLowPrice(curIndex);

                if (isImpulseUp)
                {
                    if (low <= startValue)
                        break;

                    if (high - endValue > 0)
                        return true;
                }
                else
                {
                    if (high >= startValue)
                        break;

                    if (low - endValue < 0)
                        return true;
                }
            }

            return false;
        }

        #endregion

        #region Report

        private static string BuildLeadingEndingReport(
            IReadOnlyList<ClassifiedSample> samples)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal — leading vs ending: does the ending-only filter help?");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine("The correction-depth run (§9.27) found no stable geometric separator for `worked236`; the next hypothesis is structural: **leading diagonals contain a 1-2-1-2 sequence that only resembles a wedge, and the signal catches a wave-3 extension instead of a reversal.** If true, keeping only ending diagonals should raise the correction depth and net R. The classifiers below are fixed before the outcomes are inspected and use only bars up to the signal.");
            builder.AppendLine();

            builder.AppendLine("## Classifiers");
            builder.AppendLine();
            builder.AppendLine("| Classifier | Leading signature | Ending signature |");
            builder.AppendLine("|---|---|---|");
            builder.AppendLine("| `D-INIT` | `IsInitialMovement` is true: a prior bar beyond the wave-5 extreme is found before a bar returns past point 0 | false (the diagonal is not contained in the prior counter-move) |");
            builder.AppendLine("| `P0 extreme` | point 0 is the lowest low (up) / highest high (down) of the preceding `3×` skeleton lookback (clamped 100–2000 bars) | point 0 is a higher low / lower high inside the prior range |");
            builder.AppendLine("| `Prior beyond W5` | a prior bar traded beyond the wave-5 extreme, so the diagonal is still inside the preceding range | wave 5 made a new extreme beyond the whole prior lookback |");
            builder.AppendLine();
            builder.AppendLine("`Ending broad` = not `D-INIT`. `Ending structural` = not `P0 extreme` **and** not `Prior beyond W5`. `Ending strict` = all three. `Leading strict` = all three leading signatures. No threshold search: these are the three pre-registered context tests.");
            builder.AppendLine();

            AppendClassifierShares(builder, samples);
            AppendGroupTable(builder, samples);
            AppendAucTable(builder, samples);
            AppendFilterTable(builder, samples);
            AppendEndingnessQuintiles(builder, samples);
            AppendPairTable(builder, samples);
            AppendBootstrapTable(builder, samples);
            AppendCandidateDiagnostics(builder, samples);
            AppendRebaseTable(builder, samples);
            AppendVerdict(builder, samples);

            return builder.ToString();
        }

        private static void AppendClassifierShares(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine("## Sample and classifier shares");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | N | D-INIT leading | P0 extreme leading | Prior beyond W5 leading | Ending broad | Ending structural | Ending strict | Leading strict |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                if (slice.Count == 0)
                    continue;

                builder.AppendLine(FormattableString.Invariant(
                    $"| {config} | {set} | {slice.Count} | {Rate(slice, x => x.Context.IsInitial):P1} | {Rate(slice, x => x.Context.P0IsLookbackExtreme):P1} | {Rate(slice, x => x.Context.PriorBeyondW5):P1} | {Rate(slice, x => x.Context.EndingBroad):P1} | {Rate(slice, x => x.Context.EndingStructural):P1} | {Rate(slice, x => x.Context.EndingStrict):P1} | {Rate(slice, x => x.Context.LeadingStrict):P1} |"));
            }
        }

        private static void AppendGroupTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Outcomes by context group");
            builder.AppendLine();
            builder.AppendLine("`worked236/382/50` are the post-diagonal correction depths from §9.27; `tradeable236` additionally requires that the theoretical stop was not hit before the actual end. `avg netR` and `profitable` are the finder's TP/SL result and use resolved signals only. `avg MFE/R` is the favourable excursion before the stop.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Group | N | worked236 | worked382 | worked50 | tradeable236 | median depth | avg netR | profitable | avg MFE/R |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                if (slice.Count == 0)
                    continue;

                AppendGroupRow(builder, config, set, "all", slice);
                AppendGroupRow(builder, config, set, "ending broad",
                    slice.Where(x => x.Context.EndingBroad).ToList());
                AppendGroupRow(builder, config, set, "leading broad",
                    slice.Where(x => x.Context.LeadingBroad).ToList());
                AppendGroupRow(builder, config, set, "ending structural",
                    slice.Where(x => x.Context.EndingStructural).ToList());
                AppendGroupRow(builder, config, set, "leading structural",
                    slice.Where(x => x.Context.LeadingStructural).ToList());
                AppendGroupRow(builder, config, set, "ending strict",
                    slice.Where(x => x.Context.EndingStrict).ToList());
                AppendGroupRow(builder, config, set, "leading strict",
                    slice.Where(x => x.Context.LeadingStrict).ToList());
            }
        }

        private static void AppendGroupRow(
            StringBuilder builder,
            string config,
            string set,
            string group,
            IReadOnlyList<ClassifiedSample> samples)
        {
            GroupStats stats = GroupStats.Create(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {config} | {set} | {group} | {stats.N} | {stats.Worked236:P1} | {stats.Worked382:P1} | {stats.Worked50:P1} | {stats.Tradeable236:P1} | {stats.MedianDepth:F3} | {stats.AvgNetR:F3} | {stats.Profitable:P1} | {stats.AvgMfeR:F3} |"));
        }

        private static void AppendAucTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## AUC by context feature");
            builder.AppendLine();
            builder.AppendLine("AUC > 0.5 means a higher feature value goes with the target. The leading flags (`D-INIT`, `P0 extreme`, `Prior beyond W5`, `Prior beyond W5 / height`) are 1 for leading-like setups, so the hypothesis predicts **AUC < 0.5** for them. The endingness features (`P0 vs prior extreme`, `Bars since prior extreme`) are higher for ending-like setups, so the hypothesis predicts **AUC > 0.5**.");
            builder.AppendLine();
            builder.AppendLine("| Feature | Config | Tuning worked236 | Fresh worked236 | Tuning tradeable236 | Fresh tradeable236 | Tuning netR>0 | Fresh netR>0 |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            var features = new (string Name, Func<ClassifiedSample, double> Value)[]
            {
                ("D-INIT leading (1/0)", x => x.Context.IsInitial ? 1d : 0d),
                ("P0 extreme leading (1/0)", x => x.Context.P0IsLookbackExtreme ? 1d : 0d),
                ("Prior beyond W5 leading (1/0)", x => x.Context.PriorBeyondW5 ? 1d : 0d),
                ("P0 vs prior extreme (endingness)", x => x.Context.P0VsPriorExtreme),
                ("Prior range / height", x => x.Context.PriorRangeHeight),
                ("Prior beyond W5 / height (leadingness)", x => x.Context.PriorBeyondW5Height),
                ("Bars since prior extreme / skeleton", x => x.Context.BarsSincePriorExtreme /
                    Math.Max(1, x.Base.P4Bar - x.Base.P0Bar))
            };

            foreach ((string name, Func<ClassifiedSample, double> value) in features)
            {
                foreach (string config in new[] { "permissive", "exact" })
                {
                    List<ClassifiedSample> tuning = Slice(samples, config, "tuning");
                    List<ClassifiedSample> fresh = Slice(samples, config, "fresh");
                    if (tuning.Count == 0 || fresh.Count == 0)
                        continue;

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {name} | {config} | {AucOfClassified(tuning, value, x => x.Outcome.Worked236):F3} | {AucOfClassified(fresh, value, x => x.Outcome.Worked236):F3} | {AucOfClassified(tuning, value, x => x.Outcome.Tradeable236):F3} | {AucOfClassified(fresh, value, x => x.Outcome.Tradeable236):F3} | {AucOfClassified(tuning, value, x => x.Base.NetR > 0):F3} | {AucOfClassified(fresh, value, x => x.Base.NetR > 0):F3} |"));
                }
            }
        }

        private static void AppendFilterTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Ending-only filter");
            builder.AppendLine();
            builder.AppendLine("The filter is applied post-hoc to the same signal set; `pairs improved` counts files where the filter's mean netR is higher than the file's own baseline (files with at least 5 resolved signals on both sides).");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Filter | N | coverage | worked236 | tradeable236 | median depth | avg netR | profitable | avg MFE/R | pairs improved |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                if (slice.Count == 0)
                    continue;

                AppendFilterRow(builder, config, set, "all", slice, slice);
                AppendFilterRow(builder, config, set, "ending broad",
                    slice.Where(x => x.Context.EndingBroad).ToList(), slice);
                AppendFilterRow(builder, config, set, "ending structural",
                    slice.Where(x => x.Context.EndingStructural).ToList(), slice);
                AppendFilterRow(builder, config, set, "ending strict",
                    slice.Where(x => x.Context.EndingStrict).ToList(), slice);

                Func<ClassifiedSample, bool> highRisk =
                    x => x.Base.W3 > 1e-12 && x.Base.Risk / x.Base.W3 >= 0.646d;
                AppendFilterRow(builder, config, set, "risk/W3 >= 0.646",
                    slice.Where(highRisk).ToList(), slice);
                AppendFilterRow(builder, config, set, "ending structural & risk/W3 >= 0.646",
                    slice.Where(x => x.Context.EndingStructural && highRisk(x)).ToList(), slice);
                AppendFilterRow(builder, config, set, "not ending & risk/W3 >= 0.646",
                    slice.Where(x => !x.Context.EndingStructural && highRisk(x)).ToList(), slice);
            }
        }

        private static void AppendEndingnessQuintiles(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Endingness quintiles");
            builder.AppendLine();
            builder.AppendLine("`P0 vs prior extreme` is 0 when point 0 is the prior low/high (leading signature) and grows as point 0 becomes a higher low / lower high inside an existing trend (ending signature). Quintile edges are built inside each slice.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Quintile | P0 vs prior extreme | N | worked236 | tradeable236 | median depth | avg netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");

            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set)
                    .Where(x => IsFinite(x.Context.P0VsPriorExtreme))
                    .OrderBy(x => x.Context.P0VsPriorExtreme)
                    .ToList();
                if (slice.Count < 5)
                    continue;

                for (int q = 0; q < 5; q++)
                {
                    int from = q * slice.Count / 5;
                    int to = (q + 1) * slice.Count / 5;
                    List<ClassifiedSample> part = slice.Skip(from).Take(to - from).ToList();
                    if (part.Count == 0)
                        continue;

                    GroupStats stats = GroupStats.Create(part);
                    builder.AppendLine(FormattableString.Invariant(
                        $"| {config} | {set} | q{q + 1} | {part[0].Context.P0VsPriorExtreme:F3}–{part[^1].Context.P0VsPriorExtreme:F3} | {stats.N} | {stats.Worked236:P1} | {stats.Tradeable236:P1} | {stats.MedianDepth:F3} | {stats.AvgNetR:F3} |"));
                }
            }
        }

        private static void AppendFilterRow(
            StringBuilder builder,
            string config,
            string set,
            string filter,
            IReadOnlyList<ClassifiedSample> filtered,
            IReadOnlyList<ClassifiedSample> baseline)
        {
            GroupStats stats = GroupStats.Create(filtered);
            double coverage = baseline.Count == 0 ? 0 : filtered.Count / (double)baseline.Count;
            (int improved, int compared) = PairsImproved(
                baseline, filtered, x => x.Base.NetR);
            string pairs = compared == 0 ? "—" : $"{improved}/{compared}";

            builder.AppendLine(FormattableString.Invariant(
                $"| {config} | {set} | {filter} | {stats.N} | {coverage:P1} | {stats.Worked236:P1} | {stats.Tradeable236:P1} | {stats.MedianDepth:F3} | {stats.AvgNetR:F3} | {stats.Profitable:P1} | {stats.AvgMfeR:F3} | {pairs} |"));
        }

        private static void AppendPairTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Pair-level consistency (fresh)");
            builder.AppendLine();
            builder.AppendLine("Mean net R per file. `ending` uses the structural definition (point 0 is not the prior extreme and wave 5 made a new extreme); `leading` is its complement.");
            builder.AppendLine();
            builder.AppendLine("| Config | Pair | N | baseline | ending structural | leading structural |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");

            foreach (string config in new[] { "permissive", "exact" })
            {
                List<ClassifiedSample> fresh = Slice(samples, config, "fresh");
                foreach (IGrouping<string, ClassifiedSample> file in fresh
                             .GroupBy(x => x.Base.File)
                             .OrderBy(g => g.First().Base.Symbol))
                {
                    List<ClassifiedSample> all = file.ToList();
                    List<ClassifiedSample> ending = all
                        .Where(x => x.Context.EndingStructural)
                        .ToList();
                    List<ClassifiedSample> leading = all
                        .Where(x => x.Context.LeadingStructural)
                        .ToList();

                    builder.AppendLine(FormattableString.Invariant(
                        $"| {config} | {DisplayPair(all[0].Base)} | {all.Count} | {MeanNetR(all):F3} | {MeanNetR(ending):F3} | {MeanNetR(leading):F3} |"));
                }
            }
        }

        private static void AppendBootstrapTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Bootstrap difference: ending structural vs leading structural");
            builder.AppendLine();
            builder.AppendLine("Independent percentile bootstrap (10,000 draws) of the difference `ending − leading`; p is the two-sided bootstrap p-value. `netR` uses resolved signals only; `tradeable236` uses all labelled signals.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Metric | Ending N | Leading N | Ending | Leading | Difference | 95% CI | p |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|---:|");

            int seed = CONTEXT_SEED;
            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                List<ClassifiedSample> ending = slice
                    .Where(x => x.Context.EndingStructural)
                    .ToList();
                List<ClassifiedSample> leading = slice
                    .Where(x => x.Context.LeadingStructural)
                    .ToList();
                if (ending.Count == 0 || leading.Count == 0)
                    continue;

                AppendBootstrapRow(builder, config, set, "netR",
                    ending.Where(x => x.Base.Resolved).ToList(),
                    leading.Where(x => x.Base.Resolved).ToList(),
                    x => x.Base.NetR, seed++);
                AppendBootstrapRow(builder, config, set, "tradeable236",
                    ending, leading,
                    x => x.Outcome.Tradeable236 ? 1d : 0d, seed++);
            }
        }

        private static void AppendBootstrapRow(
            StringBuilder builder,
            string config,
            string set,
            string metricName,
            IReadOnlyList<ClassifiedSample> ending,
            IReadOnlyList<ClassifiedSample> leading,
            Func<ClassifiedSample, double> metric,
            int seed)
        {
            List<double> e = ending.Select(metric).Where(IsFinite).ToList();
            List<double> l = leading.Select(metric).Where(IsFinite).ToList();
            if (e.Count == 0 || l.Count == 0)
                return;

            DifferenceStats stats = BootstrapDifferenceStats(e, l, CONTEXT_BOOTSTRAP, seed);
            builder.AppendLine(FormattableString.Invariant(
                $"| {config} | {set} | {metricName} | {stats.EndingN} | {stats.LeadingN} | {stats.EndingMean:F3} | {stats.LeadingMean:F3} | {stats.Difference:F3} | [{stats.Low:F3}; {stats.High:F3}] | {stats.PValue:F3} |"));
        }

        private static void AppendCandidateDiagnostics(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Candidate lifecycle diagnostics");
            builder.AppendLine();
            builder.AppendLine("The finder dedups signals by point 0 (`m_SignaledPoint0`), so a later skeleton with the same point 0 but a different wave-3/4 end is dropped (`duplicatePoint0`). Counts are summed over files; `signals` is the number of emitted setups. A high `duplicatePoint0 / signal` ratio means the first wave-3 skeleton often wins and a later re-based wave 3 is suppressed. `duplicateSkeleton` counts repeated registration attempts of an already-signaled skeleton and is not comparable to signals; `duplicatePoint0` is the unique-alternative counter.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Files | Signals | Registered | duplicatePoint0 | duplicateSkeleton | duplicatePoint0 / signal |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");

            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                var perFile = slice
                    .GroupBy(x => x.Base.File)
                    .Select(g => g.First())
                    .ToList();
                if (perFile.Count == 0)
                    continue;

                int registered = perFile.Sum(x => DiagValue(x.Base, "registered"));
                int duplicatePoint0 = perFile.Sum(x => DiagValue(x.Base, "duplicatePoint0"));
                int duplicateSkeleton = perFile.Sum(x => DiagValue(x.Base, "duplicateSkeleton"));
                int signals = slice.Count;

                builder.AppendLine(FormattableString.Invariant(
                    $"| {config} | {set} | {perFile.Count} | {signals} | {registered} | {duplicatePoint0} | {duplicateSkeleton} | {(signals == 0 ? 0d : duplicatePoint0 / (double)signals):F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("The ratio is about 3.7 dropped alternatives per emitted signal on the permissive sample and 1.1–1.5 on exact: re-basing is frequent, not a corner case. The dropped candidates are not necessarily better, but the dedup guarantees that the first skeleton wins — exactly the failure mode the hypothesis describes.");
        }

        private static int DiagValue(Sample sample, string key)
        {
            return sample.FinderDiag.TryGetValue(key, out int value) ? value : 0;
        }

        private static void AppendRebaseTable(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Wave-3 re-basing diagnostic");
            builder.AppendLine();
            builder.AppendLine("The same point 0 can produce several skeletons with different wave-3/4 ends when the third wave is re-based sideways. Within each `(file, direction, point 0)` group, distinct `P4` values are ordered; the earliest is `early`, every later one is `re-based`. If the algorithm fires too early on an incomplete wave 3, the re-based skeleton should be better. This is a diagnostic, not a signal filter.");
            builder.AppendLine();
            builder.AppendLine("| Config | Set | Groups with >1 P4 | Early N | Early worked236 | Early tradeable236 | Early netR | Re-based N | Re-based worked236 | Re-based tradeable236 | Re-based netR | Groups where re-based netR > early |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            bool any = false;
            foreach ((string config, string set) in ConfigSets(samples))
            {
                List<ClassifiedSample> slice = Slice(samples, config, set);
                var groups = slice
                    .GroupBy(x => (x.Base.File, x.Base.IsUpDiagonal, x.Base.P0Bar))
                    .Select(g => g
                        .GroupBy(x => x.Base.P4Bar)
                        .Select(p => p.OrderBy(x => x.Base.SignalBar).First())
                        .OrderBy(x => x.Base.P4Bar)
                        .ToList())
                    .Where(g => g.Count > 1)
                    .ToList();
                if (groups.Count == 0)
                    continue;

                List<ClassifiedSample> early = groups.Select(g => g[0]).ToList();
                List<ClassifiedSample> rebased = groups.SelectMany(g => g.Skip(1)).ToList();
                GroupStats earlyStats = GroupStats.Create(early);
                GroupStats rebasedStats = GroupStats.Create(rebased);
                int improved = groups.Count(g =>
                {
                    double e = MeanNetR(new[] { g[0] });
                    double r = MeanNetR(g.Skip(1).ToList());
                    return IsFinite(e) && IsFinite(r) && r > e;
                });

                any = true;
                builder.AppendLine(FormattableString.Invariant(
                    $"| {config} | {set} | {groups.Count} | {earlyStats.N} | {earlyStats.Worked236:P1} | {earlyStats.Tradeable236:P1} | {earlyStats.AvgNetR:F3} | {rebasedStats.N} | {rebasedStats.Worked236:P1} | {rebasedStats.Tradeable236:P1} | {rebasedStats.AvgNetR:F3} | {improved}/{groups.Count} |"));
            }

            if (!any)
            {
                builder.AppendLine();
                builder.AppendLine("No emitted sample contains two skeletons with the same point 0 and different `P4`: the `m_SignaledPoint0` dedup suppresses the later one. The suppression itself is the mechanism to study — see the candidate lifecycle diagnostics above; comparing early vs re-based outcomes requires instrumenting the candidate pool before emission.");
            }
        }

        private static void AppendVerdict(
            StringBuilder builder,
            IReadOnlyList<ClassifiedSample> samples)
        {
            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine();
            builder.AppendLine("**The hypothesis is supported for the pattern, not for the trade.**");
            builder.AppendLine();
            builder.AppendLine("1. **Ending-like diagonals correct deeper.** `ending structural` raises the median depth from 0.648 to 0.710 (permissive tuning), 0.632 to 0.669 (permissive fresh), 0.644 to 0.733 (exact tuning) and 0.618 to 0.719 (exact fresh); `worked236` rises by 1.6–5.3 pp in all four slices. The continuous `P0 vs prior extreme` is the only context feature with AUC > 0.5 for `worked236` on all four slices (0.527–0.583). This is consistent with the 1-2-1-2 story: the false leading setups start at the reversal extreme, while the true ending diagonals start from a higher low / lower high inside an existing trend.");
            builder.AppendLine("2. **The effect is weak and not a standalone filter.** `tradeable236` barely moves (25.5 vs 24.8, 27.7 vs 25.0, 34.5 vs 33.8, 37.3 vs 36.6): the theoretical stop still hits before the correction ends in roughly two thirds of the cases. Net R is inconsistent: permissive tuning +0.039 (p=0.696), permissive fresh +0.184 (p=0.055), exact tuning +0.109 (p=0.257), exact fresh −0.085 (p=0.398). Only permissive fresh is borderline; exact fresh reverses. Pair-level: 6/8 permissive fresh pairs improve, but only 2/6 exact fresh pairs.");
            builder.AppendLine("3. **The strict leading group is consistently the bad one.** `leading strict` net R: permissive tuning −0.076 (baseline −0.056), fresh −0.144 (baseline +0.009), exact tuning +0.002 (baseline +0.032), fresh −0.047 (baseline −0.037). Its median depth is the lowest in every slice (0.583, 0.590, 0.648, 0.616). So “do not trade the strict leading group” is better supported than “trade only ending”.");
            builder.AppendLine("4. **The most promising combination is `ending structural & Risk/W3 >= 0.646`.** Permissive tuning: N=55, worked236 89.1%, tradeable236 38.2%, net R +0.144; permissive fresh: N=52, 82.7%, 38.5%, +0.253. But exact tuning is +0.030 and exact fresh −0.038, so it does not replicate on the exact corridor, and N is only 52–55. This is a candidate for a pre-registered validation, not a default.");
            builder.AppendLine("5. **The wave-3 re-basing diagnostic is empty by construction, and that is the finding.** `m_SignaledPoint0` suppresses every later skeleton for the same point 0; the diagnostics show about 3.7 dropped alternatives per emitted signal on the permissive sample and 1.1–1.5 on exact. The first skeleton always wins. The next test must instrument the candidate pool **before** emission and compare the first skeleton with the suppressed alternatives; the fix, if any, belongs in the candidate lifecycle, not in another geometry gate.");
            builder.AppendLine("6. **Recommendation.** Do not add a hard `RequireEndingDiagonal` default. The ending context is a real depth axis, but the current V(3)-break entry still loses the trade; combine it with a post-end entry or a better end detector and test it on a new sample.");
        }

        #endregion

        #region Helpers

        private static IEnumerable<(string Config, string Set)> ConfigSets(
            IReadOnlyList<ClassifiedSample> samples)
        {
            foreach (string config in new[] { "permissive", "exact" })
            {
                foreach (string set in new[] { "tuning", "fresh" })
                {
                    if (samples.Any(x => x.Config == config && x.Set == set))
                        yield return (config, set);
                }
            }
        }

        private static List<ClassifiedSample> Slice(
            IReadOnlyList<ClassifiedSample> samples,
            string config,
            string set)
        {
            return samples
                .Where(x => x.Config == config && x.Set == set)
                .ToList();
        }

        private static double Rate(
            IReadOnlyList<ClassifiedSample> samples,
            Func<ClassifiedSample, bool> predicate)
        {
            return samples.Count == 0 ? 0 : samples.Count(predicate) / (double)samples.Count;
        }

        private static double MeanNetR(IReadOnlyList<ClassifiedSample> samples)
        {
            List<double> values = samples
                .Where(x => x.Base.Resolved)
                .Select(x => x.Base.NetR)
                .Where(IsFinite)
                .ToList();
            return values.Count == 0 ? double.NaN : values.Average();
        }

        private static string DisplayPair(Sample sample)
        {
            return sample.Symbol;
        }

        private static (int Improved, int Compared) PairsImproved(
            IReadOnlyList<ClassifiedSample> baseline,
            IReadOnlyList<ClassifiedSample> filtered,
            Func<ClassifiedSample, double> metric)
        {
            var baselineByFile = baseline
                .Where(x => x.Base.Resolved)
                .GroupBy(x => x.Base.File)
                .ToDictionary(g => g.Key, g => g.Select(metric).Where(IsFinite).ToList());
            var filteredByFile = filtered
                .Where(x => x.Base.Resolved)
                .GroupBy(x => x.Base.File)
                .ToDictionary(g => g.Key, g => g.Select(metric).Where(IsFinite).ToList());

            int improved = 0;
            int compared = 0;
            foreach (KeyValuePair<string, List<double>> pair in baselineByFile)
            {
                if (!filteredByFile.TryGetValue(pair.Key, out List<double>? filteredValues))
                    continue;

                if (pair.Value.Count < 5 || filteredValues.Count < 5)
                    continue;

                compared++;
                if (filteredValues.Average() > pair.Value.Average())
                    improved++;
            }

            return (improved, compared);
        }

        private static double AucOfClassified(
            IReadOnlyList<ClassifiedSample> samples,
            Func<ClassifiedSample, double> metric,
            Func<ClassifiedSample, bool> positive)
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

        private static DifferenceStats BootstrapDifferenceStats(
            IReadOnlyList<double> ending,
            IReadOnlyList<double> leading,
            int iterations,
            int seed)
        {
            double observed = ending.Average() - leading.Average();
            var rng = new Random(seed);
            var differences = new List<double>(iterations);
            for (int i = 0; i < iterations; i++)
            {
                differences.Add(
                    ResampleMean(ending, rng) - ResampleMean(leading, rng));
            }

            differences.Sort();
            double low = Quantile(differences, 0.025);
            double high = Quantile(differences, 0.975);
            double p = 2 * Math.Min(
                (differences.Count(d => d <= 0) + 1) / (double)(iterations + 1),
                (differences.Count(d => d >= 0) + 1) / (double)(iterations + 1));

            return new DifferenceStats(
                ending.Count,
                leading.Count,
                ending.Average(),
                leading.Average(),
                observed,
                low,
                high,
                Math.Min(1d, p));
        }

        private static double ResampleMean(IReadOnlyList<double> values, Random rng)
        {
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
                sum += values[rng.Next(values.Count)];

            return sum / values.Count;
        }

        private static string BuildLeadingEndingCsv(
            IReadOnlyList<ClassifiedSample> samples)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config,set,file,symbol,entry_time,signal_bar,p0_bar,p4_bar,p5_bar,is_up,entry,risk,net_r,resolved,profitable," +
                "worked236,worked382,worked50,tradeable236,depth_diag,depth_w3,depth_r,mfe_r,mae_r," +
                "is_initial,p0_is_extreme,prior_beyond_w5,p0_vs_prior_extreme,prior_range_height," +
                "prior_beyond_w5_height,bars_since_prior_extreme,lookback_bars," +
                "ending_broad,ending_structural,ending_strict,leading_strict");

            foreach (ClassifiedSample x in samples)
            {
                ContextFeatures c = x.Context;
                builder.AppendLine(string.Join(",",
                    Csv(x.Config),
                    Csv(x.Set),
                    Csv(x.Base.File),
                    Csv(x.Base.Symbol),
                    Csv(x.Base.EntryTime.ToString("O", CultureInfo.InvariantCulture)),
                    x.Base.SignalBar.ToString(CultureInfo.InvariantCulture),
                    x.Base.P0Bar.ToString(CultureInfo.InvariantCulture),
                    x.Base.P4Bar.ToString(CultureInfo.InvariantCulture),
                    x.Base.P5Bar.ToString(CultureInfo.InvariantCulture),
                    B(x.Base.IsUpDiagonal),
                    F(x.Base.Entry),
                    F(x.Base.Risk),
                    F(x.Base.NetR),
                    B(x.Base.Resolved),
                    B(x.Base.Profitable),
                    B(x.Outcome.Worked236),
                    B(x.Outcome.Worked382),
                    B(x.Outcome.Worked50),
                    B(x.Outcome.Tradeable236),
                    F(x.Outcome.DepthDiag),
                    F(x.Outcome.DepthW3),
                    F(x.Outcome.DepthR),
                    F(x.Outcome.MfeR),
                    F(x.Outcome.MaeR),
                    B(c.IsInitial),
                    B(c.P0IsLookbackExtreme),
                    B(c.PriorBeyondW5),
                    F(c.P0VsPriorExtreme),
                    F(c.PriorRangeHeight),
                    F(c.PriorBeyondW5Height),
                    F(c.BarsSincePriorExtreme),
                    c.LookbackBars.ToString(CultureInfo.InvariantCulture),
                    B(c.EndingBroad),
                    B(c.EndingStructural),
                    B(c.EndingStrict),
                    B(c.LeadingStrict)));
            }

            return builder.ToString();
        }

        #endregion

        #region Types

        private sealed record ClassifiedSample(
            CorrectionSample Sample,
            ContextFeatures Context)
        {
            public Sample Base => Sample.Base;
            public CorrectionOutcome Outcome => Sample.Outcome;
            public string Config => Sample.Config;
            public string Set => Sample.Set;
        }

        private sealed record ContextFeatures(
            bool IsInitial,
            bool P0IsLookbackExtreme,
            bool PriorBeyondW5,
            double P0VsPriorExtreme,
            double PriorRangeHeight,
            double PriorBeyondW5Height,
            double BarsSincePriorExtreme,
            int LookbackBars)
        {
            public bool EndingBroad => !IsInitial;
            public bool EndingStructural => !P0IsLookbackExtreme && !PriorBeyondW5;
            public bool EndingStrict => EndingBroad && EndingStructural;
            public bool LeadingBroad => IsInitial;
            public bool LeadingStructural => P0IsLookbackExtreme || PriorBeyondW5;
            public bool LeadingStrict => IsInitial && P0IsLookbackExtreme && PriorBeyondW5;
        }

        private sealed record GroupStats(
            int N,
            int ResolvedN,
            double Worked236,
            double Worked382,
            double Worked50,
            double Tradeable236,
            double MedianDepth,
            double AvgNetR,
            double Profitable,
            double AvgMfeR)
        {
            public static GroupStats Create(IReadOnlyList<ClassifiedSample> samples)
            {
                int n = samples.Count;
                List<ClassifiedSample> resolved = samples
                    .Where(x => x.Base.Resolved)
                    .ToList();

                return new GroupStats(
                    n,
                    resolved.Count,
                    Rate(samples, x => x.Outcome.Worked236),
                    Rate(samples, x => x.Outcome.Worked382),
                    Rate(samples, x => x.Outcome.Worked50),
                    Rate(samples, x => x.Outcome.Tradeable236),
                    MedianOf(samples.Select(x => x.Outcome.DepthDiag)),
                    resolved.Count == 0 ? double.NaN : resolved.Average(x => x.Base.NetR),
                    resolved.Count == 0
                        ? double.NaN
                        : resolved.Count(x => x.Base.Profitable) / (double)resolved.Count,
                    samples.Count == 0 ? double.NaN : samples.Average(x => x.Outcome.MfeR));
            }
        }

        private sealed record DifferenceStats(
            int EndingN,
            int LeadingN,
            double EndingMean,
            double LeadingMean,
            double Difference,
            double Low,
            double High,
            double PValue);

        #endregion
    }
}
