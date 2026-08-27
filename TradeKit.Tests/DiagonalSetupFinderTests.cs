using System.Globalization;
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

        private const double PERCENT_ALLOWANCE_SL = 2;

        private static double Sign(bool isUpDiagonal) => isUpDiagonal ? 1 : -1;

        private static (DiagonalSetupFinder Finder, List<ElliottWaveSignalEventArgs> Signals)
            Run(string file, ITimeFrame timeFrame, double takeProfitRatio = 1.0,
                bool requireWave5Ratio = false, bool requireWave4Ratio = false,
                bool requireInitialDiagonal = false,
                DiagonalTakeProfitMode takeProfitMode = DiagonalTakeProfitMode.RISK_RATIO,
                double minConvergence = 0,
                bool requireWave2Shorter = false,
                double minWave2Retrace = 0,
                double maxWave5SpillRatio = 0)
        {
            var provider = new TestBarsProvider(timeFrame);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                takeProfitRatio, requireWave5Ratio, requireWave4Ratio, requireInitialDiagonal,
                takeProfitMode, minConvergence,
                requireWave2Shorter: requireWave2Shorter,
                minWave2Retrace: minWave2Retrace,
                maxWave5SpillRatio: maxWave5SpillRatio);

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
        [TestCase(H1_FILE, false, false, false, false, true)]
        [TestCase(H1_FILE, false, false, false, false, false, 0.5)]
        [TestCase(H1_FILE, false, false, false, false, false, 0, 0.3)]
        public void Diagonal_EmittedSignals_SatisfyHardRules(
            string file, bool requireWave5Ratio, bool requireWave4Ratio,
            bool requireInitialDiagonal, bool retraceTakeProfit,
            bool requireWave2Shorter = false, double minWave2Retrace = 0,
            double maxWave5SpillRatio = 0)
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
                    requireInitialDiagonal, tpMode, requireWave2Shorter: requireWave2Shorter,
                    minWave2Retrace: minWave2Retrace,
                    maxWave5SpillRatio: maxWave5SpillRatio);

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

                // D-W2-RET
                Assert.That(w2,
                    Is.GreaterThanOrEqualTo(finder.MinWave2Retrace * w1 - 1e-12),
                    $"{at}: D-W2-RET — wave 2 retraces less of wave 1 than required.");

                // D-W3-PEN
                Assert.That(sgn * (p[3].Value - p[1].Value),
                    Is.GreaterThanOrEqualTo(finder.MinWave3Penetration * w1),
                    $"{at}: D-W3-PEN — wave 3 did not make a new extreme beyond wave 1.");

                // D-CONTRACT-3 / D-CONTRACT-4
                Assert.That(w3, Is.LessThan(w1), $"{at}: D-CONTRACT-3 — |W3| >= |W1|.");
                Assert.That(w4, Is.LessThan(w2), $"{at}: D-CONTRACT-4 — |W4| >= |W2|.");

                // D-W4-38 — wave 4 retraces at least 38.2% of wave 3.
                Assert.That(w4,
                    Is.GreaterThanOrEqualTo(finder.MinWave4RetraceW3 * w3 - 1e-12),
                    $"{at}: D-W4-38 — wave 4 retraces less than 38.2% of wave 3.");

                // D-W4-24 — wave 4 reaches at least the 23.6% level of wave 2's range.
                Assert.That(sgn * (p[1].Value - p[4].Value),
                    Is.GreaterThanOrEqualTo(finder.MinWave4Wave2Level * w2 - 1e-12),
                    $"{at}: D-W4-24 — wave 4 stops short of the 23.6% level of wave 2.");

                // D-TIME-24 — wave 4 lasts fewer bars than wave 2.
                Assert.That(p[4].BarIndex - p[3].BarIndex,
                    Is.LessThan(p[2].BarIndex - p[1].BarIndex),
                    $"{at}: D-TIME-24 — wave 4 lasts as long as or longer than wave 2.");

                if (requireWave2Shorter)
                {
                    // D-TIME-12 — wave 2 lasts fewer bars than wave 1.
                    Assert.That(p[2].BarIndex - p[1].BarIndex,
                        Is.LessThan(p[1].BarIndex - p[0].BarIndex),
                        $"{at}: D-TIME-12 — wave 2 lasts as long as or longer than wave 1 " +
                        "although the option is on.");
                }

                // D-CONVERGE — the wedge is at least as closed as MinConvergence demands.
                double ceilSlope = (p[3].Value - p[1].Value) / (p[3].BarIndex - p[1].BarIndex);
                double floorSlope = (p[4].Value - p[2].Value) / (p[4].BarIndex - p[2].BarIndex);
                double widthAt1 =
                    sgn * (p[1].Value - (p[2].Value + floorSlope * (p[1].BarIndex - p[2].BarIndex)));
                double widthAt4 =
                    sgn * ((p[1].Value + ceilSlope * (p[4].BarIndex - p[1].BarIndex)) - p[4].Value);
                Assert.That(widthAt1 / widthAt4 - 1,
                    Is.GreaterThanOrEqualTo(finder.MinConvergence - 1e-9),
                    $"{at}: D-CONVERGE — the wedge converges less than required.");

                // D-INSIDE — the bars of waves 2-4 stay inside the wedge (on by default);
                // isolated-print bars are skipped, exactly as the finder does (§4.4).
                double spill = 0, wedge = 0;
                for (int bar = p[1].BarIndex; bar <= p[4].BarIndex; bar++)
                {
                    if (finder.PrintFilter.IsExcluded(bar))
                        continue;

                    double ceiling = sgn * (p[1].Value + ceilSlope * (bar - p[1].BarIndex));
                    double floorLine = sgn * (p[2].Value + floorSlope * (bar - p[2].BarIndex));
                    double high = sgn * finder.BarsProvider.GetHighPrice(bar);
                    double low = sgn * finder.BarsProvider.GetLowPrice(bar);
                    spill += Math.Max(0, Math.Max(high, low) - ceiling) +
                             Math.Max(0, floorLine - Math.Min(high, low));
                    wedge += ceiling - floorLine;
                }

                Assert.That(spill / wedge, Is.LessThanOrEqualTo(finder.MaxSpillAreaRatio + 1e-9),
                    $"{at}: D-INSIDE — the bars spill out of the wedge.");

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
                "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
                "GBPUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv"
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
            foreach (double minConvergence in new[] { -1.0, 0.0, 0.5, 1.0 })
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
                        minConvergence);

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
                        $"conv={minConvergence,5:F1} W5={requireRatio,-5} W4={requireW4,-5} " +
                        $"init={requireInit,-5} " +
                        $"tp={(isRetrace ? "23.6%" : $"R{ratio:F1}"),5} enters={enters,4} " +
                        $"avgR={avgR,5:F2} tp={tp,4} sl={sl,4} " +
                        $"win={winRate,5:F1}% expectancy={expectancy,6:F2}R");
                }
            }
        }

        /// <summary>
        /// Calibrates the D-INSIDE threshold (DIAGONAL.md §4.3): how much of the wedge area
        /// may the bars of waves 2-4 spend outside the trendlines? Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_SpillThreshold_Report()
        {
            string[] files =
            {
                H1_FILE,
                "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
                "GBPUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv"
            };
            foreach (string file in files)
            {
                if (!File.Exists(Path.Combine(FindDataDir(), file)))
                    continue;

                foreach (double maxSpill in new[] { 0.0, 0.002, 0.003, 0.005, 0.01, 0.02, 0.05 })
                foreach (double ratio in new[] { 1.0, 1.5, 2.0 })
                {
                    var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
                    provider.LoadCandles(Path.Combine(FindDataDir(), file));

                    var finder = new DiagonalSetupFinder(
                        provider, provider.BarSymbol, new EWParams(0, 0.1, 10), ratio,
                        requireWave5Ratio: false, requireWave4Ratio: false,
                        requireInitialDiagonal: false,
                        takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                        minConvergence: 0,
                        requireInsideWedge: maxSpill > 0,
                        maxSpillAreaRatio: maxSpill > 0 ? maxSpill : 1.0);

                    int enters = 0, tp = 0, sl = 0;
                    finder.OnEnter += (_, _) => enters++;
                    finder.OnTakeProfit += (_, _) => tp++;
                    finder.OnStopLoss += (_, _) => sl++;
                    finder.MarkAsInitialized();
                    for (int i = 0; i < provider.Count; i++)
                        finder.CheckBar(provider.GetOpenTime(i));

                    int resolved = tp + sl;
                    double winRate = resolved > 0 ? 100.0 * tp / resolved : 0;
                    double expectancy = resolved > 0 ? (tp * ratio - sl) / resolved : 0;
                    string spillLabel = maxSpill > 0 ? maxSpill.ToString("F3") : "off";
                    TestContext.Out.WriteLine(
                        $"{file[..6]} maxSpill={spillLabel,5} " +
                        $"R:R={ratio:F1} enters={enters,4} tp={tp,4} sl={sl,4} " +
                        $"win={winRate,5:F1}% expectancy={expectancy,6:F2}R");
                }
            }
        }

        /// <summary>
        /// Calibrates the D-INSIDE-5 threshold (DIAGONAL.md §4.3): how much of the (narrow)
        /// wedge corridor may wave 5 spend outside the trendlines? Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_Wave5SpillThreshold_Report()
        {
            string[] files =
            {
                H1_FILE,
                "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
                "GBPUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv"
            };
            foreach (string file in files)
            {
                if (!File.Exists(Path.Combine(FindDataDir(), file)))
                    continue;

                foreach (double maxW5Spill in new[] { 0.0, 0.1, 0.2, 0.3, 0.5, 1.0 })
                foreach (double ratio in new[] { 1.0, 1.5, 2.0 })
                {
                    var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
                    provider.LoadCandles(Path.Combine(FindDataDir(), file));

                    var finder = new DiagonalSetupFinder(
                        provider, provider.BarSymbol, new EWParams(0, 0.1, 10), ratio,
                        maxWave5SpillRatio: maxW5Spill);

                    int enters = 0, tp = 0, sl = 0;
                    finder.OnEnter += (_, _) => enters++;
                    finder.OnTakeProfit += (_, _) => tp++;
                    finder.OnStopLoss += (_, _) => sl++;
                    finder.MarkAsInitialized();
                    for (int i = 0; i < provider.Count; i++)
                        finder.CheckBar(provider.GetOpenTime(i));

                    int resolved = tp + sl;
                    double winRate = resolved > 0 ? 100.0 * tp / resolved : 0;
                    double expectancy = resolved > 0 ? (tp * ratio - sl) / resolved : 0;
                    string label = maxW5Spill > 0 ? maxW5Spill.ToString("F2") : "off";
                    TestContext.Out.WriteLine(
                        $"{file[..6]} maxW5Spill={label,4} " +
                        $"R:R={ratio:F1} enters={enters,4} tp={tp,4} sl={sl,4} " +
                        $"win={winRate,5:F1}% expectancy={expectancy,6:F2}R");
                }
            }
        }

        /// <summary>
        /// Diagnoses one hand-marked diagonal (NZDCAD m5, 2026-08-09..10): evaluates every
        /// §4 gate on the manual skeleton, lists the ladder rungs that actually see those
        /// pivots and dumps the gate the live finder used. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_NzdCadM5_Case_Diagnostics()
        {
            const string file = "NZDCAD_m5_2026-07-09T07-20-00_2026-08-10T16-05-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var live = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            double minPen = live.MinWave3Penetration;
            double maxDur = live.MaxWaveDurationRatio;
            TestContext.Out.WriteLine($"defaults: pen={minPen:F3} dur={maxDur:F1}");

            (DateTime Time, bool IsHigh)[][] markups =
            {
                new[]
                {
                    (new DateTime(2026, 8, 9, 21, 25, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 9, 22, 0, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 10, 3, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 10, 6, 0, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 10, 7, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 10, 7, 45, 0, DateTimeKind.Utc), true)
                },
                new[]
                {
                    (new DateTime(2026, 8, 9, 21, 25, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 9, 22, 0, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 10, 3, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 10, 7, 45, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 10, 8, 20, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 10, 9, 25, 0, DateTimeKind.Utc), true)
                }
            };

            for (int v = 0; v < markups.Length; v++)
            {
                TestContext.Out.WriteLine($"=== markup variant {v + 1} ===");
                BarPoint[] p = markups[v]
                    .Select(x =>
                    {
                        int i = provider.GetIndexByTime(x.Time);
                        double value = x.IsHigh
                            ? provider.GetHighPrice(i)
                            : provider.GetLowPrice(i);
                        return new BarPoint(value, x.Time, provider.TimeFrame, i);
                    })
                    .ToArray();

                int sgn = 1;
                double w1 = Math.Abs(p[1].Value - p[0].Value);
                double w2 = Math.Abs(p[2].Value - p[1].Value);
                double w3 = Math.Abs(p[3].Value - p[2].Value);
                double w4 = Math.Abs(p[4].Value - p[3].Value);
                double w5 = Math.Abs(p[5].Value - p[4].Value);

                for (int i = 0; i < p.Length; i++)
                    TestContext.Out.WriteLine($"  V({i}) {p[i].OpenTime:u} idx={p[i].BarIndex,5} {p[i].Value:F5}");

                TestContext.Out.WriteLine(
                    $"  |W1|={w1:F5} |W2|={w2:F5} |W3|={w3:F5} |W4|={w4:F5} |W5|={w5:F5}");

                void Gate(string rule, bool ok, string detail) =>
                    TestContext.Out.WriteLine($"  {(ok ? "OK  " : "FAIL")} {rule,-14} {detail}");

                Gate("D-W2", sgn * (p[2].Value - p[0].Value) > 0,
                    $"V(2)-V(0)={p[2].Value - p[0].Value:F5}");
                Gate("D-W3-PEN", sgn * (p[3].Value - p[1].Value) >= minPen * w1,
                    $"pen={p[3].Value - p[1].Value:F5} " +
                    $"= {(p[3].Value - p[1].Value) / w1:P2} of |W1|");
                Gate("D-CONTRACT-3", w3 < w1, $"{w3:F5} < {w1:F5}");
                Gate("D-CONTRACT-4", w4 < w2, $"{w4:F5} < {w2:F5}");
                Gate("D-W4-38", w4 >= live.MinWave4RetraceW3 * w3,
                    $"{w4:F5} >= {live.MinWave4RetraceW3 * w3:F5} (38.2% of |W3|)");
                Gate("D-W4-24", sgn * (p[1].Value - p[4].Value) >= live.MinWave4Wave2Level * w2,
                    $"level={sgn * (p[1].Value - p[4].Value) / w2:F3} of |W2| " +
                    $"(need {live.MinWave4Wave2Level:F3})");
                Gate("D-TIME-24",
                    p[4].BarIndex - p[3].BarIndex < p[2].BarIndex - p[1].BarIndex,
                    $"bars(W4)={p[4].BarIndex - p[3].BarIndex} < " +
                    $"bars(W2)={p[2].BarIndex - p[1].BarIndex}");
                Gate("D-OVERLAP", sgn * (p[4].Value - p[1].Value) < 0,
                    $"V(4)-V(1)={p[4].Value - p[1].Value:F5}");
                Gate("D-W4-2", sgn * (p[4].Value - p[2].Value) > 0,
                    $"V(4)-V(2)={p[4].Value - p[2].Value:F5}");
                Gate("D-W5-BREAK", sgn * (p[5].Value - p[3].Value) > 0,
                    $"V(5)-V(3)={p[5].Value - p[3].Value:F5}");
                Gate("D-W5-CAP", w5 < w3, $"{w5:F5} < {w3:F5}");

                double ceilSlope = (p[3].Value - p[1].Value) / (p[3].BarIndex - p[1].BarIndex);
                double floorSlope = (p[4].Value - p[2].Value) / (p[4].BarIndex - p[2].BarIndex);
                double widthAt1 =
                    sgn * (p[1].Value - (p[2].Value + floorSlope * (p[1].BarIndex - p[2].BarIndex)));
                double widthAt4 =
                    sgn * ((p[1].Value + ceilSlope * (p[4].BarIndex - p[1].BarIndex)) - p[4].Value);
                Gate("D-CONVERGE", widthAt1 / widthAt4 - 1 >= 0,
                    $"conv={widthAt1 / widthAt4 - 1:F3}");

                double spill = 0, wedge = 0;
                for (int bar = p[1].BarIndex; bar <= p[4].BarIndex; bar++)
                {
                    if (live.PrintFilter.IsExcluded(bar))
                        continue;

                    double ceiling = p[1].Value + ceilSlope * (bar - p[1].BarIndex);
                    double floorLine = p[2].Value + floorSlope * (bar - p[2].BarIndex);
                    spill += Math.Max(0, provider.GetHighPrice(bar) - ceiling) +
                             Math.Max(0, floorLine - provider.GetLowPrice(bar));
                    wedge += ceiling - floorLine;
                }

                Gate("D-INSIDE", spill / wedge <= live.MaxSpillAreaRatio,
                    $"spill={spill / wedge:F5}");

                for (int w = 3; w < p.Length - 1; w++)
                {
                    double siblingBars = p[w - 2].BarIndex - p[w - 3].BarIndex;
                    double curBars = p[w].BarIndex - p[w - 1].BarIndex;
                    double ratio = Math.Max(curBars / siblingBars, siblingBars / curBars);
                    Gate("D-TIME", ratio <= maxDur,
                        $"W{w} vs W{w - 2}: {curBars}/{siblingBars} -> ratio={ratio:F2}");
                }
            }

            // Which ladder rungs actually see the marked pivots?
            int basePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            TestContext.Out.WriteLine($"=== auto base period = {basePeriod} ===");
            var window = (From: new DateTime(2026, 8, 9, 21, 0, 0, DateTimeKind.Utc),
                To: new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc));

            foreach (double ratio in new[]
                     {
                         0.382, 0.618, 0.786, 1.000, 1.127, 1.272, 1.434, 1.618,
                         1.826, 2.058, 2.321, 2.618, 3.330, 4.236, 5.388, 6.854
                     })
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                string pivots = string.Join(", ", zz.Extrema.Values
                    .Where(x => x.OpenTime >= window.From && x.OpenTime <= window.To)
                    .Select(x => $"{x.OpenTime:HH:mm}"));
                TestContext.Out.WriteLine($"  period={period,4}: {pivots}");
            }

            // What does the live finder do with candidates in the window?
            var gates = new List<string>();
            live.OnGate = (p0, gate) =>
            {
                if (p0.OpenTime >= window.From && p0.OpenTime <= window.To)
                    gates.Add($"{p0.OpenTime:u} -> {gate}");
            };
            var emitted = new List<ElliottWaveSignalEventArgs>();
            live.OnEnter += (_, a) => emitted.Add(a);
            live.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                live.CheckBar(provider.GetOpenTime(i));

            TestContext.Out.WriteLine("=== live finder gates in the window ===");
            foreach (string g in gates.Distinct())
                TestContext.Out.WriteLine($"  {g}");

            TestContext.Out.WriteLine("=== signals in the window ===");
            foreach (ElliottWaveSignalEventArgs s in emitted
                         .Where(x => x.WavePoints[0].OpenTime >= window.From &&
                                     x.WavePoints[0].OpenTime <= window.To))
            {
                TestContext.Out.WriteLine(
                    "  " + string.Join(" | ", s.WavePoints.Select(
                        (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
                TestContext.Out.WriteLine(
                    $"    entry={s.Level.Value:F5} sl={s.StopLoss.Value:F5} tp={s.TakeProfit.Value:F5}");
            }
        }

        /// <summary>
        /// Diagnoses one hand-marked diagonal (EURAUD m5, 2026-07-31..08-03, wave 2 spans the
        /// weekend gap): evaluates every §4 gate on the manual skeleton, lists the ladder rungs
        /// that see those pivots, checks point-0 reachability (MAX_ASSEMBLY_DEPTH) and dumps the
        /// gates the live finder used. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_EurAudM5_Case_Diagnostics()
        {
            const string file = "EURAUD_m5_2026-07-09T07-20-00_2026-08-10T21-15-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var live = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            double minPen = live.MinWave3Penetration;
            double maxDur = live.MaxWaveDurationRatio;
            TestContext.Out.WriteLine(
                $"defaults: pen={minPen:F3} dur={maxDur:F1} " +
                $"minConv={live.MinConvergence:F2} inside={live.RequireInsideWedge} " +
                $"spill={live.MaxSpillAreaRatio:F4}");

            // point0 is a LOW (up diagonal): low,high,low,high,low,high.
            (DateTime Time, bool IsHigh)[] markup =
            {
                (new DateTime(2026, 7, 31, 13, 40, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 7, 31, 14, 10, 0, DateTimeKind.Utc), true),
                (new DateTime(2026, 8, 2, 21, 15, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 8, 2, 22, 0, 0, DateTimeKind.Utc), true),
                (new DateTime(2026, 8, 3, 0, 10, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 8, 3, 2, 30, 0, DateTimeKind.Utc), true)
            };

            TestContext.Out.WriteLine($"=== manual skeleton ===");
            BarPoint[] p = markup
                .Select(x =>
                {
                    int i = provider.GetIndexByTime(x.Time);
                    double value = x.IsHigh
                        ? provider.GetHighPrice(i)
                        : provider.GetLowPrice(i);
                    return new BarPoint(value, x.Time, provider.TimeFrame, i);
                })
                .ToArray();

            int sgn = 1; // up diagonal
            double w1 = Math.Abs(p[1].Value - p[0].Value);
            double w2 = Math.Abs(p[2].Value - p[1].Value);
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double w4 = Math.Abs(p[4].Value - p[3].Value);
            double w5 = Math.Abs(p[5].Value - p[4].Value);

            for (int i = 0; i < p.Length; i++)
                TestContext.Out.WriteLine($"  V({i}) {p[i].OpenTime:u} idx={p[i].BarIndex,5} {p[i].Value:F5}");

            TestContext.Out.WriteLine(
                $"  |W1|={w1:F5} |W2|={w2:F5} |W3|={w3:F5} |W4|={w4:F5} |W5|={w5:F5}");
            TestContext.Out.WriteLine(
                $"  bars: W1={p[1].BarIndex - p[0].BarIndex} W2={p[2].BarIndex - p[1].BarIndex} " +
                $"W3={p[3].BarIndex - p[2].BarIndex} W4={p[4].BarIndex - p[3].BarIndex} " +
                $"W5={p[5].BarIndex - p[4].BarIndex} span0-4={p[4].BarIndex - p[0].BarIndex}");

            void Gate(string rule, bool ok, string detail) =>
                TestContext.Out.WriteLine($"  {(ok ? "OK  " : "FAIL")} {rule,-14} {detail}");

            Gate("D-W2", sgn * (p[2].Value - p[0].Value) > 0,
                $"V(2)-V(0)={p[2].Value - p[0].Value:F5}");
            Gate("D-W3-PEN", sgn * (p[3].Value - p[1].Value) >= minPen * w1,
                $"pen={p[3].Value - p[1].Value:F5} " +
                $"= {(p[3].Value - p[1].Value) / w1:P2} of |W1| (need {minPen:P0})");
            Gate("D-CONTRACT-3", w3 < w1, $"{w3:F5} < {w1:F5}");
            Gate("D-CONTRACT-4", w4 < w2, $"{w4:F5} < {w2:F5}");
            Gate("D-W4-38", w4 >= live.MinWave4RetraceW3 * w3,
                $"{w4:F5} >= {live.MinWave4RetraceW3 * w3:F5} (38.2% of |W3|)");
            Gate("D-W4-24", sgn * (p[1].Value - p[4].Value) >= live.MinWave4Wave2Level * w2,
                $"level={sgn * (p[1].Value - p[4].Value) / w2:F3} of |W2| " +
                $"(need {live.MinWave4Wave2Level:F3})");
            Gate("D-TIME-24",
                p[4].BarIndex - p[3].BarIndex < p[2].BarIndex - p[1].BarIndex,
                $"bars(W4)={p[4].BarIndex - p[3].BarIndex} < " +
                $"bars(W2)={p[2].BarIndex - p[1].BarIndex}");
            Gate("D-OVERLAP", sgn * (p[4].Value - p[1].Value) < 0,
                $"V(4)-V(1)={p[4].Value - p[1].Value:F5}");
            Gate("D-W4-2", sgn * (p[4].Value - p[2].Value) > 0,
                $"V(4)-V(2)={p[4].Value - p[2].Value:F5}");
            Gate("D-W5-BREAK", sgn * (p[5].Value - p[3].Value) > 0,
                $"V(5)-V(3)={p[5].Value - p[3].Value:F5}");
            Gate("D-W5-CAP", w5 < w3, $"{w5:F5} < {w3:F5}");

            double ceilSlope = (p[3].Value - p[1].Value) / (p[3].BarIndex - p[1].BarIndex);
            double floorSlope = (p[4].Value - p[2].Value) / (p[4].BarIndex - p[2].BarIndex);
            double widthAt1 =
                sgn * (p[1].Value - (p[2].Value + floorSlope * (p[1].BarIndex - p[2].BarIndex)));
            double widthAt4 =
                sgn * ((p[1].Value + ceilSlope * (p[4].BarIndex - p[1].BarIndex)) - p[4].Value);
            Gate("D-CONVERGE", widthAt1 / widthAt4 - 1 >= live.MinConvergence,
                $"conv={widthAt1 / widthAt4 - 1:F3} (need >= {live.MinConvergence:F2})");

            double spill = 0, wedge = 0;
            for (int bar = p[1].BarIndex; bar <= p[4].BarIndex; bar++)
            {
                if (live.PrintFilter.IsExcluded(bar))
                    continue;

                double ceiling = p[1].Value + ceilSlope * (bar - p[1].BarIndex);
                double floorLine = p[2].Value + floorSlope * (bar - p[2].BarIndex);
                spill += Math.Max(0, provider.GetHighPrice(bar) - ceiling) +
                         Math.Max(0, floorLine - provider.GetLowPrice(bar));
                wedge += ceiling - floorLine;
            }

            Gate("D-INSIDE", spill / wedge <= live.MaxSpillAreaRatio,
                $"spill={spill / wedge:F5} (limit {live.MaxSpillAreaRatio:F4})");

            for (int w = 3; w < p.Length - 1; w++)
            {
                double siblingBars = p[w - 2].BarIndex - p[w - 3].BarIndex;
                double curBars = p[w].BarIndex - p[w - 1].BarIndex;
                double ratio = Math.Max(curBars / siblingBars, siblingBars / curBars);
                Gate("D-TIME", ratio <= maxDur,
                    $"W{w} vs W{w - 2}: {curBars}/{siblingBars} -> ratio={ratio:F2}");
            }

            // Which ladder rungs actually see the marked pivots, and is point 0 reachable?
            int basePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            TestContext.Out.WriteLine($"=== auto base period = {basePeriod} ===");
            var window = (From: new DateTime(2026, 7, 31, 13, 0, 0, DateTimeKind.Utc),
                To: new DateTime(2026, 8, 3, 3, 0, 0, DateTimeKind.Utc));

            int[] markedIdx = p.Select(x => x.BarIndex).ToArray();
            foreach (double ratio in new[]
                     {
                         0.382, 0.618, 0.786, 1.000, 1.127, 1.272, 1.434, 1.618,
                         1.826, 2.058, 2.321, 2.618, 3.330, 4.236, 5.388, 6.854
                     })
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                var inWindow = zz.Extrema.Values
                    .Where(x => x.OpenTime >= window.From && x.OpenTime <= window.To)
                    .OrderBy(x => x.OpenTime)
                    .ToList();

                string pivots = string.Join(", ", inWindow.Select(x => $"{x.OpenTime:MM-dd HH:mm}"));

                // How many pivots between point0 and point4 at this rung (assembly-depth check)?
                int between = inWindow.Count(x =>
                    x.BarIndex > markedIdx[0] && x.BarIndex < markedIdx[4]);
                string hit = string.Join("", markedIdx
                    .Select(mi => inWindow.Any(x => x.BarIndex == mi) ? "#" : "."));

                TestContext.Out.WriteLine(
                    $"  period={period,4} pivots0to4={between,3} hit0..5={hit} | {pivots}");
            }

            // What does the live finder do with candidates in the window?
            var gates = new List<string>();
            live.OnGate = (p0, gate) =>
            {
                if (p0.OpenTime >= window.From && p0.OpenTime <= window.To)
                    gates.Add($"{p0.OpenTime:u} -> {gate}");
            };
            var emitted = new List<ElliottWaveSignalEventArgs>();
            live.OnEnter += (_, a) => emitted.Add(a);
            live.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                live.CheckBar(provider.GetOpenTime(i));

            TestContext.Out.WriteLine("=== live finder gates in the window ===");
            foreach (string g in gates.Distinct())
                TestContext.Out.WriteLine($"  {g}");

            TestContext.Out.WriteLine("=== signals in the window ===");
            foreach (ElliottWaveSignalEventArgs s in emitted
                         .Where(x => x.WavePoints[0].OpenTime >= window.From &&
                                     x.WavePoints[0].OpenTime <= window.To))
            {
                TestContext.Out.WriteLine(
                    "  " + string.Join(" | ", s.WavePoints.Select(
                        (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
                TestContext.Out.WriteLine(
                    $"    entry={s.Level.Value:F5} sl={s.StopLoss.Value:F5} tp={s.TakeProfit.Value:F5}");
            }

            // Parameter sweep: does any config emit a signal anchored at this point 0?
            TestContext.Out.WriteLine("=== parameter sweep (signal anchored at 07-31 13:40) ===");
            DateTime target0 = p[0].OpenTime;
            foreach (double minConv in new[] { -1.0, 0.0 })
            foreach (double spillLim in new[] { 0.005, 0.05, 1.0 })
            foreach (double pen in new[] { 0.0, 0.03 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    takeProfitRatio: 1.0,
                    takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                    minConvergence: minConv,
                    requireInsideWedge: spillLim < 1.0,
                    maxSpillAreaRatio: spillLim,
                    minWave3Penetration: pen);

                ElliottWaveSignalEventArgs? found = null;
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints[0].OpenTime == target0)
                        found = a;
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  conv={minConv,4:F1} spill={spillLim,5:F3} pen={pen:F2} -> " +
                    (found == null
                        ? "not found"
                        : $"FOUND entry={found.Level.Value:F5} " +
                          $"sl={found.StopLoss.Value:F5} tp={found.TakeProfit.Value:F5}"));
            }

            // Tolerance sweep: with every threshold gate disabled (conv=-1 disables
            // D-CONVERGE, spillLim=1.0 disables D-INSIDE, pen=0 disables D-W3-PEN), does ANY
            // greedy-merge pullback tolerance carve the hand-marked skeleton? This isolates
            // the segmentation step. Prediction: NO — wave 1 needs tol<=0.543 to end at 14:10
            // while wave 2 needs tol>=0.862 to survive the 86% Friday bounce.
            TestContext.Out.WriteLine("=== wavePullbackTol sweep (all threshold gates off) ===");
            foreach (double tol in new[] { 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.86, 0.90, 1.00, 1.20, 1.50 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    takeProfitRatio: 1.0,
                    takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                    minConvergence: -1.0,
                    requireInsideWedge: false,
                    maxSpillAreaRatio: 1.0,
                    minWave3Penetration: 0.0,
                    wavePullbackTol: tol);

                ElliottWaveSignalEventArgs? found = null;
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints[0].OpenTime == target0)
                        found = a;
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  tol={tol,4:F2} -> " +
                    (found == null
                        ? "not found"
                        : $"FOUND entry={found.Level.Value:F5} " +
                          $"sl={found.StopLoss.Value:F5} tp={found.TakeProfit.Value:F5}"));
            }
        }

        /// <summary>
        /// Diagnoses one hand-marked diagonal (GBPNZD m5, 2026-07-28 21:25 → 07-29 12:20):
        /// evaluates every §4 gate on the manual skeleton, lists the ladder rungs that see
        /// those pivots, checks point-0 reachability (MAX_ASSEMBLY_DEPTH) and dumps the gates
        /// the live finder used. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_GbpNzdM5_Case_Diagnostics()
        {
            const string file = "GBPNZD_m5_2026-07-09T07-20-00_2026-08-10T23-55-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var live = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            double minPen = live.MinWave3Penetration;
            double maxDur = live.MaxWaveDurationRatio;
            TestContext.Out.WriteLine(
                $"defaults: pen={minPen:F3} dur={maxDur:F1} " +
                $"minConv={live.MinConvergence:F2} inside={live.RequireInsideWedge} " +
                $"spill={live.MaxSpillAreaRatio:F4}");

            // point0 is a LOW (up diagonal): low,high,low,high,low,high.
            (DateTime Time, bool IsHigh)[] markup =
            {
                (new DateTime(2026, 7, 28, 21, 25, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 7, 29, 3, 5, 0, DateTimeKind.Utc), true),
                (new DateTime(2026, 7, 29, 6, 0, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 7, 29, 8, 45, 0, DateTimeKind.Utc), true),
                (new DateTime(2026, 7, 29, 11, 25, 0, DateTimeKind.Utc), false),
                (new DateTime(2026, 7, 29, 12, 20, 0, DateTimeKind.Utc), true)
            };

            TestContext.Out.WriteLine($"=== manual skeleton ===");
            BarPoint[] p = markup
                .Select(x =>
                {
                    int i = provider.GetIndexByTime(x.Time);
                    double value = x.IsHigh
                        ? provider.GetHighPrice(i)
                        : provider.GetLowPrice(i);
                    return new BarPoint(value, x.Time, provider.TimeFrame, i);
                })
                .ToArray();

            int sgn = 1; // up diagonal
            double w1 = Math.Abs(p[1].Value - p[0].Value);
            double w2 = Math.Abs(p[2].Value - p[1].Value);
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double w4 = Math.Abs(p[4].Value - p[3].Value);
            double w5 = Math.Abs(p[5].Value - p[4].Value);

            for (int i = 0; i < p.Length; i++)
                TestContext.Out.WriteLine($"  V({i}) {p[i].OpenTime:u} idx={p[i].BarIndex,5} {p[i].Value:F5}");

            TestContext.Out.WriteLine(
                $"  |W1|={w1:F5} |W2|={w2:F5} |W3|={w3:F5} |W4|={w4:F5} |W5|={w5:F5}");
            TestContext.Out.WriteLine(
                $"  bars: W1={p[1].BarIndex - p[0].BarIndex} W2={p[2].BarIndex - p[1].BarIndex} " +
                $"W3={p[3].BarIndex - p[2].BarIndex} W4={p[4].BarIndex - p[3].BarIndex} " +
                $"W5={p[5].BarIndex - p[4].BarIndex} span0-4={p[4].BarIndex - p[0].BarIndex}");

            void Gate(string rule, bool ok, string detail) =>
                TestContext.Out.WriteLine($"  {(ok ? "OK  " : "FAIL")} {rule,-14} {detail}");

            Gate("D-W2", sgn * (p[2].Value - p[0].Value) > 0,
                $"V(2)-V(0)={p[2].Value - p[0].Value:F5}");
            Gate("D-W3-PEN", sgn * (p[3].Value - p[1].Value) >= minPen * w1,
                $"pen={p[3].Value - p[1].Value:F5} " +
                $"= {(p[3].Value - p[1].Value) / w1:P2} of |W1| (need {minPen:P0})");
            Gate("D-CONTRACT-3", w3 < w1, $"{w3:F5} < {w1:F5}");
            Gate("D-CONTRACT-4", w4 < w2, $"{w4:F5} < {w2:F5}");
            Gate("D-W4-38", w4 >= live.MinWave4RetraceW3 * w3,
                $"{w4:F5} >= {live.MinWave4RetraceW3 * w3:F5} (38.2% of |W3|)");
            Gate("D-W4-24", sgn * (p[1].Value - p[4].Value) >= live.MinWave4Wave2Level * w2,
                $"level={sgn * (p[1].Value - p[4].Value) / w2:F3} of |W2| " +
                $"(need {live.MinWave4Wave2Level:F3})");
            Gate("D-TIME-24",
                p[4].BarIndex - p[3].BarIndex < p[2].BarIndex - p[1].BarIndex,
                $"bars(W4)={p[4].BarIndex - p[3].BarIndex} < " +
                $"bars(W2)={p[2].BarIndex - p[1].BarIndex}");
            Gate("D-OVERLAP", sgn * (p[4].Value - p[1].Value) < 0,
                $"V(4)-V(1)={p[4].Value - p[1].Value:F5}");
            Gate("D-W4-2", sgn * (p[4].Value - p[2].Value) > 0,
                $"V(4)-V(2)={p[4].Value - p[2].Value:F5}");
            Gate("D-W5-BREAK", sgn * (p[5].Value - p[3].Value) > 0,
                $"V(5)-V(3)={p[5].Value - p[3].Value:F5}");
            Gate("D-W5-CAP", w5 < w3, $"{w5:F5} < {w3:F5}");

            double ceilSlope = (p[3].Value - p[1].Value) / (p[3].BarIndex - p[1].BarIndex);
            double floorSlope = (p[4].Value - p[2].Value) / (p[4].BarIndex - p[2].BarIndex);
            double widthAt1 =
                sgn * (p[1].Value - (p[2].Value + floorSlope * (p[1].BarIndex - p[2].BarIndex)));
            double widthAt4 =
                sgn * ((p[1].Value + ceilSlope * (p[4].BarIndex - p[1].BarIndex)) - p[4].Value);
            Gate("D-CONVERGE", widthAt1 / widthAt4 - 1 >= live.MinConvergence,
                $"conv={widthAt1 / widthAt4 - 1:F3} (need >= {live.MinConvergence:F2})");

            double spill = 0, wedge = 0;
            for (int bar = p[1].BarIndex; bar <= p[4].BarIndex; bar++)
            {
                if (live.PrintFilter.IsExcluded(bar))
                    continue;

                double ceiling = p[1].Value + ceilSlope * (bar - p[1].BarIndex);
                double floorLine = p[2].Value + floorSlope * (bar - p[2].BarIndex);
                spill += Math.Max(0, provider.GetHighPrice(bar) - ceiling) +
                         Math.Max(0, floorLine - provider.GetLowPrice(bar));
                wedge += ceiling - floorLine;
            }

            Gate("D-INSIDE", spill / wedge <= live.MaxSpillAreaRatio,
                $"spill={spill / wedge:F5} (limit {live.MaxSpillAreaRatio:F4})");

            for (int w = 3; w < p.Length - 1; w++)
            {
                double siblingBars = p[w - 2].BarIndex - p[w - 3].BarIndex;
                double curBars = p[w].BarIndex - p[w - 1].BarIndex;
                double ratio = Math.Max(curBars / siblingBars, siblingBars / curBars);
                Gate("D-TIME", ratio <= maxDur,
                    $"W{w} vs W{w - 2}: {curBars}/{siblingBars} -> ratio={ratio:F2}");
            }

            // Which ladder rungs actually see the marked pivots, and is point 0 reachable?
            int basePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            TestContext.Out.WriteLine($"=== auto base period = {basePeriod} ===");
            var window = (From: new DateTime(2026, 7, 28, 21, 0, 0, DateTimeKind.Utc),
                To: new DateTime(2026, 7, 29, 13, 0, 0, DateTimeKind.Utc));

            int[] markedIdx = p.Select(x => x.BarIndex).ToArray();
            foreach (double ratio in new[]
                     {
                         0.382, 0.618, 0.786, 1.000, 1.127, 1.272, 1.434, 1.618,
                         1.826, 2.058, 2.321, 2.618, 3.330, 4.236, 5.388, 6.854
                     })
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                var inWindow = zz.Extrema.Values
                    .Where(x => x.OpenTime >= window.From && x.OpenTime <= window.To)
                    .OrderBy(x => x.OpenTime)
                    .ToList();

                string pivots = string.Join(", ", inWindow.Select(x => $"{x.OpenTime:MM-dd HH:mm}"));

                // How many pivots between point0 and point4 at this rung (assembly-depth check)?
                int between = inWindow.Count(x =>
                    x.BarIndex > markedIdx[0] && x.BarIndex < markedIdx[4]);
                string hit = string.Join("", markedIdx
                    .Select(mi => inWindow.Any(x => x.BarIndex == mi) ? "#" : "."));

                TestContext.Out.WriteLine(
                    $"  period={period,4} pivots0to4={between,3} hit0..5={hit} | {pivots}");
            }

            // What does the live finder do with candidates in the window?
            var gates = new List<string>();
            live.OnGate = (p0, gate) =>
            {
                if (p0.OpenTime >= window.From && p0.OpenTime <= window.To)
                    gates.Add($"{p0.OpenTime:u} -> {gate}");
            };
            var emitted = new List<ElliottWaveSignalEventArgs>();
            live.OnEnter += (_, a) => emitted.Add(a);
            live.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                live.CheckBar(provider.GetOpenTime(i));

            TestContext.Out.WriteLine("=== live finder gates in the window ===");
            foreach (string g in gates.Distinct())
                TestContext.Out.WriteLine($"  {g}");

            TestContext.Out.WriteLine("=== signals in the window ===");
            foreach (ElliottWaveSignalEventArgs s in emitted
                         .Where(x => x.WavePoints[0].OpenTime >= window.From &&
                                     x.WavePoints[0].OpenTime <= window.To))
            {
                TestContext.Out.WriteLine(
                    "  " + string.Join(" | ", s.WavePoints.Select(
                        (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
                TestContext.Out.WriteLine(
                    $"    entry={s.Level.Value:F5} sl={s.StopLoss.Value:F5} tp={s.TakeProfit.Value:F5}");
            }

            // Parameter sweep: does any config emit a signal anchored at this point 0?
            TestContext.Out.WriteLine("=== parameter sweep (signal anchored at 07-28 21:25) ===");
            DateTime target0 = p[0].OpenTime;
            foreach (double minConv in new[] { -1.0, 0.0 })
            foreach (double spillLim in new[] { 0.005, 0.05, 1.0 })
            foreach (double pen in new[] { 0.0, 0.03 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    takeProfitRatio: 1.0,
                    takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                    minConvergence: minConv,
                    requireInsideWedge: spillLim < 1.0,
                    maxSpillAreaRatio: spillLim,
                    minWave3Penetration: pen);

                ElliottWaveSignalEventArgs? found = null;
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints[0].OpenTime == target0)
                        found = a;
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  conv={minConv,4:F1} spill={spillLim,5:F3} pen={pen:F2} -> " +
                    (found == null
                        ? "not found"
                        : $"FOUND entry={found.Level.Value:F5} " +
                          $"sl={found.StopLoss.Value:F5} tp={found.TakeProfit.Value:F5}"));
                if (found != null)
                    TestContext.Out.WriteLine(
                        "    " + string.Join(" | ", found.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
            }

            // Tolerance sweep: with every threshold gate disabled, does ANY greedy-merge
            // pullback tolerance carve the hand-marked skeleton? This isolates the
            // segmentation step from the validation gates.
            TestContext.Out.WriteLine("=== wavePullbackTol sweep (all threshold gates off) ===");
            foreach (double tol in new[] { 0.30, 0.40, 0.42, 0.43, 0.44, 0.46, 0.50, 0.60, 0.70, 0.80, 0.90, 1.00 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    takeProfitRatio: 1.0,
                    takeProfitMode: DiagonalTakeProfitMode.RISK_RATIO,
                    minConvergence: -1.0,
                    requireInsideWedge: false,
                    maxSpillAreaRatio: 1.0,
                    minWave3Penetration: 0.0,
                    wavePullbackTol: tol);

                ElliottWaveSignalEventArgs? found = null;
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints[0].OpenTime == target0)
                        found = a;
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  tol={tol,4:F2} -> " +
                    (found == null
                        ? "not found"
                        : $"FOUND entry={found.Level.Value:F5} " +
                          $"sl={found.StopLoss.Value:F5} tp={found.TakeProfit.Value:F5}"));
                if (found != null)
                    TestContext.Out.WriteLine(
                        "    " + string.Join(" | ", found.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
            }
        }

        /// <summary>
        /// Calibrates the two hard thresholds that are not EW rules but sanity guards —
        /// D-W3-PEN (<c>MinWave3Penetration</c>) and D-TIME (<c>MaxWaveDurationRatio</c>).
        /// Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_PenetrationAndDurationThresholds_Report()
        {
            (string File, ITimeFrame Tf)[] files =
            {
                (H1_FILE, TimeFrameHelper.Hour1),
                (M15_FILE, TimeFrameHelper.Minute15),
                ("EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv", TimeFrameHelper.Hour1),
                ("GBPUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv", TimeFrameHelper.Hour1)
            };

            foreach ((string file, ITimeFrame tf) in files)
            {
                if (!File.Exists(Path.Combine(FindDataDir(), file)))
                    continue;

                var provider = new TestBarsProvider(tf);
                provider.LoadCandles(Path.Combine(FindDataDir(), file));

                foreach (double penetration in new[] { 0.0, 0.01, 0.03, 0.05, 0.1 })
                foreach (double duration in new[] { 4.0, 6.0, 8.0, 12.0, 1e9 })
                {
                    var finder = new DiagonalSetupFinder(
                        provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                        takeProfitRatio: 1.5,
                        minWave3Penetration: penetration,
                        maxWaveDurationRatio: duration);

                    int enters = 0, tp = 0, sl = 0;
                    finder.OnEnter += (_, _) => enters++;
                    finder.OnTakeProfit += (_, _) => tp++;
                    finder.OnStopLoss += (_, _) => sl++;
                    finder.MarkAsInitialized();
                    for (int i = 0; i < provider.Count; i++)
                        finder.CheckBar(provider.GetOpenTime(i));

                    int resolved = tp + sl;
                    double winRate = resolved > 0 ? 100.0 * tp / resolved : 0;
                    double expectancy = resolved > 0 ? (tp * 1.5 - sl) / resolved : 0;
                    TestContext.Out.WriteLine(
                        $"{file[..6]} {tf.ShortName,4} pen={penetration:F2} " +
                        $"dur={(duration > 1e6 ? "off" : duration.ToString("F0")),3} " +
                        $"enters={enters,4} tp={tp,4} sl={sl,4} " +
                        $"win={winRate,5:F1}% expectancy={expectancy,6:F2}R");
                }
            }
        }

        /// <summary>
        /// Regression (DIAGONAL.md §9.9): the EURAUD m5 diagonal anchored at 2026-07-31 13:40
        /// whose wave 2 spans the weekend gap is invisible to single-scale carving — no single
        /// pullback tolerance separates V(1) from the mid-wave-2 Friday bounce. It must be
        /// recovered by the cross-scale assembly (waves 0-1 on the event rung, wave 2's end on
        /// a coarser rung, waves 3-4 on whichever rung resolves them). Asserts the finder emits
        /// an entered signal with point 0 at that bar and wave 2 ending 2026-08-02 21:15.
        /// </summary>
        [Test]
        public void Diagonal_EurAudM5_WeekendWave2_FoundByCrossScale()
        {
            const string file = "EURAUD_m5_2026-07-09T07-20-00_2026-08-10T21-15-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            DateTime p0Time = new DateTime(2026, 7, 31, 13, 40, 0, DateTimeKind.Utc);
            DateTime p2Time = new DateTime(2026, 8, 2, 21, 15, 0, DateTimeKind.Utc);

            ElliottWaveSignalEventArgs? signal = signals.FirstOrDefault(s =>
                s.WavePoints[0].OpenTime == p0Time);

            Assert.That(signal, Is.Not.Null,
                "Cross-scale assembly did not fire for the weekend-wave-2 diagonal. Funnel: " +
                string.Join(", ", finder.Diag.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}")));

            BarPoint[] p = signal!.WavePoints;
            Assert.That(p[2].OpenTime, Is.EqualTo(p2Time),
                "Wave 2 must end at the Sunday session low (2026-08-02 21:15).");
            Assert.That(p[1].OpenTime, Is.EqualTo(
                new DateTime(2026, 7, 31, 14, 10, 0, DateTimeKind.Utc)),
                "Wave 1 must be the Friday rally leg.");
        }

        /// <summary>
        /// GBPNZD m5, 2026-07-28 21:25 → 07-29 12:20: wave 1 ends at a DOUBLE TOP — the
        /// 02:00 and 03:05 highs are exactly equal (2.30095). On the fine rungs that
        /// resolve waves 3-4 (08:45/11:25) the greedy merge keeps the FIRST touch (02:00)
        /// as the wave-1 extreme, shifting the whole carve; the coarse rungs that see the
        /// second touch do not resolve 08:45/11:25. The cross-scale fallback must re-carve
        /// wave 1 from point 0 on a coarse rung (→ 03:05) and waves 3-4 on a fine rung.
        /// </summary>
        [Test]
        public void Diagonal_GbpNzdM5_DoubleTopWave1_FoundByCrossScale()
        {
            const string file = "GBPNZD_m5_2026-07-09T07-20-00_2026-08-10T23-55-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            DateTime p0Time = new DateTime(2026, 7, 28, 21, 25, 0, DateTimeKind.Utc);

            ElliottWaveSignalEventArgs? signal = signals.FirstOrDefault(s =>
                (s.WavePoints[0].OpenTime - p0Time).Duration() <= TimeSpan.FromSeconds(5));

            Assert.That(signal, Is.Not.Null,
                "Cross-scale assembly did not fire for the double-top-wave-1 diagonal. Funnel: " +
                string.Join(", ", finder.Diag.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}")));

            BarPoint[] p = signal!.WavePoints;
            (DateTime Time, double Value)[] expected =
            {
                (new DateTime(2026, 7, 28, 21, 25, 0, DateTimeKind.Utc), 2.29240),
                (new DateTime(2026, 7, 29, 3, 5, 0, DateTimeKind.Utc), 2.30095),
                (new DateTime(2026, 7, 29, 6, 0, 0, DateTimeKind.Utc), 2.29577),
                (new DateTime(2026, 7, 29, 8, 45, 0, DateTimeKind.Utc), 2.30138),
                (new DateTime(2026, 7, 29, 11, 25, 0, DateTimeKind.Utc), 2.29897)
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(Math.Abs((p[i].OpenTime - expected[i].Time).TotalSeconds),
                    Is.LessThanOrEqualTo(5),
                    $"V({i}) must be at {expected[i].Time:u} (double top: wave 1 ends at the " +
                    $"SECOND 2.30095 touch, waves 3-4 resolve at 08:45/11:25). " +
                    $"Actual V({i}) = {p[i].OpenTime:u} {p[i].Value:F5}.");
                Assert.That(p[i].Value, Is.EqualTo(expected[i].Value).Within(1e-5),
                    $"V({i}) price mismatch at {p[i].OpenTime:u}.");
            }
        }

        /// <summary>
        /// CADJPY m5, 2026-08-19 12:35 → 15:25: wave 1 is a spike (|W1| = 0.602) and wave 2
        /// gives back only 34% of it — less than the greedy merge tolerance — so the single
        /// pass swallows waves 2-4 into wave 1 and the wedge anchored at 12:35 falls apart
        /// (the detector used to anchor it one pivot later, at 13:10). The shorter-wave-1
        /// fallback must recover the full skeleton.
        /// </summary>
        [Test]
        public void Diagonal_CadJpyM5_ShallowWave2_FoundByShorterWave1()
        {
            const string file = "CADJPY_m5_2026-07-17T04-20-00_2026-08-20T23-00-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            DateTime p0Time = new DateTime(2026, 8, 19, 12, 35, 0, DateTimeKind.Utc);
            ElliottWaveSignalEventArgs? signal = signals.FirstOrDefault(s =>
                (s.WavePoints[0].OpenTime - p0Time).Duration() <= TimeSpan.FromSeconds(5));

            Assert.That(signal, Is.Not.Null,
                "The shallow-wave-2 diagonal was not recovered. Funnel: " +
                string.Join(", ", finder.Diag.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}")));

            (DateTime Time, double Value)[] expected =
            {
                (p0Time, 114.084),
                (new DateTime(2026, 8, 19, 14, 5, 0, DateTimeKind.Utc), 114.686),
                (new DateTime(2026, 8, 19, 14, 35, 0, DateTimeKind.Utc), 114.480),
                (new DateTime(2026, 8, 19, 15, 10, 0, DateTimeKind.Utc), 114.726),
                (new DateTime(2026, 8, 19, 15, 20, 0, DateTimeKind.Utc), 114.607)
            };

            BarPoint[] p = signal!.WavePoints;
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(Math.Abs((p[i].OpenTime - expected[i].Time).TotalSeconds),
                    Is.LessThanOrEqualTo(5),
                    $"V({i}) must be at {expected[i].Time:u}; actual {p[i].OpenTime:u}.");
                Assert.That(p[i].Value, Is.EqualTo(expected[i].Value).Within(1e-5),
                    $"V({i}) price mismatch at {p[i].OpenTime:u}.");
            }
        }

        /// <summary>
        /// EURCHF m5, 2026-08-19 21:10 → 08-21 03:30 (DIAGONAL.md §9.13): the detector
        /// used to build wave 4 on an isolated print — a zero-range bar at 2026-08-20 21:00
        /// (0.93288) gapped ~18 points below the previous bar and ~15 points below the
        /// next one, fully retraced on the very next candle and never revisited again.
        /// The filter must confirm the bar as a print and no emitted signal may use it.
        /// </summary>
        [Test]
        public void Diagonal_EurChfM5_IsolatedPrintWave4_Excluded()
        {
            const string file = "EURCHF_m5_2026-08-14T07-20-00_2026-08-21T20-55-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            // The zero-range 21:00 bar with gaps on both sides is a confirmed print.
            DateTime spikeTime = new DateTime(2026, 8, 20, 21, 0, 0, DateTimeKind.Utc);
            int spikeBar = provider.GetIndexByTime(spikeTime);
            Assert.That(finder.PrintFilter.IsExcluded(spikeBar), Is.True,
                "The isolated print at 2026-08-20 21:00 was not confirmed. Segments: " +
                string.Join(", ", finder.PrintFilter.Segments.Select(s =>
                    $"[{provider.GetOpenTime(s.StartBar):u}..{provider.GetOpenTime(s.EndBar):u}]")));

            IsolatedPrintSegment segment = finder.PrintFilter.Segments
                .First(s => s.StartBar <= spikeBar && spikeBar <= s.EndBar);
            Assert.That(segment.IsDown, Is.True, "The EURCHF print hangs below both neighbors.");

            // No signal may stand on the print — neither as wave 4 nor as any other point.
            foreach (ElliottWaveSignalEventArgs s in signals)
            {
                Assert.That(s.WavePoints.Any(x => x.BarIndex == spikeBar), Is.False,
                    $"Signal {s.WavePoints[0].OpenTime:u} uses the isolated print " +
                    "2026-08-20 21:00 as a wave point.");
            }

            // In particular, the faulty diagonal V(0)=08-19 21:10 … V(4)=21:00 must be gone.
            DateTime badPoint0 = new DateTime(2026, 8, 19, 21, 10, 0, DateTimeKind.Utc);
            Assert.That(signals.Any(s =>
                    (s.WavePoints[0].OpenTime - badPoint0).Duration() <= TimeSpan.FromSeconds(5) &&
                    s.WavePoints[4].OpenTime == spikeTime),
                Is.False,
                "The diagonal anchored at 2026-08-19 21:10 still ends wave 4 on the print.");
        }

        /// <summary>
        /// DIAGONAL.md §6.6: with <c>Wave3RetraceRatio</c> set, the target is a retrace of
        /// |W3| from the running extreme of wave 5 — <c>TP = W5 ∓ ratio·|W3|</c> — and it
        /// overrides both the fixed R:R and the 23.6%-of-the-diagonal mode.
        /// </summary>
        [Test]
        public void Diagonal_Wave3RetraceRatio_PlacesTargetAtW3Fibo()
        {
            const string file = "USDJPY_m5_2026-07-16T07-05-00_2026-08-20T21-15-00.csv";
            const double ratio = 0.382;
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                takeProfitMode: DiagonalTakeProfitMode.DIAGONAL_RETRACE,
                wave3RetraceRatio: ratio);

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            DateTime p0Time = new DateTime(2026, 8, 19, 22, 5, 0, DateTimeKind.Utc);
            ElliottWaveSignalEventArgs? signal = signals.FirstOrDefault(s =>
                (s.WavePoints[0].OpenTime - p0Time).Duration() <= TimeSpan.FromSeconds(5));

            Assert.That(signal, Is.Not.Null, "No signal for the USDJPY diagonal.");

            BarPoint[] p = signal!.WavePoints;
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double expected = p[5].Value - ratio * w3;

            Assert.That(signal.TakeProfit.Value, Is.EqualTo(expected).Within(1e-4),
                $"TP must sit at the {ratio:P1} retrace of |W3| = {w3:F5} " +
                $"below the wave-5 extreme {p[5].Value:F5}.");
        }

        /// <summary>
        /// GBPCHF m5, 2026-08-19 21:00 → 08-20 15:25 (DIAGONAL.md §6.5, §9.15): the trigger
        /// candle offers an R:R of only ≈0.5, so a stricter <c>MinRiskRewardRatio</c> parks the
        /// candidate. Wave 5 then keeps climbing for another day, which both improves the ratio
        /// and — since D-INSIDE-5 used to be measured up to the current candle — used to blow
        /// the spill budget and silence the setup for good. The wait must instead produce the
        /// signal on the first candle whose ratio qualifies.
        /// </summary>
        [TestCase(0.0, "2026-08-20T15:25:00")]
        [TestCase(0.7, "2026-08-20T15:40:00")]
        [TestCase(1.0, "2026-08-21T06:35:00")]
        public void Diagonal_GbpChfM5_RatioWait_EntersOnLaterCandle(
            double minRiskRewardRatio, string expectedEntry)
        {
            const string file = "GBPCHF_m5_2026-08-14T07-20-00_2026-08-26T21-45-00.csv";
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.3, 50),
                takeProfitMode: DiagonalTakeProfitMode.DIAGONAL_RETRACE,
                minConvergence: 0.3,
                minWave3Penetration: 0.07,
                minRiskRewardRatio: minRiskRewardRatio,
                minWave2Retrace: 0.5,
                maxWave5SpillRatio: 0.01);

            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            DateTime p0Time = new DateTime(2026, 8, 19, 21, 0, 0, DateTimeKind.Utc);
            ElliottWaveSignalEventArgs? signal = signals.FirstOrDefault(s =>
                (s.WavePoints[0].OpenTime - p0Time).Duration() <= TimeSpan.FromSeconds(5));

            Assert.That(signal, Is.Not.Null,
                $"The diagonal vanished at MinRiskRewardRatio = {minRiskRewardRatio}. Funnel: " +
                string.Join(", ", finder.Diag.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}")));

            var expected = DateTime.Parse(expectedEntry, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            Assert.That(signal!.Level.OpenTime, Is.EqualTo(expected),
                $"Entry candle mismatch at MinRiskRewardRatio = {minRiskRewardRatio}.");

            double risk = Math.Abs(signal.Level.Value - signal.StopLoss.Value);
            double reward = Math.Abs(signal.TakeProfit.Value - signal.Level.Value);
            Assert.That(reward, Is.GreaterThanOrEqualTo(minRiskRewardRatio * risk),
                "The emitted setup must satisfy the requested R:R.");
        }

        /// <summary>
        /// Diagnoses two hand-marked diagonals on NZDJPY m5 (2026-08-19 12:35 → 16:05 and
        /// 2026-08-19 21:25 → 08-20 19:55) the same way. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_NzdJpyM5_Case_Diagnostics()
        {
            const string file = "NZDJPY_m5_2026-07-17T04-20-00_2026-08-20T23-55-00.csv";

            RunCaseDiagnostics(
                file,
                new[]
                {
                    (new DateTime(2026, 8, 19, 12, 35, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 14, 5, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 19, 14, 20, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 15, 35, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 19, 15, 50, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 16, 5, 0, DateTimeKind.Utc), true)
                },
                new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 19, 17, 0, 0, DateTimeKind.Utc));

            RunCaseDiagnostics(
                file,
                new[]
                {
                    (new DateTime(2026, 8, 19, 21, 25, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 5, 30, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 20, 7, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 14, 40, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 20, 15, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 19, 55, 0, DateTimeKind.Utc), true)
                },
                new DateTime(2026, 8, 19, 21, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 20, 20, 30, 0, DateTimeKind.Utc));
        }

        /// <summary>
        /// Diagnoses one hand-marked diagonal (USDJPY m5, 2026-08-19 22:05 → 08-20 14:15):
        /// evaluates every §4 gate on the manual skeleton, lists the ladder rungs that see
        /// those pivots, dumps the gates and the signals the live finder produced in the
        /// window. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_UsdJpyM5_Case_Diagnostics()
        {
            // point0 is a LOW (up diagonal): low,high,low,high,low,high.
            RunCaseDiagnostics(
                "USDJPY_m5_2026-07-16T07-05-00_2026-08-20T21-15-00.csv",
                new[]
                {
                    (new DateTime(2026, 8, 19, 22, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 5, 40, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 20, 9, 20, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 12, 5, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 20, 13, 5, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 20, 14, 15, 0, DateTimeKind.Utc), true)
                },
                new DateTime(2026, 8, 19, 21, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc));
        }

        /// <summary>
        /// Diagnoses one hand-marked diagonal (CADJPY m5, 2026-08-19 12:35 → 15:25) the same
        /// way. Research-only.
        /// </summary>
        [Test]
        [Explicit]
        [Category("Research")]
        public void Diagonal_CadJpyM5_Case_Diagnostics()
        {
            RunCaseDiagnostics(
                "CADJPY_m5_2026-07-17T04-20-00_2026-08-20T23-00-00.csv",
                new[]
                {
                    (new DateTime(2026, 8, 19, 12, 35, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 14, 5, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 19, 14, 35, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 15, 10, 0, DateTimeKind.Utc), true),
                    (new DateTime(2026, 8, 19, 15, 20, 0, DateTimeKind.Utc), false),
                    (new DateTime(2026, 8, 19, 15, 25, 0, DateTimeKind.Utc), true)
                },
                new DateTime(2026, 8, 19, 11, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 19, 17, 0, 0, DateTimeKind.Utc));
        }

        private static void RunCaseDiagnostics(
            string file, (DateTime Time, bool IsHigh)[] markup, DateTime from, DateTime to)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute5);
            provider.LoadCandles(Path.Combine(FindDataDir(), file));

            var live = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            double minPen = live.MinWave3Penetration;
            double maxDur = live.MaxWaveDurationRatio;
            TestContext.Out.WriteLine(
                $"{file}\ndefaults: pen={minPen:F3} dur={maxDur:F1} " +
                $"minConv={live.MinConvergence:F2} inside={live.RequireInsideWedge} " +
                $"spill={live.MaxSpillAreaRatio:F4} tol={live.WavePullbackTol:F2} " +
                $"zzPeriod={live.ZigzagPeriod}");

            TestContext.Out.WriteLine("=== manual skeleton ===");
            BarPoint[] p = markup
                .Select(x =>
                {
                    int i = provider.GetIndexByTime(x.Time);
                    double value = x.IsHigh
                        ? provider.GetHighPrice(i)
                        : provider.GetLowPrice(i);
                    return new BarPoint(value, x.Time, provider.TimeFrame, i);
                })
                .ToArray();

            int sgn = 1; // up diagonal
            double w1 = Math.Abs(p[1].Value - p[0].Value);
            double w2 = Math.Abs(p[2].Value - p[1].Value);
            double w3 = Math.Abs(p[3].Value - p[2].Value);
            double w4 = Math.Abs(p[4].Value - p[3].Value);
            double w5 = Math.Abs(p[5].Value - p[4].Value);

            for (int i = 0; i < p.Length; i++)
                TestContext.Out.WriteLine($"  V({i}) {p[i].OpenTime:u} idx={p[i].BarIndex,5} {p[i].Value:F5}");

            TestContext.Out.WriteLine(
                $"  |W1|={w1:F5} |W2|={w2:F5} |W3|={w3:F5} |W4|={w4:F5} |W5|={w5:F5}");
            TestContext.Out.WriteLine(
                $"  bars: W1={p[1].BarIndex - p[0].BarIndex} W2={p[2].BarIndex - p[1].BarIndex} " +
                $"W3={p[3].BarIndex - p[2].BarIndex} W4={p[4].BarIndex - p[3].BarIndex} " +
                $"W5={p[5].BarIndex - p[4].BarIndex} span0-4={p[4].BarIndex - p[0].BarIndex}");

            void Gate(string rule, bool ok, string detail) =>
                TestContext.Out.WriteLine($"  {(ok ? "OK  " : "FAIL")} {rule,-14} {detail}");

            Gate("D-W2", sgn * (p[2].Value - p[0].Value) > 0,
                $"V(2)-V(0)={p[2].Value - p[0].Value:F5}");
            Gate("D-W3-PEN", sgn * (p[3].Value - p[1].Value) >= minPen * w1,
                $"pen={p[3].Value - p[1].Value:F5} = {(p[3].Value - p[1].Value) / w1:P2} of |W1|");
            Gate("D-CONTRACT-3", w3 < w1, $"{w3:F5} < {w1:F5}");
            Gate("D-CONTRACT-4", w4 < w2, $"{w4:F5} < {w2:F5}");
            Gate("D-W4-38", w4 >= live.MinWave4RetraceW3 * w3,
                $"{w4:F5} >= {live.MinWave4RetraceW3 * w3:F5} (38.2% of |W3|)");
            Gate("D-W4-24", sgn * (p[1].Value - p[4].Value) >= live.MinWave4Wave2Level * w2,
                $"level={sgn * (p[1].Value - p[4].Value) / w2:F3} of |W2|");
            Gate("D-TIME-24",
                p[4].BarIndex - p[3].BarIndex < p[2].BarIndex - p[1].BarIndex,
                $"bars(W4)={p[4].BarIndex - p[3].BarIndex} < bars(W2)={p[2].BarIndex - p[1].BarIndex}");
            Gate("D-OVERLAP", sgn * (p[4].Value - p[1].Value) < 0,
                $"V(4)-V(1)={p[4].Value - p[1].Value:F5}");
            Gate("D-W4-2", sgn * (p[4].Value - p[2].Value) > 0,
                $"V(4)-V(2)={p[4].Value - p[2].Value:F5}");
            Gate("D-W5-BREAK", sgn * (p[5].Value - p[3].Value) > 0,
                $"V(5)-V(3)={p[5].Value - p[3].Value:F5}");
            Gate("D-W5-CAP", w5 < w3, $"{w5:F5} < {w3:F5}");

            double ceilSlope = (p[3].Value - p[1].Value) / (p[3].BarIndex - p[1].BarIndex);
            double floorSlope = (p[4].Value - p[2].Value) / (p[4].BarIndex - p[2].BarIndex);
            double widthAt1 =
                sgn * (p[1].Value - (p[2].Value + floorSlope * (p[1].BarIndex - p[2].BarIndex)));
            double widthAt4 =
                sgn * ((p[1].Value + ceilSlope * (p[4].BarIndex - p[1].BarIndex)) - p[4].Value);
            Gate("D-CONVERGE", widthAt1 / widthAt4 - 1 >= live.MinConvergence,
                $"conv={widthAt1 / widthAt4 - 1:F3}");

            double spill = 0, wedge = 0;
            for (int bar = p[1].BarIndex; bar <= p[4].BarIndex; bar++)
            {
                if (live.PrintFilter.IsExcluded(bar))
                    continue;

                double ceiling = p[1].Value + ceilSlope * (bar - p[1].BarIndex);
                double floorLine = p[2].Value + floorSlope * (bar - p[2].BarIndex);
                spill += Math.Max(0, provider.GetHighPrice(bar) - ceiling) +
                         Math.Max(0, floorLine - provider.GetLowPrice(bar));
                wedge += ceiling - floorLine;
            }

            Gate("D-INSIDE", spill / wedge <= live.MaxSpillAreaRatio,
                $"spill={spill / wedge:F5} (limit {live.MaxSpillAreaRatio:F4})");

            for (int w = 3; w < p.Length - 1; w++)
            {
                double siblingBars = p[w - 2].BarIndex - p[w - 3].BarIndex;
                double curBars = p[w].BarIndex - p[w - 1].BarIndex;
                double ratio = Math.Max(curBars / siblingBars, siblingBars / curBars);
                Gate("D-TIME", ratio <= maxDur,
                    $"W{w} vs W{w - 2}: {curBars}/{siblingBars} -> ratio={ratio:F2}");
            }

            // Deepest counter-move inside every marked wave: the greedy merger ends a wave
            // when a pullback exceeds WavePullbackTol of the amplitude accumulated so far.
            TestContext.Out.WriteLine("=== worst internal pullback per wave (share of run so far) ===");
            for (int w = 1; w < p.Length; w++)
            {
                bool up = p[w].Value > p[w - 1].Value;
                double start = p[w - 1].Value;
                double best = start, worst = 0;
                DateTime worstAt = p[w - 1].OpenTime;
                for (int bar = p[w - 1].BarIndex + 1; bar <= p[w].BarIndex; bar++)
                {
                    double fwd = up ? provider.GetHighPrice(bar) : provider.GetLowPrice(bar);
                    double back = up ? provider.GetLowPrice(bar) : provider.GetHighPrice(bar);
                    double run = Math.Abs(best - start);
                    double pull = up ? best - back : back - best;
                    if (run > 0 && pull / run > worst)
                    {
                        worst = pull / run;
                        worstAt = provider.GetOpenTime(bar);
                    }

                    if (up ? fwd > best : fwd < best)
                        best = fwd;
                }

                TestContext.Out.WriteLine(
                    $"  W{w}: worst pullback = {worst:P1} at {worstAt:MM-dd HH:mm} " +
                    $"(tol = {live.WavePullbackTol:P0})");
            }

            // Which ladder rungs actually see the marked pivots?
            int basePeriod = AutoPeriodEstimator.EstimateImpulsePeriod(provider);
            TestContext.Out.WriteLine($"=== auto base period = {basePeriod} ===");
            var window = (From: from, To: to);

            int[] markedIdx = p.Select(x => x.BarIndex).ToArray();
            foreach (double ratio in new[]
                     {
                         0.382, 0.618, 0.786, 1.000, 1.127, 1.272, 1.434, 1.618,
                         1.826, 2.058, 2.321, 2.618, 3.330, 4.236, 5.388, 6.854
                     })
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                var inWindow = zz.Extrema.Values
                    .Where(x => x.OpenTime >= window.From && x.OpenTime <= window.To)
                    .OrderBy(x => x.OpenTime)
                    .ToList();

                string pivots = string.Join(", ", inWindow.Select(x => $"{x.OpenTime:MM-dd HH:mm}"));
                int between = inWindow.Count(x =>
                    x.BarIndex > markedIdx[0] && x.BarIndex < markedIdx[4]);
                string hit = string.Join("", markedIdx
                    .Select(mi => inWindow.Any(x => x.BarIndex == mi) ? "#" : "."));

                TestContext.Out.WriteLine(
                    $"  period={period,4} pivots0to4={between,3} hit0..5={hit} | {pivots}");
            }

            // What does the live finder do with candidates in the window?
            var gates = new List<string>();
            live.OnGate = (p0, gate) =>
            {
                if (p0.OpenTime >= window.From && p0.OpenTime <= window.To)
                    gates.Add($"{p0.OpenTime:u} -> {gate}");
            };
            var emitted = new List<ElliottWaveSignalEventArgs>();
            live.OnEnter += (_, a) => emitted.Add(a);
            live.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                live.CheckBar(provider.GetOpenTime(i));

            TestContext.Out.WriteLine("=== live finder gates in the window ===");
            foreach (string g in gates.Distinct())
                TestContext.Out.WriteLine($"  {g}");

            TestContext.Out.WriteLine("=== signals in the window ===");
            foreach (ElliottWaveSignalEventArgs s in emitted
                         .Where(x => x.WavePoints.Any(wp => wp.OpenTime >= window.From &&
                                                           wp.OpenTime <= window.To)))
            {
                TestContext.Out.WriteLine(
                    "  " + string.Join(" | ", s.WavePoints.Select(
                        (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm} {x.Value:F5}")));
                TestContext.Out.WriteLine(
                    $"    entry={s.Level.Value:F5} sl={s.StopLoss.Value:F5} tp={s.TakeProfit.Value:F5}");
            }

            // When does each candidate reach the pool, and in what order do they fire?
            TestContext.Out.WriteLine("=== registration timeline (registered/entered) ===");
            var timed = new DiagonalSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            DateTime curBar = default;
            timed.OnGate = (p0, gate) =>
            {
                if (gate is "registered" or "entered" &&
                    p0.OpenTime >= window.From && p0.OpenTime <= window.To)
                    TestContext.Out.WriteLine(
                        $"  bar {curBar:MM-dd HH:mm} | V(0) {p0.OpenTime:MM-dd HH:mm} -> {gate}");
            };
            timed.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
            {
                curBar = provider.GetOpenTime(i);
                timed.CheckBar(curBar);
            }

            // Production-style configs: does the zigzag/size filter change the skeleton?
            TestContext.Out.WriteLine("=== config matrix (all signals touching the window) ===");
            foreach ((double dev, int bars) in new[] { (0.1, 10), (0.3, 40) })
            foreach (bool retraceTp in new[] { false, true })
            foreach (bool w5Ratio in new[] { false, true })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, dev, bars),
                    takeProfitRatio: 1.0,
                    requireWave5Ratio: w5Ratio,
                    takeProfitMode: retraceTp
                        ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                        : DiagonalTakeProfitMode.RISK_RATIO);

                var found = new List<ElliottWaveSignalEventArgs>();
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints.Any(wp => wp.OpenTime >= window.From &&
                                               wp.OpenTime <= window.To))
                        found.Add(a);
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  dev={dev:F1} bars={bars,2} tp={(retraceTp ? "23.6%" : "R1.0")} " +
                    $"w5ratio={w5Ratio,-5} -> {found.Count} signal(s)");
                foreach (ElliottWaveSignalEventArgs s in found)
                    TestContext.Out.WriteLine(
                        "      " + string.Join(" | ", s.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm}")) +
                        $" entry={s.Level.Value:F3} sl={s.StopLoss.Value:F3} tp={s.TakeProfit.Value:F3}");
            }

            // Zigzag period sweep: the auto period depends on how much history is loaded,
            // so a chart with a shorter history can carve a different (right-shifted) wedge.
            TestContext.Out.WriteLine("=== zigzag period sweep (all signals touching the window) ===");
            foreach (int zzPeriod in new[] { 4, 5, 6, 8, 10, 12, 14, 16, 19, 21, 27 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(zzPeriod, 0.1, 10));

                var found = new List<ElliottWaveSignalEventArgs>();
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints.Any(wp => wp.OpenTime >= window.From &&
                                               wp.OpenTime <= window.To))
                        found.Add(a);
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine($"  period={zzPeriod,3} -> {found.Count} signal(s)");
                foreach (ElliottWaveSignalEventArgs s in found)
                    TestContext.Out.WriteLine(
                        "      " + string.Join(" | ", s.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm}")) +
                        $" entry={s.Level.Value:F3}");
            }

            // Greedy-merge tolerance: a wave ends when a pullback exceeds this share of the
            // amplitude accumulated so far, so it decides where wave 1 stops.
            TestContext.Out.WriteLine("=== wavePullbackTol sweep (default gates) ===");
            foreach (double tol in new[] { 0.20, 0.25, 0.30, 0.34, 0.40, 0.50, 0.60, 0.70, 0.80 })
            foreach (bool retraceTp in new[] { false, true })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    takeProfitMode: retraceTp
                        ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                        : DiagonalTakeProfitMode.RISK_RATIO,
                    wavePullbackTol: tol);

                var found = new List<ElliottWaveSignalEventArgs>();
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints.Any(wp => wp.OpenTime >= window.From &&
                                               wp.OpenTime <= window.To))
                        found.Add(a);
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  tol={tol:F2} tp={(retraceTp ? "23.6%" : "R1.0")} -> {found.Count} signal(s)");
                foreach (ElliottWaveSignalEventArgs s in found)
                    TestContext.Out.WriteLine(
                        "      " + string.Join(" | ", s.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm}")) +
                        $" entry={s.Level.Value:F3} sl={s.StopLoss.Value:F3} tp={s.TakeProfit.Value:F3}");
            }

            // Gate knobs that reject "almost-wedges": how far wave 4 must reach into wave 2
            // (D-W4-24), whether wave 4 must be shorter than wave 2 (D-TIME-24) and how much
            // the bars may spill out of the wedge (D-INSIDE).
            TestContext.Out.WriteLine("=== D-W4-24 / D-TIME-24 / D-INSIDE sweep ===");
            foreach (double w4Level in new[] { 0.236, 0.20, 0.15, 0.0 })
            foreach (bool w4Shorter in new[] { true, false })
            foreach (double spillLimit in new[] { 0.005, 0.02 })
            {
                var finder = new DiagonalSetupFinder(
                    provider, provider.BarSymbol, new EWParams(0, 0.1, 10),
                    maxSpillAreaRatio: spillLimit,
                    minWave4Wave2Level: w4Level,
                    requireWave4Shorter: w4Shorter);

                var found = new List<ElliottWaveSignalEventArgs>();
                finder.OnEnter += (_, a) =>
                {
                    if (a.WavePoints.Any(wp => wp.OpenTime >= window.From &&
                                               wp.OpenTime <= window.To))
                        found.Add(a);
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));

                TestContext.Out.WriteLine(
                    $"  w4Level={w4Level:F3} w4Shorter={w4Shorter,-5} spill={spillLimit:F3} " +
                    $"-> {found.Count} signal(s)");
                foreach (ElliottWaveSignalEventArgs s in found)
                    TestContext.Out.WriteLine(
                        "      " + string.Join(" | ", s.WavePoints.Select(
                            (x, i) => $"V({i}) {x.OpenTime:MM-dd HH:mm}")) +
                        $" entry={s.Level.Value:F3}");
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
