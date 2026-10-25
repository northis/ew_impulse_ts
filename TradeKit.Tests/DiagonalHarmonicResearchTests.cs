using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Research harness for the "diagonal + harmonic" hypothesis: does requiring a confirmed
    /// harmonic pattern at the diagonal's wave-5 extreme turn the diagonal setup into a
    /// profitable one?
    /// <para>
    /// The diagonal is replayed exactly as in <c>DiagonalExactRunResearchTests</c> (the
    /// <c>W2/W1 >= 0.5</c> and <c>W4/W3 &lt;= 0.382</c> corridor with the 23.6% target), so the
    /// baseline is the best diagonal configuration found so far. A second, permissive diagonal
    /// baseline is reported for robustness. On the same provider a wide harmonic search is run
    /// once per file; its patterns are then filtered post-hoc by every parameter of interest.
    /// The join is causal: a harmonic pattern is only usable when its point D was already
    /// confirmed on or before the diagonal signal bar.
    /// </para>
    /// <para>
    /// Writes <c>reports/diagonal_harmonic.md</c>. Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalHarmonic"</c>.
    /// </para>
    /// </summary>
    public partial class DiagonalGeometryResearchTests
    {
        /// <summary>
        /// The wide harmonic search that produces the superset every post-hoc filter is applied
        /// to. Fibonacci tolerance is wider than any tested threshold, the leg-duration
        /// symmetry check is off, and the pivot range covers every preset. This makes the
        /// post-hoc sweep exact for the Fib error, symmetry and pivot bounds: a pattern found
        /// here at a tighter setting would also have been found by a run with that setting.
        /// </summary>
        private const int HARMONIC_BARS_DEPTH = 500;
        private const int HARMONIC_MIN_PIVOT = 2;
        private const int HARMONIC_MAX_PIVOT = 60;
        private const double HARMONIC_FIB_ERROR = 30d;

        /// <summary>
        /// The wide run completes a pattern on the bar that made its point D. This is the only
        /// setting that captures a D formed on the diagonal's own signal bar - the diagonal
        /// enters at that bar's close, so a pattern confirmed one bar later cannot gate it.
        /// Larger confirmation lags are modelled post-hoc by requiring the D to survive the
        /// trailing bars, which is exact for the point D of a given XABCD.
        /// </summary>
        private const int HARMONIC_D_CONFIRMATION = 0;

        /// <summary>The widest time distance between the harmonic D and the diagonal wave 5.</summary>
        private const int HARMONIC_MAX_BAR_TOLERANCE = 40;

        /// <summary>Minimum number of train signals a sweep cell must keep to be considered.</summary>
        private const int MIN_TRAIN_SIGNALS = 25;

        private static readonly DiagonalRunConfig EXACT_CORRIDOR = new(
            "exact corridor", 1.0d, DiagonalTakeProfitMode.DIAGONAL_RETRACE,
            MinWave2Retrace: 0.5d, MaxWave4RetraceW3: 0.382d,
            RequireInsideWedge: false, MaxSpillAreaRatio: 1.0d,
            MinWave4Wave2Level: 0d, MinWave4RetraceW3: 0d, MinRiskRewardRatio: 0d);

        private static readonly DiagonalRunConfig PERMISSIVE = new(
            "permissive", 1.5d, DiagonalTakeProfitMode.RISK_RATIO,
            MinWave2Retrace: 0d, MaxWave4RetraceW3: 0d,
            RequireInsideWedge: false, MaxSpillAreaRatio: 1.0d,
            MinWave4Wave2Level: 0d, MinWave4RetraceW3: 0d, MinRiskRewardRatio: 0d);

        private static readonly ConfluenceConfig DEFAULT_CONFLUENCE =
            new("lag 0 / ±3 bars / 25%", ConfirmationBars: 0, MaxBarDistance: 3, MaxPriceDistance: 0.25d);

        private static readonly HarmonicPatternType[] ALL_TYPES =
        {
            HarmonicPatternType.GARTLEY,
            HarmonicPatternType.BAT,
            HarmonicPatternType.BUTTERFLY,
            HarmonicPatternType.CRAB,
            HarmonicPatternType.SHARK,
            HarmonicPatternType.CYPHER
        };

        [Test]
        [Explicit("Research harness - run manually to (re)generate reports/diagonal_harmonic.md")]
        [Category("Research")]
        public void DiagonalHarmonic_Confluence_Report()
        {
            string dataDir = FindDataDir();
            var files = new List<string>();

            foreach (string file in H1_FILES.Concat(M15_FILES))
            {
                if (File.Exists(Path.Combine(dataDir, file)))
                    files.Add(file);
                else
                    TestContext.Out.WriteLine($"SKIP (missing): {file}");
            }

            Assert.That(files, Is.Not.Empty, "No archive file was found under data/.");

            var results = new List<FileResult>();
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                file =>
                {
                    FileResult result = RunCombinedFile(Path.Combine(dataDir, file), file);
                    lock (sync)
                    {
                        results.Add(result);
                    }

                    TestContext.Out.WriteLine(
                        $"{file}: bars={result.Bars}, patterns={result.PatternCount}, " +
                        $"exact={result.Exact.Count}, permissive={result.Permissive.Count}");
                });

            Assert.That(results, Is.Not.Empty, "The combined replay produced no results.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_harmonic.md");
            File.WriteAllText(reportPath, BuildHarmonicReport(results, files.Count));
            TestContext.Out.WriteLine($"Wrote {reportPath}");
        }

        #region Replay

        private static FileResult RunCombinedFile(string path, string fileName)
        {
            ITimeFrame timeFrame = fileName.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(path);

            List<HarmPattern> patterns = CollectHarmonicPatterns(provider);

            List<Sample> exact = RunDiagonalSamples(provider, fileName, EXACT_CORRIDOR);
            List<Sample> permissive = RunDiagonalSamples(provider, fileName, PERMISSIVE);

            Dictionary<Sample, List<HarmMatch>> harmony =
                BuildHarmonyIndex(patterns, exact.Concat(permissive));

            var byType = new Dictionary<HarmonicPatternType, int>();
            foreach (HarmPattern pattern in patterns)
            {
                byType.TryGetValue(pattern.Type, out int count);
                byType[pattern.Type] = count + 1;
            }

            return new FileResult
            {
                File = fileName,
                Bars = provider.Count,
                Exact = exact,
                Permissive = permissive,
                Harmony = harmony,
                PatternCount = patterns.Count,
                PatternByType = byType
            };
        }

        /// <summary>
        /// Replays one diagonal configuration and converts every emitted signal into a
        /// <see cref="Sample"/>. Mirrors <c>RunExactFile</c> so the exact corridor stays
        /// byte-for-byte comparable with the existing diagonal reports.
        /// </summary>
        private static List<Sample> RunDiagonalSamples(
            TestBarsProvider provider,
            string fileName,
            DiagonalRunConfig config,
            bool recordSkeletons = false,
            Action<DiagonalSetupFinder>? onFinished = null)
        {
            var finder = new DiagonalSetupFinder(
                provider,
                provider.BarSymbol,
                new EWParams(0, 0.1, 10),
                takeProfitRatio: config.TakeProfitRatio,
                requireWave5Ratio: false,
                requireWave4Ratio: false,
                requireInitialDiagonal: false,
                takeProfitMode: config.TakeProfitMode,
                minConvergence: 0,
                maxConvergence: 0,
                requireInsideWedge: config.RequireInsideWedge,
                maxSpillAreaRatio: config.MaxSpillAreaRatio,
                minWave3Penetration: 0.03,
                maxWaveDurationRatio: 8.0,
                minWave4Wave2Level: config.MinWave4Wave2Level,
                minWave4RetraceW3: config.MinWave4RetraceW3,
                maxWave4RetraceW3: config.MaxWave4RetraceW3,
                requireWave4Shorter: true,
                requireWave2Shorter: false,
                minWave2Retrace: config.MinWave2Retrace,
                maxWave2Retrace: 0,
                maxWave5SpillRatio: 0,
                minRiskRewardRatio: config.MinRiskRewardRatio,
                triggerMode: config.TriggerMode,
                recordSkeletons: recordSkeletons);

            var records = new List<SignalRecord>();
            SignalRecord? pending = null;

            finder.OnEnter += (_, args) =>
            {
                pending = new SignalRecord(args);
                records.Add(pending);
            };
            finder.OnTakeProfit += (_, args) =>
            {
                if (pending != null)
                {
                    pending.Outcome = "TP";
                    pending.ExitPrice = args.Level.Value;
                    pending.OutcomeBar = args.Level.BarIndex;
                    pending = null;
                }
            };
            finder.OnStopLoss += (_, args) =>
            {
                if (pending != null)
                {
                    pending.Outcome = "SL";
                    pending.ExitPrice = args.Level.Value;
                    pending.OutcomeBar = args.Level.BarIndex;
                    pending = null;
                }
            };

            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            onFinished?.Invoke(finder);

            string symbol = fileName[..fileName.IndexOf('_')];
            string tf = fileName.Contains("_m15_") ? "m15" : "h1";
            var samples = new List<Sample>(records.Count);

            foreach (SignalRecord record in records)
            {
                Sample? sample = BuildSample(provider, finder, record, fileName, symbol, tf);
                if (sample != null)
                    samples.Add(sample);
            }

            return samples;
        }

        /// <summary>
        /// Runs the widest harmonic search once per file. Every completed XABCD is recorded
        /// with the attributes the post-hoc filters need, so the sweep does not replay the
        /// finder for each parameter combination.
        /// </summary>
        private static List<HarmPattern> CollectHarmonicPatterns(TestBarsProvider provider)
        {
            var parameters = new HarmonicParams
            {
                BarsDepth = HARMONIC_BARS_DEPTH,
                MinPivotPeriod = HARMONIC_MIN_PIVOT,
                MaxPivotPeriod = HARMONIC_MAX_PIVOT,
                DConfirmationBars = HARMONIC_D_CONFIRMATION,
                MinBarsBeforePivot = 3,
                FibErrorPercent = HARMONIC_FIB_ERROR,
                LegAsymmetryPercent = 250d,
                CheckLegSymmetry = false,
                MinimumScore = 0d,
                UseBullish = true,
                UseBearish = true
            };

            var finder = new HarmonicPatternFinder(provider, parameters);
            var result = new List<HarmPattern>();

            for (int i = 0; i < provider.Count; i++)
            {
                IReadOnlyList<HarmonicItem> items = finder.FindPatterns(i);
                for (int k = 0; k < items.Count; k++)
                    result.Add(ToHarmPattern(provider, items[k]));
            }

            return result;
        }

        private static HarmPattern ToHarmPattern(TestBarsProvider provider, HarmonicItem item)
        {
            int x = item.ItemX.BarIndex;
            int a = item.ItemA.BarIndex;
            int b = item.ItemB.BarIndex;
            int c = item.ItemC.BarIndex;
            int d = item.ItemD.BarIndex;

            (double maxRatio, double minRatio) = GetLegRatios(a - x, b - a, c - b, d - c);

            return new HarmPattern(
                item.PatternType,
                item.IsBull,
                d,
                item.ItemD.Value,
                item.PivotPeriod,
                item.LengthBars,
                GetMaxLegError(item.Score),
                maxRatio,
                minRatio,
                item.Score.Total,
                item.PatternHeight,
                GetSurvival(provider, item, 3));
        }

        /// <summary>
        /// Whether the point D would still be a confirmed pivot after <c>lag</c> trailing bars.
        /// The finder applies a strict comparison: an equal low/high does not invalidate the
        /// pivot, exactly as in the Pine reference.
        /// </summary>
        private static bool[] GetSurvival(
            TestBarsProvider provider, HarmonicItem item, int maxLag)
        {
            var result = new bool[maxLag + 1];
            result[0] = true;

            int dIndex = item.ItemD.BarIndex;
            double dValue = item.ItemD.Value;
            bool isBull = item.IsBull;

            for (int lag = 1; lag <= maxLag; lag++)
            {
                int bar = dIndex + lag;
                bool survives = result[lag - 1] && bar < provider.Count;
                if (survives)
                {
                    survives = isBull
                        ? provider.GetLowPrice(bar) >= dValue
                        : provider.GetHighPrice(bar) <= dValue;
                }

                result[lag] = survives;
            }

            return result;
        }

        private static double GetMaxLegError(HarmonicScore score)
        {
            double result = 0d;
            if (score.AbToXaError.HasValue)
                result = Math.Max(result, score.AbToXaError.Value);
            if (score.BcToAbError.HasValue)
                result = Math.Max(result, score.BcToAbError.Value);
            if (score.CdToBcError.HasValue)
                result = Math.Max(result, score.CdToBcError.Value);
            if (score.FinalError.HasValue)
                result = Math.Max(result, score.FinalError.Value);

            return result;
        }

        /// <summary>
        /// The per-leg deviation from the average of the other three legs. Reproduces the
        /// <c>TestSymmetry</c> check exactly: a leg passes at <c>asymmetryPercent</c> when its
        /// ratio is within <c>[1 - a, 1 + a]</c>.
        /// </summary>
        private static (double MaxRatio, double MinRatio) GetLegRatios(
            int xa, int ab, int bc, int cd)
        {
            int[] legs = { xa, ab, bc, cd };
            double max = 0d;
            double min = double.PositiveInfinity;

            for (int i = 0; i < legs.Length; i++)
            {
                double others = 0d;
                for (int j = 0; j < legs.Length; j++)
                {
                    if (j != i)
                        others += legs[j];
                }

                double average = others / 3d;
                if (average <= 0d)
                    continue;

                double ratio = legs[i] / average;
                max = Math.Max(max, ratio);
                min = Math.Min(min, ratio);
            }

            return (max, min == double.PositiveInfinity ? 0d : min);
        }

        /// <summary>
        /// Indexes the harmonic patterns by their D bar and, for every diagonal sample, keeps
        /// the patterns of the matching direction whose D sits near the diagonal's wave-5
        /// extreme. Price distance is measured relative to the diagonal height, so it is
        /// comparable across instruments and timeframes.
        /// </summary>
        private static Dictionary<Sample, List<HarmMatch>> BuildHarmonyIndex(
            IReadOnlyList<HarmPattern> patterns, IEnumerable<Sample> samples)
        {
            var byBar = new Dictionary<int, List<HarmPattern>>();
            foreach (HarmPattern pattern in patterns)
            {
                if (!byBar.TryGetValue(pattern.DBar, out List<HarmPattern>? bucket))
                {
                    bucket = new List<HarmPattern>();
                    byBar[pattern.DBar] = bucket;
                }

                bucket.Add(pattern);
            }

            var result = new Dictionary<Sample, List<HarmMatch>>();
            foreach (Sample sample in samples)
            {
                var matches = new List<HarmMatch>();
                double height = Math.Abs(sample.V5 - sample.V0);

                if (height > 1e-12d)
                {
                    // The trade is counter to the diagonal: an up diagonal is a SELL, so it
                    // needs a bearish harmonic pattern at the wave-5 high.
                    bool tradeIsBuy = !sample.IsUpDiagonal;

                    for (int bar = sample.P5Bar - HARMONIC_MAX_BAR_TOLERANCE;
                         bar <= sample.P5Bar + HARMONIC_MAX_BAR_TOLERANCE;
                         bar++)
                    {
                        if (!byBar.TryGetValue(bar, out List<HarmPattern>? bucket))
                            continue;

                        foreach (HarmPattern pattern in bucket)
                        {
                            if (pattern.IsBull != tradeIsBuy)
                                continue;

                            double distance = Math.Abs(pattern.DValue - sample.V5) / height;
                            matches.Add(new HarmMatch(pattern, distance));
                        }
                    }
                }

                result[sample] = matches;
            }

            return result;
        }

        #endregion

        #region Filtering

        private static List<Sample> FilterByHarmony(
            IReadOnlyList<Sample> samples,
            IReadOnlyDictionary<Sample, List<HarmMatch>> harmony,
            HarmSearchConfig search,
            ConfluenceConfig confluence)
        {
            var result = new List<Sample>();

            foreach (Sample sample in samples)
            {
                if (!harmony.TryGetValue(sample, out List<HarmMatch>? matches))
                    continue;

                foreach (HarmMatch match in matches)
                {
                    HarmPattern pattern = match.Pattern;

                    // Causal join: the pattern must be confirmed on or before the signal bar.
                    if (confluence.ConfirmationBars < 0 ||
                        confluence.ConfirmationBars >= pattern.Survives.Length)
                        continue;
                    if (!pattern.Survives[confluence.ConfirmationBars])
                        continue;
                    if (pattern.DBar + confluence.ConfirmationBars > sample.SignalBar)
                        continue;

                    // Confluence: the harmonic D is the diagonal's wave-5 extreme.
                    if (Math.Abs(pattern.DBar - sample.P5Bar) > confluence.MaxBarDistance)
                        continue;
                    if (match.PriceDistance > confluence.MaxPriceDistance)
                        continue;

                    // Harmonic quality thresholds.
                    if (pattern.MaxLegError > search.MaxLegError)
                        continue;
                    if (pattern.MaxLegRatio > search.MaxLegRatio)
                        continue;
                    if (pattern.MinLegRatio < search.MinLegRatio)
                        continue;
                    if (pattern.PivotPeriod < search.MinPivot || pattern.PivotPeriod > search.MaxPivot)
                        continue;
                    if (pattern.Score < search.MinScore)
                        continue;
                    if (pattern.LengthBars < search.MinLengthBars)
                        continue;
                    if (search.Type.HasValue && pattern.Type != search.Type.Value)
                        continue;

                    result.Add(sample);
                    break;
                }
            }

            return result;
        }

        private static List<HarmSearchConfig> BuildSearchGrid()
        {
            var result = new List<HarmSearchConfig>();
            double[] fibErrors = { 0.10d, 0.15d, 0.20d, 0.25d, 0.30d };
            (string Name, double Max, double Min)[] symmetries =
            {
                ("off", double.PositiveInfinity, 0d),
                ("250%", 3.5d, 0d),
                ("150%", 2.5d, 0d),
                ("100%", 2.0d, 0d)
            };
            (string Name, int Min, int Max)[] pivots =
            {
                ("2-60", 2, 60),
                ("3-20", 3, 20),
                ("5-40", 5, 40),
                ("10-60", 10, 60)
            };
            double[] scores = { 0d, 0.5d, 0.6d };

            foreach (double fib in fibErrors)
            {
                foreach ((string symName, double max, double min) in symmetries)
                {
                    foreach ((string pivotName, int pivotMin, int pivotMax) in pivots)
                    {
                        foreach (double score in scores)
                        {
                            result.Add(new HarmSearchConfig(
                                FormattableString.Invariant(
                                    $"fib {fib:P0} / sym {symName} / piv {pivotName} / score {score:F1}"),
                                fib, max, min, pivotMin, pivotMax, score, 0, null));
                        }
                    }
                }
            }

            return result;
        }

        private static List<ConfluenceConfig> BuildConfluenceGrid()
        {
            var result = new List<ConfluenceConfig>();
            int[] lags = { 0, 1, 2, 3 };
            int[] bars = { 0, 1, 2, 3, 5, 10, 20 };
            double[] prices = { 0.05d, 0.10d, 0.25d, 0.50d, double.PositiveInfinity };

            foreach (int lag in lags)
            {
                foreach (int bar in bars)
                {
                    foreach (double price in prices)
                    {
                        string priceName = double.IsPositiveInfinity(price)
                            ? "any"
                            : price.ToString("P0", CultureInfo.InvariantCulture);
                        result.Add(new ConfluenceConfig(
                            $"lag {lag} / ±{bar} bars / {priceName}",
                            lag, bar, price));
                    }
                }
            }

            return result;
        }

        #endregion

        #region Statistics

        private static JoinStats JStat(IReadOnlyList<Sample> subset)
        {
            List<Sample> resolved = subset.Where(s => s.Resolved).ToList();
            double averageRr = resolved.Count == 0
                ? 0d
                : resolved.Average(s =>
                    Math.Abs(s.Tp - s.Entry) / Math.Max(1e-12d, s.Risk));

            return new JoinStats(
                subset.Count,
                resolved.Count,
                subset.Count == 0 ? 0d : Share(subset, s => s.Worked236),
                resolved.Count == 0 ? 0d : Share(resolved, s => s.Profitable),
                resolved.Count == 0 ? 0d : resolved.Average(s => s.NetR),
                averageRr);
        }

        private static List<SweepRow> BuildSearchRows(
            IReadOnlyList<Sample> train,
            IReadOnlyList<Sample> test,
            IReadOnlyDictionary<Sample, List<HarmMatch>> harmony,
            IReadOnlyList<HarmSearchConfig> searchGrid,
            ConfluenceConfig confluence)
        {
            var rows = new List<SweepRow>();
            foreach (HarmSearchConfig search in searchGrid)
            {
                List<Sample> trainFiltered = FilterByHarmony(train, harmony, search, confluence);
                if (trainFiltered.Count < MIN_TRAIN_SIGNALS)
                    continue;

                List<Sample> testFiltered = FilterByHarmony(test, harmony, search, confluence);
                rows.Add(new SweepRow(search, confluence, JStat(trainFiltered), JStat(testFiltered)));
            }

            return rows;
        }

        private static List<SweepRow> BuildConfluenceRows(
            IReadOnlyList<Sample> train,
            IReadOnlyList<Sample> test,
            IReadOnlyDictionary<Sample, List<HarmMatch>> harmony,
            HarmSearchConfig search,
            IReadOnlyList<ConfluenceConfig> confluenceGrid)
        {
            var rows = new List<SweepRow>();
            foreach (ConfluenceConfig confluence in confluenceGrid)
            {
                List<Sample> trainFiltered = FilterByHarmony(train, harmony, search, confluence);
                if (trainFiltered.Count < MIN_TRAIN_SIGNALS)
                    continue;

                List<Sample> testFiltered = FilterByHarmony(test, harmony, search, confluence);
                rows.Add(new SweepRow(search, confluence, JStat(trainFiltered), JStat(testFiltered)));
            }

            return rows;
        }

        private static SweepRow? SelectBest(IReadOnlyList<SweepRow> rows)
        {
            return rows
                .OrderByDescending(r => r.Train.AvgNetR)
                .ThenBy(r => r.Search.Name, StringComparer.Ordinal)
                .ThenBy(r => r.Join.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        #endregion

        #region Report

        private static string BuildHarmonicReport(
            IReadOnlyList<FileResult> results, int fileCount)
        {
            var exact = results.SelectMany(r => r.Exact).ToList();
            var permissive = results.SelectMany(r => r.Permissive).ToList();

            var harmony = new Dictionary<Sample, List<HarmMatch>>();
            foreach (FileResult result in results)
            {
                foreach (KeyValuePair<Sample, List<HarmMatch>> pair in result.Harmony)
                    harmony[pair.Key] = pair.Value;
            }

            List<Sample> exactTrain = exact.Where(s => s.EntryTime.Year <= 2022).ToList();
            List<Sample> exactTest = exact.Where(s => s.EntryTime.Year >= 2023).ToList();
            List<Sample> permissiveTrain = permissive.Where(s => s.EntryTime.Year <= 2022).ToList();
            List<Sample> permissiveTest = permissive.Where(s => s.EntryTime.Year >= 2023).ToList();

            JoinStats exactBaseTrain = JStat(exactTrain);
            JoinStats exactBaseTest = JStat(exactTest);
            JoinStats permissiveBaseTrain = JStat(permissiveTrain);
            JoinStats permissiveBaseTest = JStat(permissiveTest);

            var searchGrid = BuildSearchGrid();
            List<SweepRow> searchRows = BuildSearchRows(
                exactTrain, exactTest, harmony, searchGrid, DEFAULT_CONFLUENCE);
            SweepRow? bestSearch = SelectBest(searchRows);

            HarmSearchConfig finalSearch = bestSearch?.Search ?? searchGrid[0];
            ConfluenceConfig finalJoin = DEFAULT_CONFLUENCE;

            List<SweepRow> joinRows = BuildConfluenceRows(
                exactTrain, exactTest, harmony, finalSearch, BuildConfluenceGrid());
            SweepRow? bestJoin = SelectBest(joinRows);
            if (bestJoin != null)
                finalJoin = bestJoin.Join;

            List<Sample> finalTrain = FilterByHarmony(exactTrain, harmony, finalSearch, finalJoin);
            List<Sample> finalTest = FilterByHarmony(exactTest, harmony, finalSearch, finalJoin);
            JoinStats finalTrainStats = JStat(finalTrain);
            JoinStats finalTestStats = JStat(finalTest);

            var builder = new StringBuilder();
            builder.AppendLine("# Diagonal + harmonic confluence");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Files: {fileCount} (8 h1 + 4 m15 from the Phase 0 archive set). The diagonal is replayed exactly (W2/W1 >= 0.5, W4/W3 <= 0.382, 23.6% diagonal target); the harmonic finder is run once per file with the widest settings and every parameter is then applied post-hoc. A harmonic pattern is usable only when its point D was confirmed on or before the diagonal signal bar."));
            builder.AppendLine();
            builder.AppendLine("Train = entries in 2017–2022, test = 2023–2026. Net R includes a 2 bp round-trip cost. `Δ netR` compares the test cell with the exact-corridor baseline.");
            builder.AppendLine();

            builder.AppendLine("## Baseline");
            builder.AppendLine();
            builder.AppendLine("| Set | Split | Signals | worked236 | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            AppendBaselineRow(builder, "exact corridor", "train", exactBaseTrain);
            AppendBaselineRow(builder, "exact corridor", "test", exactBaseTest);
            AppendBaselineRow(builder, "permissive", "train", permissiveBaseTrain);
            AppendBaselineRow(builder, "permissive", "test", permissiveBaseTest);
            builder.AppendLine();

            builder.AppendLine("## Harmonic search baseline");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Wide search: BarsDepth {HARMONIC_BARS_DEPTH}, pivots {HARMONIC_MIN_PIVOT}–{HARMONIC_MAX_PIVOT}, Fib error {HARMONIC_FIB_ERROR:F0} %, symmetry off, D confirmation {HARMONIC_D_CONFIRMATION}."));
            builder.AppendLine();
            builder.AppendLine("| File | Bars | Patterns | Gartley | Bat | Butterfly | Crab | Shark | Cypher |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (FileResult result in results.OrderBy(r => r.File, StringComparer.Ordinal))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{result.File}` | {result.Bars} | {result.PatternCount} | {TypeCount(result, HarmonicPatternType.GARTLEY)} | {TypeCount(result, HarmonicPatternType.BAT)} | {TypeCount(result, HarmonicPatternType.BUTTERFLY)} | {TypeCount(result, HarmonicPatternType.CRAB)} | {TypeCount(result, HarmonicPatternType.SHARK)} | {TypeCount(result, HarmonicPatternType.CYPHER)} |"));
            }

            builder.AppendLine();
            builder.AppendLine("| Model | Patterns | Share |");
            builder.AppendLine("|---|---:|---:|");
            var allPatterns = results.SelectMany(r => r.PatternByType)
                .GroupBy(p => p.Key)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
            int totalPatterns = allPatterns.Values.Sum();
            foreach (HarmonicPatternType type in ALL_TYPES)
            {
                allPatterns.TryGetValue(type, out int count);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {type} | {count} | {(totalPatterns == 0 ? 0d : (double)count / totalPatterns):P1} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Sweep 1 — harmonic search settings");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Fixed confluence: **{DEFAULT_CONFLUENCE.Name}**. Only cells keeping at least {MIN_TRAIN_SIGNALS} train signals are ranked. Top 15 by train net R, with the held-out test result next to them."));
            builder.AppendLine();
            builder.AppendLine("| # | Search | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R | Δ netR |");
            builder.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            int rank = 1;
            foreach (SweepRow row in searchRows
                         .OrderByDescending(r => r.Train.AvgNetR)
                         .ThenBy(r => r.Search.Name, StringComparer.Ordinal)
                         .Take(15))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {rank++} | {row.Search.Name} | {row.Train.N} | {row.Train.Profitable:P1} | {row.Train.AvgNetR:F3} | {row.Test.N} | {row.Test.Profitable:P1} | {row.Test.AvgNetR:F3} | {row.Test.AvgRr:F2} | {row.Test.AvgNetR - exactBaseTest.AvgNetR:+0.000;-0.000} |"));
            }

            builder.AppendLine();
            builder.AppendLine("Selected on train:");
            builder.AppendLine();
            builder.AppendLine("| Search | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R | Δ netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            if (bestSearch == null)
            {
                builder.AppendLine("| — | — | — | — | — | — | — | — | — |");
            }
            else
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {bestSearch.Search.Name} | {bestSearch.Train.N} | {bestSearch.Train.Profitable:P1} | {bestSearch.Train.AvgNetR:F3} | {bestSearch.Test.N} | {bestSearch.Test.Profitable:P1} | {bestSearch.Test.AvgNetR:F3} | {bestSearch.Test.AvgRr:F2} | {bestSearch.Test.AvgNetR - exactBaseTest.AvgNetR:+0.000;-0.000} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Sweep 2 — confluence geometry");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Search fixed to the train-selected setting (**{finalSearch.Name}**). `lag` is the D-confirmation delay: 0 means the harmonic point D is known at the close of the D bar itself, which is the same close the diagonal enters at; a larger value requires the D to survive that many trailing bars. `±bars` is the allowed distance between the harmonic D and the diagonal wave-5 extreme, and the last field is the allowed price distance as a share of the diagonal height."));
            builder.AppendLine();
            builder.AppendLine("| # | Confluence | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R | Δ netR |");
            builder.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            rank = 1;
            foreach (SweepRow row in joinRows
                         .OrderByDescending(r => r.Train.AvgNetR)
                         .ThenBy(r => r.Join.Name, StringComparer.Ordinal)
                         .Take(15))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {rank++} | {row.Join.Name} | {row.Train.N} | {row.Train.Profitable:P1} | {row.Train.AvgNetR:F3} | {row.Test.N} | {row.Test.Profitable:P1} | {row.Test.AvgNetR:F3} | {row.Test.AvgRr:F2} | {row.Test.AvgNetR - exactBaseTest.AvgNetR:+0.000;-0.000} |"));
            }

            builder.AppendLine();
            builder.AppendLine("Selected on train:");
            builder.AppendLine();
            builder.AppendLine("| Search | Confluence | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R | Δ netR |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(FormattableString.Invariant(
                $"| {finalSearch.Name} | {finalJoin.Name} | {finalTrainStats.N} | {finalTrainStats.Profitable:P1} | {finalTrainStats.AvgNetR:F3} | {finalTestStats.N} | {finalTestStats.Profitable:P1} | {finalTestStats.AvgNetR:F3} | {finalTestStats.AvgRr:F2} | {finalTestStats.AvgNetR - exactBaseTest.AvgNetR:+0.000;-0.000} |"));

            builder.AppendLine();
            builder.AppendLine("## Sweep 3 — model type");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Search and confluence fixed to the selected setting. `all` is the selected cell itself."));
            builder.AppendLine();
            builder.AppendLine("| Model | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            AppendTypeRow(builder, "all", exactTrain, exactTest, harmony, finalSearch, finalJoin, null);
            foreach (HarmonicPatternType type in ALL_TYPES)
                AppendTypeRow(builder, type.ToString(), exactTrain, exactTest, harmony, finalSearch, finalJoin, type);

            builder.AppendLine();
            builder.AppendLine("## Sweep 4 — pattern length");
            builder.AppendLine();
            builder.AppendLine("Minimum X-to-D duration of the harmonic pattern, in bars.");
            builder.AppendLine();
            builder.AppendLine("| Min length | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (int minLength in new[] { 0, 10, 20, 50, 100 })
            {
                HarmSearchConfig config = finalSearch with { MinLengthBars = minLength };
                JoinStats train = JStat(FilterByHarmony(exactTrain, harmony, config, finalJoin));
                JoinStats test = JStat(FilterByHarmony(exactTest, harmony, config, finalJoin));
                builder.AppendLine(FormattableString.Invariant(
                    $"| {minLength} | {train.N} | {train.Profitable:P1} | {train.AvgNetR:F3} | {test.N} | {test.Profitable:P1} | {test.AvgNetR:F3} | {test.AvgRr:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Robustness — permissive diagonal baseline");
            builder.AppendLine();
            builder.AppendLine("The same selected harmonic filter applied to the permissive Phase 0 diagonal set (R:R 1.5, every beauty gate off). A filter that only works on one diagonal configuration is not a structural edge.");
            builder.AppendLine();
            builder.AppendLine("| Set | Train N | Train profit | Train netR | Test N | Test profit | Test netR | Test R:R |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            builder.AppendLine(FormattableString.Invariant(
                $"| permissive baseline | {permissiveBaseTrain.N} | {permissiveBaseTrain.Profitable:P1} | {permissiveBaseTrain.AvgNetR:F3} | {permissiveBaseTest.N} | {permissiveBaseTest.Profitable:P1} | {permissiveBaseTest.AvgNetR:F3} | {permissiveBaseTest.AvgRr:F2} |"));
            JoinStats permissiveTrainFiltered = JStat(
                FilterByHarmony(permissiveTrain, harmony, finalSearch, finalJoin));
            JoinStats permissiveTestFiltered = JStat(
                FilterByHarmony(permissiveTest, harmony, finalSearch, finalJoin));
            builder.AppendLine(FormattableString.Invariant(
                $"| permissive + harmonic | {permissiveTrainFiltered.N} | {permissiveTrainFiltered.Profitable:P1} | {permissiveTrainFiltered.AvgNetR:F3} | {permissiveTestFiltered.N} | {permissiveTestFiltered.Profitable:P1} | {permissiveTestFiltered.AvgNetR:F3} | {permissiveTestFiltered.AvgRr:F2} |"));

            builder.AppendLine();
            builder.AppendLine("## Execution check (post-hoc)");
            builder.AppendLine();
            builder.AppendLine("The finder's own 23.6% target versus fixed risk multiples on the exact-corridor baseline and on the selected filtered subset. `SimulatePhase1` is a post-hoc simulation on the stored entry/stop, not a finder replay.");
            builder.AppendLine();
            builder.AppendLine("| Set | Target | Signals | resolved | profitable | avg netR | avg R:R |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            AppendExecutionRows(builder, "exact baseline (test)", exactTest);
            AppendExecutionRows(builder, "exact + harmonic (test)", finalTest);

            builder.AppendLine();
            builder.AppendLine("## Stability of the selected cell (test split)");
            builder.AppendLine();
            builder.AppendLine("| Group | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in finalTest
                         .GroupBy(s => s.TimeFrame)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendStabilityRow(builder, $"timeframe {group.Key}", group.ToList());
            }

            foreach (IGrouping<string, Sample> group in finalTest
                         .GroupBy(s => SessionOf(s.EntryTime))
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendStabilityRow(builder, $"session {group.Key}", group.ToList());
            }

            foreach (IGrouping<int, Sample> group in finalTest
                         .GroupBy(s => s.EntryTime.Year)
                         .OrderBy(g => g.Key))
            {
                AppendStabilityRow(builder, $"year {group.Key}", group.ToList());
            }

            builder.AppendLine();
            builder.AppendLine("### By pair (test split)");
            builder.AppendLine();
            builder.AppendLine("| File | Signals | worked236 | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (IGrouping<string, Sample> group in finalTest
                         .GroupBy(s => s.File)
                         .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendStabilityRow(builder, $"`{group.Key}`", group.ToList());
            }

            builder.AppendLine();
            builder.AppendLine("## What this run says");
            builder.AppendLine();
            AppendConclusions(
                builder,
                exactBaseTest,
                finalTestStats,
                exact.Count,
                finalTrain.Count + finalTest.Count,
                permissiveBaseTest,
                permissiveTestFiltered,
                bestSearch,
                finalJoin);

            return builder.ToString();
        }

        private static void AppendBaselineRow(
            StringBuilder builder, string set, string split, JoinStats stats)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"| {set} | {split} | {stats.N} | {stats.Worked236:P1} | {stats.Resolved} | {stats.Profitable:P1} | {stats.AvgNetR:F3} | {stats.AvgRr:F2} |"));
        }

        private static int TypeCount(FileResult result, HarmonicPatternType type)
        {
            return result.PatternByType.TryGetValue(type, out int count) ? count : 0;
        }

        private static void AppendTypeRow(
            StringBuilder builder,
            string name,
            IReadOnlyList<Sample> train,
            IReadOnlyList<Sample> test,
            IReadOnlyDictionary<Sample, List<HarmMatch>> harmony,
            HarmSearchConfig search,
            ConfluenceConfig confluence,
            HarmonicPatternType? type)
        {
            HarmSearchConfig config = search with { Type = type };
            JoinStats trainStats = JStat(FilterByHarmony(train, harmony, config, confluence));
            JoinStats testStats = JStat(FilterByHarmony(test, harmony, config, confluence));
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {trainStats.N} | {trainStats.Profitable:P1} | {trainStats.AvgNetR:F3} | {testStats.N} | {testStats.Profitable:P1} | {testStats.AvgNetR:F3} | {testStats.AvgRr:F2} |"));
        }

        private static void AppendExecutionRows(
            StringBuilder builder, string set, IReadOnlyList<Sample> samples)
        {
            (string Name, Phase1ExecKind Kind, double Param)[] variants =
            {
                ("23.6% diagonal retrace", Phase1ExecKind.DiagonalRetrace, 0.236d),
                ("fixed R:R 1.0", Phase1ExecKind.FixedRr, 1.0d),
                ("fixed R:R 1.5", Phase1ExecKind.FixedRr, 1.5d),
                ("fixed R:R 2.0", Phase1ExecKind.FixedRr, 2.0d)
            };

            foreach ((string name, Phase1ExecKind kind, double param) in variants)
            {
                List<Phase1ExecResult> results = samples
                    .Select(s => SimulatePhase1(s, kind, param))
                    .Where(r => !r.Skipped && r.Resolved)
                    .ToList();

                double profitable = results.Count == 0
                    ? 0d
                    : (double)results.Count(r => r.Profitable) / results.Count;
                double netR = results.Count == 0 ? 0d : results.Average(r => r.NetR);
                double rr = results.Count == 0 ? 0d : results.Average(r => r.Rr);
                builder.AppendLine(FormattableString.Invariant(
                    $"| {set} | {name} | {samples.Count} | {results.Count} | {profitable:P1} | {netR:F3} | {rr:F2} |"));
            }
        }

        private static void AppendStabilityRow(
            StringBuilder builder, string name, IReadOnlyList<Sample> samples)
        {
            JoinStats stats = JStat(samples);
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {stats.N} | {stats.Worked236:P1} | {stats.Profitable:P1} | {stats.AvgNetR:F3} |"));
        }

        private static void AppendConclusions(
            StringBuilder builder,
            JoinStats exactBaseline,
            JoinStats filtered,
            int exactSignals,
            int filteredSignals,
            JoinStats permissiveBaseline,
            JoinStats permissiveFiltered,
            SweepRow? bestSearch,
            ConfluenceConfig confluence)
        {
            double delta = filtered.AvgNetR - exactBaseline.AvgNetR;
            double coverage = exactSignals == 0 ? 0d : (double)filteredSignals / exactSignals;

            builder.AppendLine(FormattableString.Invariant(
                $"- The selected cell keeps **{filteredSignals} of {exactSignals}** exact-corridor signals ({coverage:P1} coverage). On the test split it produces {filtered.N} signals, {filtered.Profitable:P1} profitable, **{filtered.AvgNetR:F3}R** per trade against the baseline's {exactBaseline.AvgNetR:F3}R (Δ {delta:+0.000;-0.000})."));
            builder.AppendLine(FormattableString.Invariant(
                $"- The filter is **{bestSearch?.Search.Name ?? "n/a"}** with **{confluence.Name}**."));
            builder.AppendLine(FormattableString.Invariant(
                $"- On the permissive diagonal set the same filter gives {permissiveFiltered.N} test signals at {permissiveFiltered.AvgNetR:F3}R versus the baseline's {permissiveBaseline.AvgNetR:F3}R (Δ {permissiveFiltered.AvgNetR - permissiveBaseline.AvgNetR:+0.000;-0.000})."));

            if (filtered.N < MIN_TRAIN_SIGNALS)
            {
                builder.AppendLine("- The selected cell is too thin on the test split to support any conclusion. Treat the sweep as inconclusive and widen the archive before shipping anything.");
            }
            else if (delta > 0.05d && filtered.AvgNetR > 0d)
            {
                builder.AppendLine("- The confluence filter is positive out of sample and materially above the diagonal baseline. This is the first configuration worth a walk-forward and a production parameterisation; it is not yet proof, because the search settings and the join geometry were both chosen on train.");
            }
            else if (delta > 0.02d)
            {
                builder.AppendLine("- The confluence filter improves the diagonal result but does not make it positive. The direction is right, the edge is not yet large enough to trade after costs.");
            }
            else
            {
                builder.AppendLine("- Requiring a harmonic pattern at the diagonal's wave-5 extreme does **not** improve the diagonal edge out of sample. On this archive the two conditions are not additive: the harmonic figure marks the same reversal the diagonal already trades, and the extra filter only removes trades.");
            }

            builder.AppendLine("- The sweep is a two-stage in-sample selection (search settings first, then confluence geometry). The test column is a held-out check of that procedure, not of a single pre-registered rule. Any candidate must still survive a fresh-pair validation like the `minRR 0.5` check in `reports/diagonal_minrr05_validation.md`.");
            builder.AppendLine("- The harmonic finder is run once per file with D confirmation 0, so a pattern formed on the signal bar itself is visible; a lag of L bars then requires the D to survive L trailing bars, which is exact for a given XABCD. The pattern set is the D=0 superset, so a true lag-L run can only find fewer patterns, never more.");
            builder.AppendLine("- Execution is unchanged: the diagonal's own entry, stop and target are traded. The harmonic pattern is used only as a gate, so the result isolates the value of the second condition rather than mixing it with a different trade mechanic.");
        }

        #endregion

        #region Types

        private sealed record DiagonalRunConfig(
            string Name,
            double TakeProfitRatio,
            DiagonalTakeProfitMode TakeProfitMode,
            double MinWave2Retrace,
            double MaxWave4RetraceW3,
            bool RequireInsideWedge,
            double MaxSpillAreaRatio,
            double MinWave4Wave2Level,
            double MinWave4RetraceW3,
            double MinRiskRewardRatio,
            DiagonalTriggerMode TriggerMode = DiagonalTriggerMode.WAVE3_BREAK);

        private sealed record HarmPattern(
            HarmonicPatternType Type,
            bool IsBull,
            int DBar,
            double DValue,
            int PivotPeriod,
            int LengthBars,
            double MaxLegError,
            double MaxLegRatio,
            double MinLegRatio,
            double Score,
            double PatternHeight,
            bool[] Survives);

        private sealed record HarmMatch(HarmPattern Pattern, double PriceDistance);

        private sealed record HarmSearchConfig(
            string Name,
            double MaxLegError,
            double MaxLegRatio,
            double MinLegRatio,
            int MinPivot,
            int MaxPivot,
            double MinScore,
            int MinLengthBars,
            HarmonicPatternType? Type);

        private sealed record ConfluenceConfig(
            string Name,
            int ConfirmationBars,
            int MaxBarDistance,
            double MaxPriceDistance);

        private sealed record JoinStats(
            int N,
            int Resolved,
            double Worked236,
            double Profitable,
            double AvgNetR,
            double AvgRr);

        private sealed record SweepRow(
            HarmSearchConfig Search,
            ConfluenceConfig Join,
            JoinStats Train,
            JoinStats Test);

        private sealed class FileResult
        {
            public string File { get; set; } = string.Empty;
            public int Bars { get; set; }
            public List<Sample> Exact { get; set; } = new();
            public List<Sample> Permissive { get; set; } = new();
            public Dictionary<Sample, List<HarmMatch>> Harmony { get; set; } = new();
            public int PatternCount { get; set; }
            public Dictionary<HarmonicPatternType, int> PatternByType { get; set; } = new();
        }

        #endregion
    }
}
