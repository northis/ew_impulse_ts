using System.Globalization;
using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// One-off research harness (Explicit, excluded from normal runs) that sweeps the
    /// zigzag <c>Period</c> (the <see cref="TradeKit.Core.Indicators.DeviationExtremumFinder"/>
    /// scale rate = deviationPercent × 100) for both <see cref="ImpulseSetupFinder"/> and
    /// <see cref="TriangleSetupFinder"/> over the saved <c>data/</c> archive, counting how
    /// many setups each period produces. The goal is to discover which period maximises the
    /// setup count and how that optimum correlates with the timeframe and the instrument's
    /// percentage volatility (median bar range in basis points), so an auto-period formula
    /// can be baked in (Period = 0 → auto).
    /// <para>
    /// Run: <c>dotnet test --filter "FullyQualifiedName~PeriodSweep"</c> — writes
    /// <c>reports/period_sweep.md</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Explicit("Research harness — run manually to (re)generate reports/period_sweep.md")]
    [Category("Research")]
    public class PeriodSweepTests
    {
        // Bounded slice per file so the 11-finder triangle sweep stays tractable while
        // still covering a long, multi-regime stretch of real history.
        private const int MAX_BARS = 6000;

        // Periods (scale rates) to probe. Covers the fine m15 range up to coarse h1/daily.
        private static readonly int[] PERIODS =
            { 5, 8, 10, 12, 15, 20, 25, 30, 40, 50, 60, 80, 100 };

        // Representative files: same symbols across m15 & h1 and very different price
        // scales (EURUSD ~1.1, USDJPY ~150, XAUUSD ~2000, GBPJPY ~185, AUDCAD ~0.9).
        private static readonly string[] FILES =
        {
            "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "EURUSD_m15_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "USDJPY_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "USDJPY_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "XAUUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv",
            "XAUUSD_m15_2017-12-27T18-00-00_2026-05-31T23-00-00.csv",
            "GBPJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPJPY_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "AUDCAD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "AUDCAD_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv"
        };

        [Test]
        public void SweepPeriods_WriteReport()
        {
            string dataDir = FindDataDir();
            var rows = new List<Row>();

            foreach (string file in FILES)
            {
                string path = Path.Combine(dataDir, file);
                if (!File.Exists(path))
                {
                    TestContext.WriteLine($"MISSING: {file}");
                    continue;
                }

                bool isM15 = file.Contains("_m15_");
                ITimeFrame tf = isM15 ? TimeFrameHelper.Minute15 : TimeFrameHelper.Hour1;

                var provider = new TestBarsProvider(tf);
                provider.LoadCandles(path);
                int n = Math.Min(provider.Count, MAX_BARS);
                if (n < 500)
                {
                    TestContext.WriteLine($"TOO SHORT: {file} ({provider.Count})");
                    continue;
                }

                (double medianBps, double meanClose) = Volatility(provider, n);

                var row = new Row
                {
                    File = file,
                    Timeframe = isM15 ? "m15" : "h1",
                    Bars = n,
                    MedianBarBps = medianBps,
                    MeanClose = meanClose
                };

                foreach (int period in PERIODS)
                {
                    row.ImpulseCounts[period] = CountImpulseSetups(provider, period, n);
                    row.TriangleCounts[period] = CountTriangleSetups(provider, period, n);
                }

                row.BestImpulsePeriod = ArgMax(row.ImpulseCounts);
                row.BestTrianglePeriod = ArgMax(row.TriangleCounts);
                rows.Add(row);

                TestContext.WriteLine(
                    $"{file}: medianBps={medianBps:F1} meanClose={meanClose:F3} " +
                    $"bestImpulseP={row.BestImpulsePeriod} bestTriangleP={row.BestTrianglePeriod}");
            }

            string report = BuildReport(rows);
            string reportsDir = Path.Combine(FindRepoRoot(), "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "period_sweep.md");
            File.WriteAllText(outPath, report);
            TestContext.WriteLine($"Wrote {outPath}");

            Assert.That(rows, Is.Not.Empty, "No files were processed.");
        }

        // Triangles are far rarer than impulses, so a 6000-bar slice with the default
        // filters yields none. This focused sweep uses a larger history and loose filters
        // on files known to contain triangles, to confirm how the triangle base period
        // responds to the volatility-scaled period.
        private const int TRIANGLE_MAX_BARS = 60000;

        private static readonly int[] TRIANGLE_PERIODS = { 5, 10, 15, 20, 25, 30, 40, 50 };

        private static readonly string[] TRIANGLE_FILES =
        {
            "AUDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "AUDUSD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "EURUSD_m15_2017-12-27T20-00-00_2026-05-31T23-00-00.csv"
        };

        [Test]
        public void DiagnoseTriangleRejections()
        {
            string dataDir = FindDataDir();
            var sb = new StringBuilder();
            sb.AppendLine("# Triangle rejection diagnostics");
            sb.AppendLine();
            sb.AppendLine($"Bars per file: up to {TRIANGLE_MAX_BARS}. Filters: MinSize=0.1%, Bars=10, period=0 (auto).");
            sb.AppendLine("Counts = how many assembled ABCDE candidates die at each gate (or enter).");
            sb.AppendLine();

            string[] order =
            {
                "assembled", "notInitialMove", "triangleInvalidated", "priceRulesFail",
                "tooFewBars", "tooSmall", "durationInsane", "notContained", "tpSlHit",
                "tooCloseToSl", "duplicate", "weakTrend", "entered"
            };

            foreach (string file in TRIANGLE_FILES)
            {
                string path = Path.Combine(dataDir, file);
                if (!File.Exists(path)) { TestContext.WriteLine($"MISSING: {file}"); continue; }

                bool isM15 = file.Contains("_m15_");
                ITimeFrame tf = isM15 ? TimeFrameHelper.Minute15 : TimeFrameHelper.Hour1;
                var provider = new TestBarsProvider(tf);
                provider.LoadCandles(path);
                int n = Math.Min(provider.Count, TRIANGLE_MAX_BARS);

                var ewParams = new EWParams(0, 0.1, 10);
                var finder = new TriangleSetupFinder(provider, provider.BarSymbol, ewParams);
                finder.MarkAsInitialized();
                for (int bar = 0; bar < n; bar++)
                    finder.CheckBar(provider.GetOpenTime(bar));

                sb.AppendLine($"## {file} ({(isM15 ? "m15" : "h1")}, basePeriod={finder.ZigzagPeriod})");
                sb.AppendLine();
                sb.AppendLine("| gate | count |");
                sb.AppendLine("|---|---|");
                foreach (string k in order)
                    sb.AppendLine($"| {k} | {finder.Diag.GetValueOrDefault(k)} |");
                sb.AppendLine();

                TestContext.WriteLine($"{file}: " +
                    string.Join(" ", order.Select(k => $"{k}={finder.Diag.GetValueOrDefault(k)}")));
            }

            string reportsDir = Path.Combine(FindRepoRoot(), "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "triangle_rejections.md");
            File.WriteAllText(outPath, sb.ToString());
            TestContext.WriteLine($"Wrote {outPath}");
        }

        [Test]
        public void SweepTrianglePeriods_Focused()
        {
            string dataDir = FindDataDir();
            var sb = new StringBuilder();
            sb.AppendLine("# Triangle period sweep (focused, loose filters)");
            sb.AppendLine();
            sb.AppendLine($"Bars per file: up to {TRIANGLE_MAX_BARS}. Filters: MinSize=0.1%, Bars=10.");
            sb.AppendLine();
            sb.Append("| File | TF | medianBps |");
            foreach (int p in TRIANGLE_PERIODS) sb.Append($" {p} |");
            sb.AppendLine(" best |");
            sb.Append("|---|---|---|");
            foreach (int _ in TRIANGLE_PERIODS) sb.Append("---|");
            sb.AppendLine("---|");

            foreach (string file in TRIANGLE_FILES)
            {
                string path = Path.Combine(dataDir, file);
                if (!File.Exists(path)) { TestContext.WriteLine($"MISSING: {file}"); continue; }

                bool isM15 = file.Contains("_m15_");
                ITimeFrame tf = isM15 ? TimeFrameHelper.Minute15 : TimeFrameHelper.Hour1;
                var provider = new TestBarsProvider(tf);
                provider.LoadCandles(path);
                int n = Math.Min(provider.Count, TRIANGLE_MAX_BARS);
                (double medianBps, _) = Volatility(provider, n);

                var counts = new Dictionary<int, int>();
                foreach (int period in TRIANGLE_PERIODS)
                    counts[period] = CountTriangleSetups(provider, period, n, minSize: 0.1, bars: 10);

                int best = ArgMax(counts);
                sb.Append($"| {file} | {(isM15 ? "m15" : "h1")} | {medianBps:F1} |");
                foreach (int p in TRIANGLE_PERIODS) sb.Append($" {counts[p]} |");
                sb.AppendLine($" {best} |");
                TestContext.WriteLine($"{file}: medianBps={medianBps:F1} best={best} " +
                                      $"counts=[{string.Join(",", TRIANGLE_PERIODS.Select(p => counts[p]))}]");
            }

            string reportsDir = Path.Combine(FindRepoRoot(), "reports");
            Directory.CreateDirectory(reportsDir);
            string outPath = Path.Combine(reportsDir, "triangle_period_sweep.md");
            File.WriteAllText(outPath, sb.ToString());
            TestContext.WriteLine($"Wrote {outPath}");
        }

        // ── setup counting ────────────────────────────────────────────────

        private static int CountImpulseSetups(TestBarsProvider provider, int period, int n)
        {
            var impulseParams = new ImpulseParams(
                Period: period,
                EnterRatio: 0.35,
                TakeRatio: 1.6,
                BreakEvenRatio: 0,
                MaxZigzagPercent: 20,
                MaxOverlapseLengthPercent: 35,
                MaxDistance: 35,
                HeterogeneityMax: 20,
                MinSizePercent: 0.13,
                AreaPercent: 35,
                BarsCount: 15,
                MaxCorrectionRatioPercent: 50);

            var tradeView = new TestTradeViewManager(provider);
            var finder = new ImpulseSetupFinder(provider, tradeView, impulseParams);
            int count = 0;
            finder.OnEnter += (_, _) => count++;
            finder.MarkAsInitialized();

            for (int bar = 0; bar < n; bar++)
                finder.CheckBar(provider.GetOpenTime(bar));

            return count;
        }

        private static int CountTriangleSetups(
            TestBarsProvider provider, int period, int n, double minSize = 0.3, int bars = 20)
        {
            var ewParams = new EWParams(period, minSize, bars);
            var finder = new TriangleSetupFinder(provider, provider.BarSymbol, ewParams);
            int count = 0;
            finder.OnEnter += (_, _) => count++;
            finder.MarkAsInitialized();

            for (int bar = 0; bar < n; bar++)
                finder.CheckBar(provider.GetOpenTime(bar));

            return count;
        }

        // ── volatility metric ─────────────────────────────────────────────

        /// <summary>
        /// Returns the median bar range as a fraction of price in basis points
        /// (<c>(High-Low)/Close × 10000</c>) and the mean close, over the first
        /// <paramref name="n"/> bars.
        /// </summary>
        private static (double medianBps, double meanClose) Volatility(TestBarsProvider provider, int n)
        {
            var bps = new List<double>(n);
            double sumClose = 0;
            for (int i = 0; i < n; i++)
            {
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                double close = provider.GetClosePrice(i);
                if (close > 0)
                    bps.Add((high - low) / close * 10000.0);
                sumClose += close;
            }

            bps.Sort();
            double median = bps.Count == 0 ? 0 : bps[bps.Count / 2];
            return (median, sumClose / n);
        }

        // ── report ────────────────────────────────────────────────────────

        private static int ArgMax(Dictionary<int, int> counts)
        {
            int best = 0, bestCount = -1;
            foreach (KeyValuePair<int, int> kv in counts)
            {
                if (kv.Value > bestCount)
                {
                    bestCount = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }

        private static string BuildReport(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Period sweep — setups vs zigzag period");
            sb.AppendLine();
            sb.AppendLine($"Generated by `PeriodSweepTests`. Bars per file: {MAX_BARS}. " +
                          "Period = DeviationExtremumFinder scale rate (deviationPercent × 100).");
            sb.AppendLine();
            sb.AppendLine("`medianBps` = median bar range (High-Low)/Close in basis points — the " +
                          "instrument's percentage volatility at that timeframe.");
            sb.AppendLine();

            // Summary table
            sb.AppendLine("## Optimal period vs volatility");
            sb.AppendLine();
            sb.AppendLine("| File | TF | medianBps | meanClose | bestImpulseP | bestTriangleP |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (Row r in rows)
                sb.AppendLine($"| {r.File} | {r.Timeframe} | {r.MedianBarBps:F1} | {r.MeanClose:F3} | " +
                              $"{r.BestImpulsePeriod} | {r.BestTrianglePeriod} |");
            sb.AppendLine();

            // Full impulse curve
            sb.AppendLine("## Impulse setup counts by period");
            sb.AppendLine();
            AppendCurve(sb, rows, isImpulse: true);

            // Full triangle curve
            sb.AppendLine("## Triangle setup counts by period");
            sb.AppendLine();
            AppendCurve(sb, rows, isImpulse: false);

            return sb.ToString();
        }

        private static void AppendCurve(StringBuilder sb, List<Row> rows, bool isImpulse)
        {
            sb.Append("| File | TF |");
            foreach (int p in PERIODS) sb.Append($" {p} |");
            sb.AppendLine();
            sb.Append("|---|---|");
            foreach (int _ in PERIODS) sb.Append("---|");
            sb.AppendLine();

            foreach (Row r in rows)
            {
                sb.Append($"| {r.File} | {r.Timeframe} |");
                Dictionary<int, int> counts = isImpulse ? r.ImpulseCounts : r.TriangleCounts;
                foreach (int p in PERIODS) sb.Append($" {counts[p]} |");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private sealed class Row
        {
            public string File = string.Empty;
            public string Timeframe = string.Empty;
            public int Bars;
            public double MedianBarBps;
            public double MeanClose;
            public readonly Dictionary<int, int> ImpulseCounts = new();
            public readonly Dictionary<int, int> TriangleCounts = new();
            public int BestImpulsePeriod;
            public int BestTrianglePeriod;
        }

        // ── data-dir discovery (same convention as the other data tests) ───

        private static string FindDataDir() => Path.Combine(FindRepoRoot(), "data");

        private static string FindRepoRoot()
        {
            DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "data")) &&
                    File.Exists(Path.Combine(dir.FullName, "TradeKit.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the repo root (with data/ and TradeKit.sln) above the test directory.");
        }
    }
}
