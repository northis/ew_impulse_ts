using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Phase 0 research harness for the geometric "wedge-likeness" of a contracting
    /// diagonal (DIAGONAL.md §9.18). It deliberately runs the finder with all beauty gates
    /// off, records every emitted setup together with its geometry, and walks the price
    /// forward to label the setup as "worked" (the expected correction arrived before the
    /// theoretical invalidation) or "false" (it did not).
    /// <para>
    /// The harness changes no trading behaviour. It writes
    /// <c>reports/diagonal_geometry.md</c> and <c>reports/diagonal_geometry_samples.csv</c>.
    /// Run manually:
    /// <c>dotnet test --filter "FullyQualifiedName~DiagonalGeometryResearch"</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness - run manually to (re)generate reports/diagonal_geometry.md")]
    [Category("Research")]
    public partial class DiagonalGeometryResearchTests
    {
        /// <summary>Round-trip cost as a fraction of price (about two pips on EURUSD).</summary>
        private const double COST_RATE = 0.0002d;

        /// <summary>The correction targets, as a share of the whole diagonal's height.</summary>
        private const double TARGET_236 = 0.236d;
        private const double TARGET_382 = 0.382d;
        private const double TARGET_50 = 0.5d;

        /// <summary>An outside excursion counts as sustained above this share of the corridor.</summary>
        private const double OUTSIDE_SHARE = 0.25d;

        private static readonly string[] H1_FILES =
        {
            "AUDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "GBPUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "USDJPY_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "AUDJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "EURJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "XAUUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv"
        };

        private static readonly string[] M15_FILES =
        {
            "AUDUSD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "EURUSD_m15_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "GBPJPY_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "USDJPY_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv"
        };

        [Test]
        public void DiagonalGeometryResearch_WriteReport()
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

            var samples = new List<Sample>();
            var fileStats = new List<FileStat>();
            long totalBars = 0;
            var sync = new object();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                file =>
                {
                    FileRun run = RunFile(Path.Combine(dataDir, file), file);
                    lock (sync)
                    {
                        samples.AddRange(run.Samples);
                        fileStats.Add(run.Stat);
                        totalBars += run.Stat.Bars;
                    }

                    TestContext.Out.WriteLine(
                        $"{file}: bars={run.Stat.Bars}, signals={run.Stat.Signals}, " +
                        $"worked236={run.Stat.Worked236}");
                });

            Assert.That(samples, Is.Not.Empty,
                "The permissive baseline produced no diagonal setups.");

            string repoRoot = Directory.GetParent(dataDir)!.FullName;
            string reportDir = Path.Combine(repoRoot, "reports");
            Directory.CreateDirectory(reportDir);

            string reportPath = Path.Combine(reportDir, "diagonal_geometry.md");
            File.WriteAllText(reportPath, BuildReport(samples, fileStats, totalBars));
            TestContext.Out.WriteLine($"Wrote {reportPath}");

            string csvPath = Path.Combine(reportDir, "diagonal_geometry_samples.csv");
            File.WriteAllText(csvPath, BuildCsv(samples));
            TestContext.Out.WriteLine($"Wrote {csvPath}");
        }

        /// <summary>
        /// Replays one file with the permissive geometry baseline: only the structural
        /// Elliott rules and the duration sanity remain, every beauty gate is off.
        /// </summary>
        private static FileRun RunFile(string path, string fileName)
        {
            ITimeFrame timeFrame = fileName.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(path);

            var finder = new DiagonalSetupFinder(
                provider,
                provider.BarSymbol,
                new EWParams(0, 0.1, 10),
                takeProfitRatio: 1.5,
                requireWave5Ratio: false,
                requireWave4Ratio: false,
                requireInitialDiagonal: false,
                takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                minConvergence: 0,
                maxConvergence: 0,
                requireInsideWedge: false,
                maxSpillAreaRatio: 1.0,
                minWave3Penetration: 0.03,
                maxWaveDurationRatio: 8.0,
                minWave4Wave2Level: 0,
                minWave4RetraceW3: 0,
                requireWave4Shorter: true,
                requireWave2Shorter: false,
                minWave2Retrace: 0,
                maxWave2Retrace: 0,
                maxWave5SpillRatio: 0);

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

            string symbol = fileName[..fileName.IndexOf('_')];
            string tf = fileName.Contains("_m15_") ? "m15" : "h1";
            var samples = new List<Sample>(records.Count);

            foreach (SignalRecord record in records)
            {
                Sample? sample = BuildSample(provider, finder, record, fileName, symbol, tf);
                if (sample != null)
                    samples.Add(sample);
            }

            var stat = new FileStat(
                fileName,
                provider.Count,
                records.Count,
                samples.Count(s => s.Worked236),
                samples.Count(s => s.Worked382),
                samples.Count(s => s.Stopped),
                samples.Count(s => s.Censored));

            return new FileRun(samples, stat);
        }

        /// <summary>
        /// Computes the geometry of one emitted setup and labels it with the forward walk.
        /// </summary>
        private static Sample? BuildSample(
            TestBarsProvider provider,
            DiagonalSetupFinder finder,
            SignalRecord record,
            string fileName,
            string symbol,
            string timeFrame)
        {
            ElliottWaveSignalEventArgs args = record.Args;
            BarPoint[] p = args.WavePoints;
            if (p.Length < 6)
                return null;

            // The trade is counter to the diagonal: a bullish diagonal is a SELL.
            bool isUpDiagonal = args.TakeProfit.Value < args.StopLoss.Value;
            int sgn = isUpDiagonal ? 1 : -1;

            double w1 = Math.Abs(p[1].Value - p[0].Value);
            double w2 = Math.Abs(p[2].Value - p[1].Value);
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double w4 = Math.Abs(p[4].Value - p[3].Value);
            double w5 = Math.Abs(p[5].Value - p[4].Value);

            if (w1 <= 0 || w2 <= 0 || w3 <= 0 || w4 <= 0 || w5 <= 0)
                return null;

            double entry = args.Level.Value;
            double risk = Math.Abs(entry - args.StopLoss.Value);
            if (risk <= 0)
                return null;

            double c = ConvergenceRatio(p[1], p[2], p[3], p[4], sgn);
            double a234 = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[1].BarIndex, p[4].BarIndex, finder.PrintFilter);
            double a2 = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[1].BarIndex, p[2].BarIndex, finder.PrintFilter);
            double a3 = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[2].BarIndex, p[3].BarIndex, finder.PrintFilter);
            double a4 = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[3].BarIndex, p[4].BarIndex, finder.PrintFilter);
            double a5Early = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[4].BarIndex, args.Level.BarIndex, finder.PrintFilter);

            int horizon = Math.Max(20, p[4].BarIndex - p[0].BarIndex);
            double invalidation = isUpDiagonal
                ? p[4].Value + w3
                : p[4].Value - w3;

            WalkResult walk = Walk(
                provider,
                args.Level.BarIndex,
                isUpDiagonal,
                p[0].Value,
                invalidation,
                entry,
                risk,
                horizon,
                p[5].Value,
                p[5].BarIndex);

            double a5Full = SpillAreaRatio(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[4].BarIndex, Math.Max(p[4].BarIndex, walk.FinalExtremeBar),
                finder.PrintFilter);

            int maxOutsideRun = MaxOutsideRun(
                provider, p[1], p[2], p[3], p[4], sgn,
                p[1].BarIndex, args.Level.BarIndex, finder.PrintFilter);

            // Trigger diagnostics (DIAGONAL.md §9.24): where the shipped V(3) break happened,
            // how far the 1-3 generatrix was from it and how the actual entry compares with
            // both. For the baseline these collapse to zero delay and zero delta.
            int v3BreakBar = FirstV3Break(
                provider, p[3].Value, p[4].BarIndex, args.Level.BarIndex, isUpDiagonal,
                finder.PrintFilter);
            double v3BreakClose = v3BreakBar >= 0
                ? provider.GetClosePrice(v3BreakBar)
                : entry;
            double line13AtSignal = p[1].Value +
                (p[3].Value - p[1].Value) * (args.Level.BarIndex - p[1].BarIndex) /
                Math.Max(1, p[3].BarIndex - p[1].BarIndex);
            double entryDeltaR = sgn * (entry - v3BreakClose) / risk;
            double entryDeltaW3 = sgn * (entry - v3BreakClose) / w3;
            double lineDistanceW3 = sgn * (line13AtSignal - p[3].Value) / w3;
            double entryVsLineR = sgn * (entry - line13AtSignal) / risk;

            double grossR = 0;
            double costR = 0;
            if (record.Outcome is "TP" or "SL")
            {
                grossR = isUpDiagonal
                    ? (entry - record.ExitPrice) / risk
                    : (record.ExitPrice - entry) / risk;
                costR = entry * COST_RATE / risk;
            }

            return new Sample
            {
                File = fileName,
                Symbol = symbol,
                TimeFrame = timeFrame,
                Provider = provider,
                EntryTime = args.Level.OpenTime,
                SignalBar = args.Level.BarIndex,
                IsUpDiagonal = isUpDiagonal,
                Entry = entry,
                Risk = risk,
                Sl = args.StopLoss.Value,
                Tp = args.TakeProfit.Value,
                P0Bar = p[0].BarIndex,
                P4Bar = p[4].BarIndex,
                P5Bar = p[5].BarIndex,
                MedianRangeW1 = MedianRange(provider, p[0].BarIndex, p[1].BarIndex),
                MedianRangeW3 = MedianRange(provider, p[2].BarIndex, p[3].BarIndex),
                MedianRangeW5 = MedianRange(provider, p[4].BarIndex, args.Level.BarIndex),
                V0 = p[0].Value,
                V3 = p[3].Value,
                V4 = p[4].Value,
                V5 = p[5].Value,
                W1 = w1,
                W2 = w2,
                W3 = w3,
                W4 = w4,
                W5 = w5,
                C = c,
                ApexRatio = c > 1e-9 ? 1d / c : double.NaN,
                A234 = a234,
                A2 = a2,
                A3 = a3,
                A4 = a4,
                A5Early = a5Early,
                A5Full = a5Full,
                AMax = Math.Max(a2, Math.Max(a3, a4)),
                MaxOutsideRun = maxOutsideRun,
                V3BreakBar = v3BreakBar,
                Line13AtSignal = line13AtSignal,
                EntryDeltaR = entryDeltaR,
                EntryDeltaW3 = entryDeltaW3,
                LineDistanceW3 = lineDistanceW3,
                EntryVsLineR = entryVsLineR,
                W2Retr = w2 / w1,
                W3Pen = sgn * (p[3].Value - p[1].Value) / w1,
                W4W3 = w4 / w3,
                W4Level = sgn * (p[1].Value - p[4].Value) / w2,
                W4W2 = w4 / w2,
                W3W1 = w3 / w1,
                W5W3 = w5 / w3,
                OverlapDepth = sgn * (p[1].Value - p[4].Value) / w1,
                BarsW1 = p[1].BarIndex - p[0].BarIndex,
                BarsW2 = p[2].BarIndex - p[1].BarIndex,
                BarsW3 = p[3].BarIndex - p[2].BarIndex,
                BarsW4 = p[4].BarIndex - p[3].BarIndex,
                BarsW5 = args.Level.BarIndex - p[4].BarIndex,
                Worked236 = walk.MaxRetraceEver >= TARGET_236,
                Worked382 = walk.MaxRetraceEver >= TARGET_382,
                Worked50 = walk.MaxRetraceEver >= TARGET_50,
                Final236 = walk.MaxRetraceFinal >= TARGET_236,
                Final382 = walk.MaxRetraceFinal >= TARGET_382,
                Stopped = walk.Stopped,
                Censored = walk.Censored,
                MfeDiag = walk.MaxRetraceEver,
                MfeR = walk.MfeR,
                MaeR = walk.MaeR,
                GrossR = grossR,
                CostR = costR,
                NetR = grossR - costR,
                Resolved = record.Outcome is "TP" or "SL",
                Profitable = record.Outcome is "TP" or "SL" && grossR - costR > 0,
                BarsToMaxRetrace = walk.BarsToMaxRetrace,
                Horizon = horizon,
                FinalExtreme = walk.FinalExtreme,
                FinalExtremeBar = walk.FinalExtremeBar,
                Outcome = record.Outcome,
                ExitPrice = record.ExitPrice,
                OutcomeBar = record.OutcomeBar,
                FinderDiag = new Dictionary<string, int>(finder.Diag)
            };
        }

        /// <summary>
        /// Walks the price forward from the signal bar. The running extreme is the best
        /// price seen so far in the diagonal's direction; a correction is measured from it,
        /// so a wave 5 that continues after the entry moves the target along with it.
        /// A bar that touches both the invalidation and a target counts as the invalidation.
        /// </summary>
        private static WalkResult Walk(
            TestBarsProvider provider,
            int entryBar,
            bool isUpDiagonal,
            double v0,
            double invalidation,
            double entry,
            double risk,
            int horizon,
            double initialExtreme,
            int initialExtremeBar)
        {
            double runningExtreme = initialExtreme;
            int finalExtremeBar = initialExtremeBar;
            double finalExtreme = initialExtreme;
            double maxRetraceEver = 0;
            double maxRetraceFinal = 0;
            double mfeR = 0;
            double maeR = 0;
            int barsToMaxRetrace = 0;
            bool stopped = false;

            int end = Math.Min(provider.Count - 1, entryBar + horizon);
            for (int i = entryBar + 1; i <= end; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                // Conservative: if the bar invalidates the model and reaches a target,
                // the model is considered dead.
                bool invalidated = isUpDiagonal
                    ? high >= invalidation
                    : low <= invalidation;
                if (invalidated)
                {
                    stopped = true;
                    maeR = Math.Max(maeR, 1d);
                    break;
                }

                bool newExtreme = false;
                if (isUpDiagonal && high > runningExtreme)
                {
                    runningExtreme = high;
                    finalExtreme = high;
                    finalExtremeBar = i;
                    newExtreme = true;
                }
                else if (!isUpDiagonal && low < runningExtreme)
                {
                    runningExtreme = low;
                    finalExtreme = low;
                    finalExtremeBar = i;
                    newExtreme = true;
                }

                if (newExtreme)
                    maxRetraceFinal = 0;

                double height = Math.Abs(runningExtreme - v0);
                if (height > 1e-12)
                {
                    double retrace = isUpDiagonal
                        ? (runningExtreme - low) / height
                        : (high - runningExtreme) / height;
                    if (retrace > maxRetraceEver)
                    {
                        maxRetraceEver = retrace;
                        barsToMaxRetrace = i - entryBar;
                    }

                    if (retrace > maxRetraceFinal)
                        maxRetraceFinal = retrace;
                }

                double favorable = isUpDiagonal ? entry - low : high - entry;
                double adverse = isUpDiagonal ? high - entry : entry - low;
                mfeR = Math.Max(mfeR, favorable / risk);
                maeR = Math.Max(maeR, adverse / risk);
            }

            return new WalkResult(
                stopped,
                !stopped && maxRetraceEver < TARGET_236,
                maxRetraceEver,
                maxRetraceFinal,
                mfeR,
                maeR,
                barsToMaxRetrace,
                finalExtremeBar,
                finalExtreme);
        }

        /// <summary>
        /// The D-CONVERGE measure <c>w(t1)/w(t4) − 1</c>, exactly as the finder computes it.
        /// In a contracting wedge this equals <c>(t4−t1)/(t_apex−t4)</c>: the inverse apex
        /// distance in skeleton lengths.
        /// </summary>
        private static double ConvergenceRatio(
            BarPoint p1, BarPoint p2, BarPoint p3, BarPoint p4, int sgn)
        {
            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);
            double widthAt1 =
                sgn * (p1.Value - (p2.Value + floorSlope * (p1.BarIndex - p2.BarIndex)));
            double widthAt4 =
                sgn * ((p1.Value + ceilSlope * (p4.BarIndex - p1.BarIndex)) - p4.Value);
            return widthAt1 / Math.Max(1e-12, widthAt4) - 1;
        }

        /// <summary>
        /// The D-INSIDE spill area over an arbitrary bar span. The same formula as the
        /// finder, so the report and the production gate stay comparable.
        /// </summary>
        private static double SpillAreaRatio(
            TestBarsProvider provider,
            BarPoint p1,
            BarPoint p2,
            BarPoint p3,
            BarPoint p4,
            int sgn,
            int fromBar,
            int toBar,
            IsolatedPrintFilter printFilter)
        {
            if (toBar < fromBar)
                return 0;

            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);

            double spill = 0;
            double area = 0;
            for (int bar = fromBar; bar <= toBar; bar++)
            {
                if (printFilter.IsExcluded(bar))
                    continue;

                double ceiling = sgn * (p1.Value + ceilSlope * (bar - p1.BarIndex));
                double floor = sgn * (p2.Value + floorSlope * (bar - p2.BarIndex));
                double high = sgn * provider.GetHighPrice(bar);
                double low = sgn * provider.GetLowPrice(bar);
                double barMax = Math.Max(high, low);
                double barMin = Math.Min(high, low);

                spill += Math.Max(0, barMax - ceiling) + Math.Max(0, floor - barMin);
                area += Math.Max(0, ceiling - floor);
            }

            return spill / Math.Max(1e-12, area);
        }

        /// <summary>
        /// Longest run of bars that spend more than <see cref="OUTSIDE_SHARE"/> of the local
        /// corridor outside the trendlines. A single wick is not a run; a sustained breakout is.
        /// </summary>
        private static int MaxOutsideRun(
            TestBarsProvider provider,
            BarPoint p1,
            BarPoint p2,
            BarPoint p3,
            BarPoint p4,
            int sgn,
            int fromBar,
            int toBar,
            IsolatedPrintFilter printFilter)
        {
            if (toBar < fromBar)
                return 0;

            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);

            int run = 0;
            int maxRun = 0;
            for (int bar = fromBar; bar <= toBar; bar++)
            {
                if (printFilter.IsExcluded(bar))
                    continue;

                double ceiling = sgn * (p1.Value + ceilSlope * (bar - p1.BarIndex));
                double floor = sgn * (p2.Value + floorSlope * (bar - p2.BarIndex));
                double high = sgn * provider.GetHighPrice(bar);
                double low = sgn * provider.GetLowPrice(bar);
                double barMax = Math.Max(high, low);
                double barMin = Math.Min(high, low);
                double outside = Math.Max(0, barMax - ceiling) + Math.Max(0, floor - barMin);
                double width = Math.Max(0, ceiling - floor);
                double share = width > 1e-12
                    ? outside / width
                    : outside > 1e-12 ? 1d : 0d;

                if (share > OUTSIDE_SHARE)
                {
                    run++;
                    maxRun = Math.Max(maxRun, run);
                }
                else
                {
                    run = 0;
                }
            }

            return maxRun;
        }

        /// <summary>
        /// The first bar after wave 4 whose wick breaks the horizontal V(3) — the shipped
        /// trigger. It is the reference point for the 1-3 generatrix study (DIAGONAL.md
        /// §9.24): how many bars later the line fires and how the entry price compares.
        /// </summary>
        private static int FirstV3Break(
            TestBarsProvider provider, double v3, int fromBar, int toBar, bool isUpDiagonal,
            IsolatedPrintFilter? printFilter = null)
        {
            for (int bar = Math.Max(0, fromBar + 1);
                 bar <= toBar && bar < provider.Count;
                 bar++)
            {
                if (printFilter != null && printFilter.IsExcluded(bar))
                    continue;

                double wick = isUpDiagonal
                    ? provider.GetHighPrice(bar)
                    : provider.GetLowPrice(bar);
                if (isUpDiagonal ? wick > v3 : wick < v3)
                    return bar;
            }

            return -1;
        }

        /// <summary>
        /// Median bar range over a wave span. A range proxy for activity: the old archive has
        /// no volume, so this is the only "fuel" measure available there.
        /// </summary>
        private static double MedianRange(TestBarsProvider provider, int fromBar, int toBar)
        {
            if (toBar < fromBar)
                return 0;

            var ranges = new List<double>(toBar - fromBar + 1);
            for (int bar = fromBar; bar <= toBar; bar++)
            {
                ranges.Add(provider.GetHighPrice(bar) - provider.GetLowPrice(bar));
            }

            if (ranges.Count == 0)
                return 0;

            ranges.Sort();
            int index = (int)Math.Round(0.5 * (ranges.Count - 1), MidpointRounding.AwayFromZero);
            return ranges[Math.Clamp(index, 0, ranges.Count - 1)];
        }

        #region Report

        private static string BuildReport(
            IReadOnlyList<Sample> samples, IReadOnlyList<FileStat> fileStats, long totalBars)
        {
            var builder = new StringBuilder();
            List<Sample> resolved = samples.Where(s => s.Resolved).ToList();
            builder.AppendLine("# Diagonal geometry — Phase 0");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
            builder.AppendLine();
            builder.Append(FormattableString.Invariant(
                $"Archive files: {fileStats.Count}, bars: {totalBars}, signals: {samples.Count}. "));
            builder.AppendLine(
                "The finder runs with every beauty gate off (convergence cap, spill, W4 shape, " +
                "W2 retrace, optional duration ratios) and only the structural Elliott rules " +
                "plus the duration sanity remain. This is the unbiased sample for the geometry " +
                "study: a filter must be judged against it, not against the already-filtered " +
                "shipped sample.");
            builder.AppendLine();
            builder.Append(
                "Labels: a setup is `worked` when price retraces at least 23.6% / 38.2% / 50% " +
                "of the whole diagonal from its running extreme before the theoretical " +
                "invalidation `V(4) ± |W3|`, within `max(20, P4−P0)` bars. A bar that touches " +
                "both invalidation and target counts as invalidation. `final` labels measure " +
                "the retrace only after the last extreme, i.e. after the completed wave 5. ");
            builder.Append(FormattableString.Invariant(
                $"`netR` uses the actual TP/SL event and a round-trip cost of {COST_RATE:P2} "));
            builder.AppendLine("of price.");
            builder.AppendLine();

            builder.AppendLine("## Files");
            builder.AppendLine();
            builder.AppendLine("| File | Bars | Signals | worked236 | worked382 | stopped | censored |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (FileStat stat in fileStats.OrderBy(s => s.File, StringComparer.Ordinal))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{stat.File}` | {stat.Bars} | {stat.Signals} | {stat.Worked236} | {stat.Worked382} | {stat.Stopped} | {stat.Censored} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## Baseline");
            builder.AppendLine();
            builder.AppendLine(FormattableString.Invariant(
                $"| Metric | Value |"));
            builder.AppendLine("|---|---:|");
            builder.AppendLine(FormattableString.Invariant(
                $"| signals | {samples.Count} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| worked236 | {Share(samples, s => s.Worked236):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| worked382 | {Share(samples, s => s.Worked382):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| final236 | {Share(samples, s => s.Final236):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| stopped | {Share(samples, s => s.Stopped):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| censored | {Share(samples, s => s.Censored):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| resolved | {resolved.Count} ({Share(samples, s => s.Resolved):P1}) |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| avg netR, resolved | {resolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| profitable, resolved | {Share(resolved, s => s.Profitable):P1} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| median MFE, diagonal | {Quantile(samples.Select(s => s.MfeDiag).ToList(), 0.5):F3} |"));
            builder.AppendLine(FormattableString.Invariant(
                $"| median c | {Quantile(samples.Select(s => s.C).ToList(), 0.5):F3} |"));
            builder.AppendLine();
            builder.AppendLine("`stopped` and `worked` are not mutually exclusive: a setup may " +
                               "deliver the correction and only later invalidate the model. " +
                               "`censored` belongs to the structural walk (neither target nor " +
                               "invalidation inside the horizon); `resolved` belongs to the " +
                               "finder's TP/SL event and is almost always 100% because the replay " +
                               "runs to the end of the file. `worked` is the structural label; " +
                               "`profitable` is the actual TP/SL outcome after costs.");

            BinTable(builder, "Convergence c = span / apex distance",
                "The two-sided corridor is the sharpness filter: low c is a wide channel, " +
                "high c is a needle. `c = 0.5` means the apex is two skeleton lengths beyond P4; " +
                "`c = 2` means half a length.",
                samples, s => s.C,
                new[]
                {
                    (0d, 0.2d, "0.0–0.2"),
                    (0.2d, 0.3d, "0.2–0.3"),
                    (0.3d, 0.5d, "0.3–0.5"),
                    (0.5d, 0.8d, "0.5–0.8"),
                    (0.8d, 1.2d, "0.8–1.2"),
                    (1.2d, 2.0d, "1.2–2.0"),
                    (2.0d, 3.0d, "2.0–3.0"),
                    (3.0d, 5.0d, "3.0–5.0"),
                    (5.0d, 8.0d, "5.0–8.0"),
                    (8.0d, double.PositiveInfinity, "8.0+")
                });

            BinTable(builder, "Aggregate spill A(2–4)",
                "The shipped D-INSIDE metric. Lower is a cleaner wedge.",
                samples, s => s.A234,
                new[]
                {
                    (0d, 0.002d, "0–0.002"),
                    (0.002d, 0.003d, "0.002–0.003"),
                    (0.003d, 0.005d, "0.003–0.005"),
                    (0.005d, 0.01d, "0.005–0.01"),
                    (0.01d, 0.02d, "0.01–0.02"),
                    (0.02d, double.PositiveInfinity, "0.02+")
                });

            BinTable(builder, "Worst sub-wave spill A(max of 2,3,4)",
                "If this separates better than A(2–4), one bad sub-wave matters more than " +
                "the aggregate. This is the main candidate for replacing the aggregate gate.",
                samples, s => s.AMax,
                new[]
                {
                    (0d, 0.005d, "0–0.005"),
                    (0.005d, 0.01d, "0.005–0.01"),
                    (0.01d, 0.02d, "0.01–0.02"),
                    (0.02d, 0.05d, "0.02–0.05"),
                    (0.05d, double.PositiveInfinity, "0.05+")
                });

            BinTable(builder, "Wave 5 early spill",
                "Only the span available at the signal (P4 → signal bar). This is the only " +
                "wave-5 spill a live filter can use; the full-wave value is ex-post.",
                samples, s => s.A5Early,
                new[]
                {
                    (0d, 0.05d, "0–0.05"),
                    (0.05d, 0.1d, "0.05–0.10"),
                    (0.1d, 0.2d, "0.10–0.20"),
                    (0.2d, 0.5d, "0.20–0.50"),
                    (0.5d, double.PositiveInfinity, "0.50+")
                });

            BinTable(builder, "Longest sustained outside run",
                $"Consecutive bars spending more than {OUTSIDE_SHARE:P0} of the local corridor " +
                "outside the lines. Distinguishes a wick from a breakout.",
                samples, s => s.MaxOutsideRun,
                new[]
                {
                    (0d, 1d, "0"),
                    (1d, 2d, "1"),
                    (2d, 3d, "2"),
                    (3d, 4d, "3"),
                    (4d, 6d, "4–5"),
                    (6d, double.PositiveInfinity, "6+")
                });

            BinTable(builder, "Wave 4 retrace of wave 3",
                "D-W4-38. The shipped default is 0.382; the borderline manual case §9.17 " +
                "sits at 0.370.",
                samples, s => s.W4W3,
                new[]
                {
                    (0d, 0.236d, "0–0.236"),
                    (0.236d, 0.30d, "0.236–0.30"),
                    (0.30d, 0.382d, "0.30–0.382"),
                    (0.382d, 0.5d, "0.382–0.5"),
                    (0.5d, 0.786d, "0.5–0.786"),
                    (0.786d, 1d, "0.786–1.0")
                });

            BinTable(builder, "Wave 4 level inside wave 2",
                "D-W4-24. 0 is the end of W1, 1 is the end of W2. The shipped default is 0.236.",
                samples, s => s.W4Level,
                new[]
                {
                    (0d, 0.1d, "0–0.1"),
                    (0.1d, 0.15d, "0.1–0.15"),
                    (0.15d, 0.236d, "0.15–0.236"),
                    (0.236d, 0.382d, "0.236–0.382"),
                    (0.382d, double.PositiveInfinity, "0.382+")
                });

            BinTable(builder, "Wave 2 retrace of wave 1",
                "A shallow W2 is a trend, a very deep W2 is a structure break. The archive " +
                "has a valid 34.2% case (§9.11), so a global 0.5 floor is not safe.",
                samples, s => s.W2Retr,
                new[]
                {
                    (0d, 0.236d, "0–0.236"),
                    (0.236d, 0.382d, "0.236–0.382"),
                    (0.382d, 0.5d, "0.382–0.5"),
                    (0.5d, 0.786d, "0.5–0.786"),
                    (0.786d, 1d, "0.786–1.0")
                });

            BinTable(builder, "Wave 3 / wave 1",
                "The strongest separation in the first run. A small W3/W1 means the third wave " +
                "barely exceeds the first — the tight contracting wedge; a large one is closer " +
                "to an impulse.",
                samples, s => s.W3W1,
                new[]
                {
                    (0d, 0.3d, "0–0.3"),
                    (0.3d, 0.5d, "0.3–0.5"),
                    (0.5d, 0.7d, "0.5–0.7"),
                    (0.7d, 0.85d, "0.7–0.85"),
                    (0.85d, 1d, "0.85–1.0")
                });

            BinTable(builder, "Wave 5 / wave 3",
                "How mature the fifth wave is at the signal. A small W5/W3 means an early " +
                "break of V(3); a large one means the entry is close to the |W3| ceiling.",
                samples, s => s.W5W3,
                new[]
                {
                    (0d, 0.382d, "0–0.382"),
                    (0.382d, 0.618d, "0.382–0.618"),
                    (0.618d, 0.786d, "0.618–0.786"),
                    (0.786d, 1d, "0.786–1.0")
                });

            builder.AppendLine("## AUC — separation of worked vs false");
            builder.AppendLine();
            builder.AppendLine("Mann–Whitney AUC: 0.5 is no separation, 0.6 is moderate, 0.7 is " +
                               "strong for this kind of data. A metric with AUC below 0.55 is not " +
                               "worth a production gate.");
            builder.AppendLine();
            builder.AppendLine("| Metric | AUC worked236 | AUC worked382 | AUC profitable |");
            builder.AppendLine("|---|---:|---:|---:|");
            AucRow(builder, "c", samples, s => s.C);
            AucRow(builder, "1/c (apex distance)", samples, s => s.ApexRatio);
            AucRow(builder, "A(2–4)", samples, s => s.A234);
            AucRow(builder, "A(max 2–4)", samples, s => s.AMax);
            AucRow(builder, "A5 early", samples, s => s.A5Early);
            AucRow(builder, "max outside run", samples, s => s.MaxOutsideRun);
            AucRow(builder, "W4/W3", samples, s => s.W4W3);
            AucRow(builder, "W4 level in W2", samples, s => s.W4Level);
            AucRow(builder, "W2/W1", samples, s => s.W2Retr);
            AucRow(builder, "W3 penetration", samples, s => s.W3Pen);
            AucRow(builder, "W3/W1", samples, s => s.W3W1);
            AucRow(builder, "W5/W3", samples, s => s.W5W3);

            builder.AppendLine();
            builder.AppendLine("## 2D — convergence × worst sub-wave spill");
            builder.AppendLine();
            builder.AppendLine("Cells show `signals / worked236%`. The top-left is wide and clean, " +
                               "the bottom-right is sharp and dirty; the expected sweet spot is the " +
                               "middle band.");
            builder.AppendLine();
            builder.AppendLine("| c \\ A(max) | 0–0.005 | 0.005–0.01 | 0.01–0.02 | 0.02+ |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            var cBins = new[]
            {
                (0d, 0.3d, "0–0.3"),
                (0.3d, 0.5d, "0.3–0.5"),
                (0.5d, 1.0d, "0.5–1.0"),
                (1.0d, 2.0d, "1.0–2.0"),
                (2.0d, double.PositiveInfinity, "2.0+")
            };
            var aBins = new[]
            {
                (0d, 0.005d, "0–0.005"),
                (0.005d, 0.01d, "0.005–0.01"),
                (0.01d, 0.02d, "0.01–0.02"),
                (0.02d, double.PositiveInfinity, "0.02+")
            };
            foreach (var (cLow, cHigh, cLabel) in cBins)
            {
                builder.Append(FormattableString.Invariant($"| {cLabel} "));
                foreach (var (aLow, aHigh, _) in aBins)
                {
                    List<Sample> cell = samples
                        .Where(s => s.C >= cLow && s.C < cHigh)
                        .Where(s => s.AMax >= aLow && s.AMax < aHigh)
                        .ToList();
                    builder.Append(FormattableString.Invariant(
                        $"| {cell.Count} / {Share(cell, s => s.Worked236):P0} "));
                }

                builder.AppendLine("|");
            }

            builder.AppendLine();
            builder.AppendLine("## Preset frontier");
            builder.AppendLine();
            builder.AppendLine("Coverage is the share of the permissive baseline kept by the preset. " +
                               "The trading preset is the intended default; the beauty preset is for " +
                               "the trainer and manual markup, not for live signals.");
            builder.AppendLine();
            builder.AppendLine("| Preset | MinC | MaxC | A(2–4) | A5 early | Signals | Coverage | " +
                               "worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            PresetRow(builder, "baseline", samples, 0d, double.PositiveInfinity,
                double.PositiveInfinity, double.PositiveInfinity);
            PresetRow(builder, "trading", samples, 0.30d, 2.50d, 0.005d, double.PositiveInfinity);
            PresetRow(builder, "quality", samples, 0.40d, 2.00d, 0.004d, 0.10d);
            PresetRow(builder, "beauty", samples, 0.50d, 1.50d, 0.003d, 0.15d);

            builder.AppendLine();
            builder.AppendLine("## Data-driven candidates (in-sample)");
            builder.AppendLine();
            builder.AppendLine("These are not shipped defaults — they are the first hypotheses the " +
                               "Phase 0 report suggests. The train/test split is 2017–2022 / " +
                               "2023–2026; a candidate is only interesting when the test column " +
                               "moves in the same direction.");
            builder.AppendLine();
            builder.AppendLine("| Rule | Signals | Coverage | worked236 | resolved | profitable | avg netR | " +
                               "train N | train worked236 | test N | test worked236 |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            RuleRow(builder, "baseline", samples, _ => true);
            RuleRow(builder, "c >= 0.8", samples, s => s.C >= 0.8d);
            RuleRow(builder, "c >= 1.2", samples, s => s.C >= 1.2d);
            RuleRow(builder, "c >= 2.0", samples, s => s.C >= 2.0d);
            RuleRow(builder, "W2/W1 >= 0.5", samples, s => s.W2Retr >= 0.5d);
            RuleRow(builder, "W2/W1 >= 0.786", samples, s => s.W2Retr >= 0.786d);
            RuleRow(builder, "W4/W3 <= 0.382", samples, s => s.W4W3 <= 0.382d);
            RuleRow(builder, "W4/W3 <= 0.30", samples, s => s.W4W3 <= 0.30d);
            RuleRow(builder, "W2/W1 >= 0.5 & W4/W3 <= 0.382", samples,
                s => s.W2Retr >= 0.5d && s.W4W3 <= 0.382d);
            RuleRow(builder, "W2/W1 >= 0.5 & W4/W3 <= 0.382 & c >= 1.0", samples,
                s => s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.C >= 1.0d);
            RuleRow(builder, "W2/W1 >= 0.5 & W4/W3 <= 0.382 & c >= 1.0 & A(2–4) <= 0.005",
                samples,
                s => s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.C >= 1.0d && s.A234 <= 0.005d);
            RuleRow(builder, "W2/W1 >= 0.5 & W4/W3 <= 0.382 & c >= 1.0 & A5 early <= 0.20",
                samples,
                s => s.W2Retr >= 0.5d && s.W4W3 <= 0.382d && s.C >= 1.0d && s.A5Early <= 0.20d);

            builder.AppendLine();
            builder.AppendLine("## Trading preset by file");
            builder.AppendLine();
            builder.AppendLine("| File | Signals | Coverage | worked236 | worked382 | resolved | profitable | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (FileStat stat in fileStats.OrderBy(s => s.File, StringComparer.Ordinal))
            {
                List<Sample> fileSamples = samples.Where(s => s.File == stat.File).ToList();
                List<Sample> kept = fileSamples.Where(s =>
                    s.C >= 0.30d && s.C <= 2.50d && s.A234 <= 0.005d).ToList();
                List<Sample> keptResolved = kept.Where(s => s.Resolved).ToList();
                builder.AppendLine(FormattableString.Invariant(
                    $"| `{stat.File}` | {kept.Count} | {Share(fileSamples, s => kept.Contains(s)):P0} | {Share(kept, s => s.Worked236):P1} | {Share(kept, s => s.Worked382):P1} | {keptResolved.Count} | {Share(keptResolved, s => s.Profitable):P1} | {keptResolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3} |"));
            }

            builder.AppendLine();
            builder.AppendLine("## What the data says");
            builder.AppendLine();
            List<Sample> wide = samples.Where(s => s.C < 0.3d).ToList();
            List<Sample> sharp = samples.Where(s => s.C >= 2.0d).ToList();
            List<Sample> lateWave5 = samples.Where(s => s.A5Early >= 0.5d).ToList();
            List<Sample> longRun = samples.Where(s => s.MaxOutsideRun >= 6).ToList();
            List<Sample> wideResolved = wide.Where(s => s.Resolved).ToList();
            List<Sample> sharpResolved = sharp.Where(s => s.Resolved).ToList();
            List<Sample> lateWave5Resolved = lateWave5.Where(s => s.Resolved).ToList();
            List<Sample> longRunResolved = longRun.Where(s => s.Resolved).ToList();

            builder.AppendLine(FormattableString.Invariant(
                $"- **Baseline:** {samples.Count} signals, `worked236` {Share(samples, s => s.Worked236):P1}, resolved {resolved.Count} ({Share(samples, s => s.Resolved):P1}), profitable among resolved {Share(resolved, s => s.Profitable):P1}, avg `netR` among resolved {resolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3}. Geometry alone does not make the current TP/SL mechanics profitable."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Sharpness:** `worked236` rises monotonically with `c` — {Share(wide, s => s.Worked236):P1} for c < 0.3 vs {Share(sharp, s => s.Worked236):P1} for c >= 2.0 — but `profitable` is flat ({Share(wideResolved, s => s.Profitable):P1} vs {Share(sharpResolved, s => s.Profitable):P1}) and the AUC for profitability is {Auc(resolved, s => s.C, s => s.Profitable):F3}. The upper tail 8.0+ does not collapse in this sample, so the \"too sharp is bad\" half of the hypothesis is not confirmed for the structural label. Do not add a `MaxConvergence` gate before a holdout check."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Spill:** the aggregate `A(2–4)`, the worst sub-wave `A(max)` and the early wave-5 spill all have AUC near 0.5 for profitability ({Auc(resolved, s => s.A234, s => s.Profitable):F3}, {Auc(resolved, s => s.AMax, s => s.Profitable):F3}, {Auc(resolved, s => s.A5Early, s => s.Profitable):F3}). The clean-wedge hypothesis is not supported as a profit filter in this sample. The only positive-netR spill bin is wave-5 early spill >= 0.5 ({lateWave5Resolved.Count} resolved, profitable {Share(lateWave5Resolved, s => s.Profitable):P1}, avg netR {lateWave5Resolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3}); that is likely a proxy for a late, sharp wave 5, not a reason to prefer spill."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Shape:** the strongest structural separators are `W2/W1` (AUC {Auc(samples, s => s.W2Retr, s => s.Worked236):F3}) and `W3/W1` (AUC {Auc(samples, s => s.W3W1, s => s.Worked236):F3}), not the spill. `W4/W3` is inverse: lower values are better for `worked236` (AUC {Auc(samples, s => s.W4W3, s => s.Worked236):F3}), so the shipped `D-W4-38 >= 0.382` may be rejecting the best structural group. This is surprising and must be validated on holdout before changing the hard rule."));
            builder.AppendLine(FormattableString.Invariant(
                $"- **Long outside runs:** a run of 6+ bars is not a defect in this sample ({longRunResolved.Count} resolved, profitable {Share(longRunResolved, s => s.Profitable):P1}, avg netR {longRunResolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3}), but its AUC is only {Auc(resolved, s => s.MaxOutsideRun, s => s.Profitable):F3}. Treat it as a diagnostic, not a filter."));
            builder.AppendLine("- **Next step:** the geometry axes explain whether a correction " +
                               "happens, but not whether the current entry/TP/SL earns. Phase 1 " +
                               "should test the execution side (wave-5 maturity, TP mode, R:R) " +
                               "and the context side (W2 depth, higher timeframe, activity " +
                               "contraction) before adding any new geometry gate.");

            builder.AppendLine();
            builder.AppendLine("## Examples");
            builder.AppendLine();
            builder.AppendLine("### Best structural outcomes");
            builder.AppendLine();
            builder.AppendLine("| Entry | Symbol | TF | c | A(2–4) | A(max) | W4/W3 | MFE | netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (Sample s in samples
                         .OrderByDescending(s => s.MfeDiag)
                         .ThenByDescending(s => s.NetR)
                         .Take(12))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {s.EntryTime:yyyy-MM-dd HH:mm} | {s.Symbol} | {s.TimeFrame} | {s.C:F2} | {s.A234:F4} | {s.AMax:F4} | {s.W4W3:F3} | {s.MfeDiag:F3} | {s.NetR:F2} |"));
            }

            builder.AppendLine();
            builder.AppendLine("### Worst false signals");
            builder.AppendLine();
            builder.AppendLine("| Entry | Symbol | TF | c | A(2–4) | A(max) | W4/W3 | MFE | netR |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (Sample s in samples
                         .Where(s => !s.Worked236)
                         .OrderBy(s => s.MfeDiag)
                         .ThenBy(s => s.NetR)
                         .Take(12))
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"| {s.EntryTime:yyyy-MM-dd HH:mm} | {s.Symbol} | {s.TimeFrame} | {s.C:F2} | {s.A234:F4} | {s.AMax:F4} | {s.W4W3:F3} | {s.MfeDiag:F3} | {s.NetR:F2} |"));
            }

            return builder.ToString();
        }

        private static void BinTable(
            StringBuilder builder,
            string title,
            string note,
            IReadOnlyList<Sample> samples,
            Func<Sample, double> metric,
            IReadOnlyList<(double Low, double High, string Label)> bins)
        {
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            builder.AppendLine(note);
            builder.AppendLine();
            builder.AppendLine("| Bin | Signals | worked236 | worked382 | final236 | resolved | profitable | avg MFE | avg netR |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var (low, high, label) in bins)
            {
                List<Sample> bin = samples
                    .Where(s =>
                    {
                        double value = metric(s);
                        return value >= low && value < high;
                    })
                    .ToList();

                List<Sample> binResolved = bin.Where(s => s.Resolved).ToList();
                builder.AppendLine(FormattableString.Invariant(
                    $"| {label} | {bin.Count} | {Share(bin, s => s.Worked236):P1} | {Share(bin, s => s.Worked382):P1} | {Share(bin, s => s.Final236):P1} | {binResolved.Count} | {Share(binResolved, s => s.Profitable):P1} | {bin.DefaultIfEmpty().Average(s => s?.MfeDiag ?? 0):F3} | {binResolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3} |"));
            }

            builder.AppendLine();
        }

        private static void AucRow(
            StringBuilder builder,
            string name,
            IReadOnlyList<Sample> samples,
            Func<Sample, double> metric)
        {
            double auc236 = Auc(samples, metric, s => s.Worked236);
            double auc382 = Auc(samples, metric, s => s.Worked382);
            double aucProfit = Auc(samples.Where(s => s.Resolved).ToList(), metric,
                s => s.Profitable);
            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {auc236:F3} | {auc382:F3} | {aucProfit:F3} |"));
        }

        private static void PresetRow(
            StringBuilder builder,
            string name,
            IReadOnlyList<Sample> samples,
            double minC,
            double maxC,
            double maxA234,
            double maxA5Early)
        {
            List<Sample> kept = samples.Where(s =>
                s.C >= minC &&
                s.C <= maxC &&
                s.A234 <= maxA234 &&
                s.A5Early <= maxA5Early).ToList();
            List<Sample> keptResolved = kept.Where(s => s.Resolved).ToList();

            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {minC:F2} | {(double.IsPositiveInfinity(maxC) ? "off" : maxC.ToString("F2", CultureInfo.InvariantCulture))} | {(double.IsPositiveInfinity(maxA234) ? "off" : maxA234.ToString("F3", CultureInfo.InvariantCulture))} | {(double.IsPositiveInfinity(maxA5Early) ? "off" : maxA5Early.ToString("F2", CultureInfo.InvariantCulture))} | {kept.Count} | {Share(samples, s => kept.Contains(s)):P0} | {Share(kept, s => s.Worked236):P1} | {Share(kept, s => s.Worked382):P1} | {keptResolved.Count} | {Share(keptResolved, s => s.Profitable):P1} | {keptResolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3} |"));
        }

        private static void RuleRow(
            StringBuilder builder,
            string name,
            IReadOnlyList<Sample> samples,
            Func<Sample, bool> predicate)
        {
            List<Sample> kept = samples.Where(predicate).ToList();
            List<Sample> keptResolved = kept.Where(s => s.Resolved).ToList();
            List<Sample> train = kept.Where(s => s.EntryTime.Year <= 2022).ToList();
            List<Sample> test = kept.Where(s => s.EntryTime.Year >= 2023).ToList();

            builder.AppendLine(FormattableString.Invariant(
                $"| {name} | {kept.Count} | {Share(samples, s => kept.Contains(s)):P0} | {Share(kept, s => s.Worked236):P1} | {keptResolved.Count} | {Share(keptResolved, s => s.Profitable):P1} | {keptResolved.DefaultIfEmpty().Average(s => s?.NetR ?? 0):F3} | {train.Count} | {Share(train, s => s.Worked236):P1} | {test.Count} | {Share(test, s => s.Worked236):P1} |"));
        }

        private static double Auc(
            IReadOnlyList<Sample> samples,
            Func<Sample, double> metric,
            Func<Sample, bool> positive)
        {
            var values = samples
                .Select(s => (Value: metric(s), Positive: positive(s)))
                .Where(x => !double.IsNaN(x.Value) && !double.IsInfinity(x.Value))
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

        private static double Share(IReadOnlyList<Sample> samples, Func<Sample, bool> predicate)
        {
            return samples.Count == 0 ? 0 : (double)samples.Count(predicate) / samples.Count;
        }

        private static double Quantile(IReadOnlyList<double> values, double q)
        {
            if (values.Count == 0)
                return 0;

            var sorted = values.OrderBy(v => v).ToList();
            int index = (int)Math.Round(q * (sorted.Count - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }

        private static string BuildCsv(IReadOnlyList<Sample> samples)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "file,symbol,timeframe,entry_time,signal_bar,is_up_diagonal,entry,risk,sl,tp," +
                "p0_bar,p4_bar,p5_bar,median_range_w1,median_range_w3,median_range_w5," +
                "activity_w3_w1,activity_w5_w3,c,apex_ratio," +
                "a234,a2,a3,a4,a5_early,a5_full,a_max,max_outside_run," +
                "w1,w2,w3,w4,w5,v5,w2_retr,w3_pen,w4_w3,w4_level,w4_w2,w3_w1,w5_w3,overlap_depth," +
                "bars_w1,bars_w2,bars_w3,bars_w4,bars_w5,horizon," +
                "worked236,worked382,worked50,final236,final382,stopped,censored," +
                "mfe_diag,mfe_r,mae_r,gross_r,cost_r,net_r,resolved,profitable,bars_to_max_retrace," +
                "final_extreme,final_extreme_bar,outcome,exit_price,outcome_bar");

            foreach (Sample s in samples.OrderBy(s => s.File, StringComparer.Ordinal)
                         .ThenBy(s => s.EntryTime))
            {
                builder.AppendLine(string.Join(",",
                    Csv(s.File),
                    Csv(s.Symbol),
                    Csv(s.TimeFrame),
                    Csv(s.EntryTime.ToString("o", CultureInfo.InvariantCulture)),
                    s.SignalBar.ToString(CultureInfo.InvariantCulture),
                    B(s.IsUpDiagonal),
                    F(s.Entry), F(s.Risk), F(s.Sl), F(s.Tp),
                    s.P0Bar.ToString(CultureInfo.InvariantCulture),
                    s.P4Bar.ToString(CultureInfo.InvariantCulture),
                    s.P5Bar.ToString(CultureInfo.InvariantCulture),
                    F(s.MedianRangeW1), F(s.MedianRangeW3), F(s.MedianRangeW5),
                    F(s.ActivityW3W1), F(s.ActivityW5W3),
                    F(s.C), F(s.ApexRatio),
                    F(s.A234), F(s.A2), F(s.A3), F(s.A4), F(s.A5Early), F(s.A5Full), F(s.AMax),
                    s.MaxOutsideRun.ToString(CultureInfo.InvariantCulture),
                    F(s.W1), F(s.W2), F(s.W3), F(s.W4), F(s.W5), F(s.V5),
                    F(s.W2Retr), F(s.W3Pen), F(s.W4W3), F(s.W4Level), F(s.W4W2),
                    F(s.W3W1), F(s.W5W3), F(s.OverlapDepth),
                    s.BarsW1.ToString(CultureInfo.InvariantCulture),
                    s.BarsW2.ToString(CultureInfo.InvariantCulture),
                    s.BarsW3.ToString(CultureInfo.InvariantCulture),
                    s.BarsW4.ToString(CultureInfo.InvariantCulture),
                    s.BarsW5.ToString(CultureInfo.InvariantCulture),
                    s.Horizon.ToString(CultureInfo.InvariantCulture),
                    B(s.Worked236), B(s.Worked382), B(s.Worked50),
                    B(s.Final236), B(s.Final382), B(s.Stopped), B(s.Censored),
                    F(s.MfeDiag), F(s.MfeR), F(s.MaeR), F(s.GrossR), F(s.CostR), F(s.NetR),
                    B(s.Resolved), B(s.Profitable),
                    s.BarsToMaxRetrace.ToString(CultureInfo.InvariantCulture),
                    F(s.FinalExtreme), s.FinalExtremeBar.ToString(CultureInfo.InvariantCulture),
                    Csv(s.Outcome), F(s.ExitPrice), s.OutcomeBar.ToString(CultureInfo.InvariantCulture)));
            }

            return builder.ToString();
        }

        private static string F(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString("G17", CultureInfo.InvariantCulture);

        private static string B(bool value) => value ? "1" : "0";

        private static string Csv(string value) =>
            value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;

        #endregion

        private static string FindDataDir()
        {
            DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                string dataDir = Path.Combine(dir.FullName, "data");
                if (Directory.Exists(dataDir) &&
                    File.Exists(Path.Combine(dir.FullName, "TradeKit.sln")))
                {
                    return dataDir;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repo 'data' folder above the test directory.");
        }

        private sealed class SignalRecord
        {
            public SignalRecord(ElliottWaveSignalEventArgs args)
            {
                Args = args;
            }

            public ElliottWaveSignalEventArgs Args { get; }
            public string Outcome { get; set; } = "NONE";
            public double ExitPrice { get; set; }
            public int OutcomeBar { get; set; } = -1;
        }

        private sealed class Sample
        {
            public string File { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string TimeFrame { get; set; } = string.Empty;
            public TestBarsProvider Provider { get; set; } = null!;
            public DateTime EntryTime { get; set; }
            public int SignalBar { get; set; }
            public bool IsUpDiagonal { get; set; }
            public double Entry { get; set; }
            public double Risk { get; set; }
            public double Sl { get; set; }
            public double Tp { get; set; }
            public int P0Bar { get; set; }
            public int P4Bar { get; set; }
            public int P5Bar { get; set; }
            public double MedianRangeW1 { get; set; }
            public double MedianRangeW3 { get; set; }
            public double MedianRangeW5 { get; set; }
            public double ActivityW3W1 => MedianRangeW1 > 1e-12
                ? MedianRangeW3 / MedianRangeW1
                : double.NaN;
            public double ActivityW5W3 => MedianRangeW3 > 1e-12
                ? MedianRangeW5 / MedianRangeW3
                : double.NaN;
            public double V0 { get; set; }
            public double V3 { get; set; }
            public double V4 { get; set; }
            public double V5 { get; set; }
            public double W1 { get; set; }
            public double W2 { get; set; }
            public double W3 { get; set; }
            public double W4 { get; set; }
            public double W5 { get; set; }
            public double C { get; set; }
            public double ApexRatio { get; set; }
            public double A234 { get; set; }
            public double A2 { get; set; }
            public double A3 { get; set; }
            public double A4 { get; set; }
            public double A5Early { get; set; }
            public double A5Full { get; set; }
            public double AMax { get; set; }
            public int MaxOutsideRun { get; set; }
            public int V3BreakBar { get; set; } = -1;
            public int DelayBars => V3BreakBar < 0 ? -1 : SignalBar - V3BreakBar;
            public int RepriceCount { get; set; } = -1;
            public double Line13AtSignal { get; set; }
            public double EntryDeltaR { get; set; }
            public double EntryDeltaW3 { get; set; }
            public double LineDistanceW3 { get; set; }
            public double EntryVsLineR { get; set; }
            public double W2Retr { get; set; }
            public double W3Pen { get; set; }
            public double W4W3 { get; set; }
            public double W4Level { get; set; }
            public double W4W2 { get; set; }
            public double W3W1 { get; set; }
            public double W5W3 { get; set; }
            public double OverlapDepth { get; set; }
            public int BarsW1 { get; set; }
            public int BarsW2 { get; set; }
            public int BarsW3 { get; set; }
            public int BarsW4 { get; set; }
            public int BarsW5 { get; set; }
            public bool Worked236 { get; set; }
            public bool Worked382 { get; set; }
            public bool Worked50 { get; set; }
            public bool Final236 { get; set; }
            public bool Final382 { get; set; }
            public bool Stopped { get; set; }
            public bool Censored { get; set; }
            public double MfeDiag { get; set; }
            public double MfeR { get; set; }
            public double MaeR { get; set; }
            public double GrossR { get; set; }
            public double CostR { get; set; }
            public double NetR { get; set; }
            public bool Resolved { get; set; }
            public bool Profitable { get; set; }
            public int BarsToMaxRetrace { get; set; }
            public int Horizon { get; set; }
            public double FinalExtreme { get; set; }
            public int FinalExtremeBar { get; set; }
            public string Outcome { get; set; } = "NONE";
            public double ExitPrice { get; set; }
            public int OutcomeBar { get; set; } = -1;
            public IReadOnlyDictionary<string, int> FinderDiag { get; set; } =
                new Dictionary<string, int>();
        }

        private readonly record struct WalkResult(
            bool Stopped,
            bool Censored,
            double MaxRetraceEver,
            double MaxRetraceFinal,
            double MfeR,
            double MaeR,
            int BarsToMaxRetrace,
            int FinalExtremeBar,
            double FinalExtreme);

        private sealed record FileStat(
            string File,
            int Bars,
            int Signals,
            int Worked236,
            int Worked382,
            int Stopped,
            int Censored);

        private sealed record FileRun(List<Sample> Samples, FileStat Stat);
    }
}
