using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Self-test of <see cref="DiagonalSetupFinder"/> over the saved <c>data/</c> archive
    /// (DIAGONAL.md §9.1). Every emitted signal must satisfy the hard rules of §4 and the
    /// entry/TP/SL mechanics of §6 — the archive is the oracle, no reference markup needed.
    /// </summary>
    internal class DiagonalSetupFinderTests
    {
        private const string M15_FILE =
            "AUDUSD_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv";

        private const string H1_FILE =
            "AUDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv";

        private const double MIN_DIAGONAL_PENETRATION = 0.05;
        private const double PERCENT_ALLOWANCE_SL = 2;

        private static double Sign(bool isUpDiagonal) => isUpDiagonal ? 1 : -1;

        private static (DiagonalSetupFinder Finder, List<ElliottWaveSignalEventArgs> Signals)
            Run(string file, ITimeFrame timeFrame, double takeProfitRatio = 1.0,
                bool requireWave5Ratio = false, bool requireWave4Ratio = false,
                bool requireInitialMovement = false,
                DiagonalTakeProfitMode takeProfitMode = DiagonalTakeProfitMode.RISK_RATIO,
                bool requireConvergence = true)
        {
            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                takeProfitRatio, requireWave5Ratio, requireWave4Ratio, requireInitialMovement,
                takeProfitMode, requireConvergence);

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return (finder, signals);
        }

        [TestCase(M15_FILE, false, false, false, false)]
        [TestCase(H1_FILE, false, false, false, false)]
        [TestCase(H1_FILE, true, false, false, false)]
        [TestCase(H1_FILE, false, true, false, false)]
        [TestCase(H1_FILE, false, false, true, false)]
        [TestCase(H1_FILE, false, false, false, true)]
        public void Diagonal_EmittedSignals_SatisfyHardRules(
            string file, bool requireWave5Ratio, bool requireWave4Ratio,
            bool requireInitialMovement, bool retraceTakeProfit)
        {
            ITimeFrame timeFrame = file.Contains("_m15_")
                ? TimeFrameHelper.Minute15
                : TimeFrameHelper.Hour1;

            const double takeProfitRatio = 1.5;
            DiagonalTakeProfitMode tpMode = retraceTakeProfit
                ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                : DiagonalTakeProfitMode.RISK_RATIO;
            (DiagonalSetupFinder finder, List<ElliottWaveSignalEventArgs> signals) =
                Run(file, timeFrame, takeProfitRatio, requireWave5Ratio, requireWave4Ratio,
                    requireInitialMovement, tpMode);

            Assert.That(signals, Is.Not.Empty,
                $"No diagonal setups detected in {file}. Funnel: " +
                string.Join(", ", finder.Diag.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}")));

            foreach (ElliottWaveSignalEventArgs s in signals)
            {
                BarPoint[] p = s.WavePoints;
                Assert.That(p, Has.Length.EqualTo(6), "WavePoints must be [0,1,2,3,4,5].");

                // The trade is COUNTER to the diagonal (DIAGONAL.md §3).
                bool isUpDiagonal = s.TakeProfit.Value < s.StopLoss.Value;
                double sgn = Sign(isUpDiagonal);
                string at = $"{p[0].OpenTime:u}";

                for (int i = 1; i < p.Length; i++)
                {
                    Assert.That(p[i].BarIndex, Is.GreaterThanOrEqualTo(p[i - 1].BarIndex),
                        $"{at}: wave points are not chronological.");
                }

                double w1 = Math.Abs(p[1].Value - p[0].Value);
                double w2 = Math.Abs(p[2].Value - p[1].Value);
                double w3 = Math.Abs(p[3].Value - p[2].Value);
                double w4 = Math.Abs(p[4].Value - p[3].Value);
                double w5 = Math.Abs(p[5].Value - p[4].Value);

                // D-W2
                Assert.That(sgn * (p[2].Value - p[0].Value), Is.GreaterThan(0),
                    $"{at}: D-W2 — wave 2 ran past the start of wave 1.");

                // D-W3-PEN
                Assert.That(sgn * (p[3].Value - p[1].Value),
                    Is.GreaterThanOrEqualTo(MIN_DIAGONAL_PENETRATION * w1),
                    $"{at}: D-W3-PEN — wave 3 did not make a new extreme beyond wave 1.");

                // D-CONTRACT-3 / D-CONTRACT-4
                Assert.That(w3, Is.LessThan(w1), $"{at}: D-CONTRACT-3 — |W3| >= |W1|.");
                Assert.That(w4, Is.LessThan(w2), $"{at}: D-CONTRACT-4 — |W4| >= |W2|.");

                // D-CONVERGE — the trendlines 1-3 and 2-4 close (on by default).
                double upperSlope = sgn * (p[3].Value - p[1].Value) /
                                    (p[3].BarIndex - p[1].BarIndex);
                double lowerSlope = sgn * (p[4].Value - p[2].Value) /
                                    (p[4].BarIndex - p[2].BarIndex);
                Assert.That(upperSlope, Is.LessThan(lowerSlope),
                    $"{at}: D-CONVERGE — the trendlines diverge.");

                // D-OVERLAP — the defining feature of a diagonal.
                Assert.That(sgn * (p[4].Value - p[1].Value), Is.LessThan(0),
                    $"{at}: D-OVERLAP — wave 4 does not overlap wave 1 (this is an impulse).");

                // D-W4-2
                Assert.That(sgn * (p[4].Value - p[2].Value), Is.GreaterThan(0),
                    $"{at}: D-W4-2 — wave 4 broke the end of wave 2.");

                // D-W5-BREAK / D-W5-CAP — no truncations, no over-runs.
                Assert.That(sgn * (p[5].Value - p[3].Value), Is.GreaterThan(0),
                    $"{at}: D-W5-BREAK — wave 5 did not break the end of wave 3.");
                Assert.That(w5, Is.LessThan(w3), $"{at}: D-W5-CAP — |W5| >= |W3|.");

                if (requireWave5Ratio)
                {
                    Assert.That(w5, Is.GreaterThanOrEqualTo(0.786 * w3),
                        $"{at}: D-W5-78 — wave 5 is not mature although the option is on.");
                }

                if (requireWave4Ratio)
                {
                    Assert.That(w4, Is.GreaterThanOrEqualTo(0.786 * w2),
                        $"{at}: D-W4-78 — wave 4 is too shallow although the option is on.");
                }

                // §6: the trade is counter to the diagonal.
                double entry = s.Level.Value;
                if (isUpDiagonal)
                {
                    Assert.That(s.StopLoss.Value, Is.GreaterThan(entry), $"{at}: SELL setup with SL below entry.");
                    Assert.That(s.TakeProfit.Value, Is.LessThan(entry), $"{at}: SELL setup with TP above entry.");
                }
                else
                {
                    Assert.That(s.StopLoss.Value, Is.LessThan(entry), $"{at}: BUY setup with SL above entry.");
                    Assert.That(s.TakeProfit.Value, Is.GreaterThan(entry), $"{at}: BUY setup with TP below entry.");
                }

                // §6: SL is the theoretical ceiling of wave 5 — V(4) ± |W3| (+ allowance).
                double slDistance = Math.Abs(s.StopLoss.Value - p[4].Value);
                double allowance = Math.Abs(entry - s.StopLoss.Value) * PERCENT_ALLOWANCE_SL / 100;
                Assert.That(sgn * (s.StopLoss.Value - p[4].Value), Is.GreaterThan(0),
                    $"{at}: SL is on the wrong side of the end of wave 4.");
                Assert.That(slDistance, Is.EqualTo(w3).Within(allowance + 2 * s.StopLoss.Value * 1e-4),
                    $"{at}: SL is not V(4) ± |W3|.");

                // §6: TP keeps the requested R:R exactly (modulo tick rounding), or — in
                // retrace mode — sits at 23.6% of the whole diagonal V(0)→W5.
                double risk = Math.Abs(s.StopLoss.Value - entry);
                double reward = Math.Abs(s.TakeProfit.Value - entry);
                if (retraceTakeProfit)
                {
                    double diagonal = Math.Abs(p[5].Value - p[0].Value);
                    double expectedTp = p[5].Value - sgn * 0.236 * diagonal;
                    Assert.That(s.TakeProfit.Value, Is.EqualTo(expectedTp)
                            .Within(2 * s.TakeProfit.Value * 1e-4),
                        $"{at}: D-TP-236 — TP is not a 23.6% retracement of the diagonal.");
                }
                else
                {
                    Assert.That(reward, Is.EqualTo(takeProfitRatio * risk).Within(risk * 0.02 + 1e-4),
                        $"{at}: TP does not match TakeProfitRatio={takeProfitRatio}.");
                }
            }
        }

        /// <summary>
        /// Funnel diagnostics: where do assembled diagonal candidates die? Writes
        /// <c>reports/diagonal_rejections.md</c> (DIAGONAL.md §9.1). Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_RejectionFunnel_Report()
        {
            string[] files =
            {
                M15_FILE, H1_FILE,
                "EURUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
                "GBPUSD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv"
            };

            var lines = new List<string>
            {
                "# Diagonal setup finder — rejection funnel", string.Empty,
                "Generated by `DiagonalSetupFinderTests.Diagonal_RejectionFunnel_Report`.",
                string.Empty
            };

            string dataDir = FindDataDir();
            foreach (string file in files)
            {
                if (!File.Exists(Path.Combine(dataDir, file)))
                    continue;

                ITimeFrame timeFrame = file.Contains("_m15_")
                    ? TimeFrameHelper.Minute15
                    : TimeFrameHelper.Hour1;

                (DiagonalSetupFinder finder, List<ElliottWaveSignalEventArgs> signals) =
                    Run(file, timeFrame);

                lines.Add($"## {file}");
                lines.Add(string.Empty);
                lines.Add($"- zigzag base period: `{finder.ZigzagPeriod}`");
                lines.Add($"- signals: **{signals.Count}**");
                lines.Add(string.Empty);
                lines.Add("| gate | count |");
                lines.Add("|---|---:|");
                foreach (KeyValuePair<string, int> gate in
                         finder.Diag.OrderByDescending(x => x.Value))
                {
                    lines.Add($"| `{gate.Key}` | {gate.Value} |");
                }

                lines.Add(string.Empty);
            }

            string reportDir = Path.Combine(Directory.GetParent(FindDataDir())!.FullName, "reports");
            Directory.CreateDirectory(reportDir);
            string reportPath = Path.Combine(reportDir, "diagonal_rejections.md");
            File.WriteAllLines(reportPath, lines);
            TestContext.Out.WriteLine($"Report written: {reportPath}");
        }

        /// <summary>
        /// Compares the option axes (DIAGONAL.md §9.1): wave-5 maturity, even contraction of
        /// wave 4, the "initial movement" test for point 0 and the target (fixed R:R vs a
        /// 23.6% retracement of the diagonal, where the R:R floats). Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_ModeComparison_Report()
        {
            foreach (bool converge in new[] { true, false })
            foreach (bool requireInit in new[] { false, true })
            foreach (bool requireW4 in new[] { false, true })
            foreach (bool requireRatio in new[] { false, true })
            {
                // The retrace target ignores TakeProfitRatio, so it is a single extra run.
                foreach (double ratio in new[] { 1.0, 1.5, 2.0, 3.0, 0.0 })
                {
                    bool isRetrace = ratio == 0.0;
                    var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
                    provider.LoadCandles(Path.Combine(FindDataDir(), H1_FILE));

                    var finder = new DiagonalSetupFinder(
                        provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                        isRetrace ? 1.0 : ratio, requireRatio, requireW4, requireInit,
                        isRetrace
                            ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                            : DiagonalTakeProfitMode.RISK_RATIO,
                        converge);

                    int enters = 0, tp = 0, sl = 0;
                    double pendingR = 0, profit = 0, rSum = 0;
                    finder.OnEnter += (_, e) =>
                    {
                        enters++;
                        pendingR = Math.Abs(e.TakeProfit.Value - e.Level.Value) /
                                   Math.Max(1e-9, Math.Abs(e.StopLoss.Value - e.Level.Value));
                        rSum += pendingR;
                    };
                    finder.OnTakeProfit += (_, _) => { tp++; profit += pendingR; };
                    finder.OnStopLoss += (_, _) => { sl++; profit -= 1; };
                    finder.MarkAsInitialized();
                    for (int i = 0; i < provider.Count; i++)
                        finder.CheckBar(provider.GetOpenTime(i));

                    int resolved = tp + sl;
                    double winRate = resolved > 0 ? 100.0 * tp / resolved : 0;
                    double expectancy = resolved > 0 ? profit / resolved : 0;
                    double avgR = enters > 0 ? rSum / enters : 0;
                    TestContext.Out.WriteLine(
                        $"conv={converge,-5} W5={requireRatio,-5} W4={requireW4,-5} " +
                        $"init={requireInit,-5} " +
                        $"tp={(isRetrace ? "23.6%" : $"R{ratio:F1}"),5} enters={enters,4} " +
                        $"avgR={avgR,5:F2} tp={tp,4} sl={sl,4} " +
                        $"win={winRate,5:F1}% expectancy={expectancy,6:F2}R");
                }
            }
        }

        /// <summary>
        /// Walks up from the test working directory to locate the repo <c>data/</c> folder
        /// (the directory next to <c>TradeKit.sln</c>).
        /// </summary>
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
    }
}
