using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for <see cref="RunningTriangleSetupFinder"/> against the reference example
    /// from EW_R_TRIANGLE.md §12: a bearish (down-thrust) running triangle on AUDUSD m15
    /// that corrects a down-trend and thrusts down to ≈0.6866 by 2026-06-30.
    /// <para>
    /// The finder resolves the ABCDE waves at the zigzag scale (merging sub-waves), so the
    /// exact macro pivots the user eyeballed (point0 2026-06-24T18:00, E 2026-06-29T07:15)
    /// are not asserted verbatim — per §12 the test checks the running property, the
    /// bearish mirror, and that the down-thrust to the reference target is captured.
    /// </para>
    /// </summary>
    internal class RunningTriangleSetupFinderTests
    {
        private const string REFERENCE_FILE =
            "AUDUSD_m15_2026-06-17T22-15-00_2026-07-10T20-45-00.csv";

        /// <summary>Whether <paramref name="a"/> is farther in the thrust direction than <paramref name="b"/>.</summary>
        private static bool Fwd(bool isUp, double a, double b) => isUp ? a > b : a < b;

        [Test]
        public void RunningTriangle_ReferenceExample_IsDetected()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var refP0 = new DateTime(2026, 6, 24, 18, 0, 0, DateTimeKind.Utc);
            var refGates = new HashSet<string>();

            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.OnGate = (p0, key) =>
            {
                if (p0 != null && p0.OpenTime == refP0)
                    refGates.Add(key);
            };
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(signals, Is.Not.Empty, "No running-triangle setups detected.");

            // Every emitted setup must be a valid running triangle (EW_R_TRIANGLE.md §4):
            // running B, C/D/E ordering, and E crossing beyond point 0 (R-E-0).
            foreach (ElliottWaveSignalEventArgs s in signals)
            {
                double p0 = s.WavePoints[0].Value, a = s.WavePoints[1].Value, b = s.WavePoints[2].Value;
                double c = s.WavePoints[3].Value, d = s.WavePoints[4].Value, e = s.WavePoints[5].Value;
                bool isUp = s.TakeProfit.Value > s.StopLoss.Value;
                DateTime at = s.WavePoints[0].OpenTime;

                Assert.That(Fwd(isUp, b, p0), Is.True,
                    $"{at:u}: wave B is not running (beyond point 0). " +
                    $"p0={p0:F5} a={a:F5} b={b:F5} c={c:F5} d={d:F5} e={e:F5} isUp={isUp}");
                Assert.That(!Fwd(isUp, c, p0) && Fwd(isUp, c, a), Is.True, $"{at:u}: wave C invalid.");
                Assert.That(!Fwd(isUp, d, b) && Fwd(isUp, d, c), Is.True, $"{at:u}: wave D invalid.");
                Assert.That(Fwd(isUp, e, a) && !Fwd(isUp, e, d), Is.True, $"{at:u}: wave E out of A..D.");
                Assert.That(!Fwd(isUp, e, p0), Is.True, $"{at:u}: wave E does not cross beyond point 0 (R-E-0).");
            }

            // The reference triangle (point 0 = 2026-06-24T18:00) is structurally recognized:
            // a build from that point 0 passed every §4 structural gate and reached the
            // entry-viability stage. (At its natural coarse scale wave E confirms only after
            // the thrust has run to the TP, so the live entry itself is filtered — see §12.)
            var structural = new[]
                { "entered", "tpSlHit", "tooCloseToSl", "duplicate", "duplicatePoint0" };
            Assert.That(refGates.Overlaps(structural), Is.True,
                "The reference running triangle (point 0 = 2026-06-24T18:00 → E 2026-06-29T07:15) " +
                "was not recognized as a structurally valid running triangle. Gates seen: " +
                string.Join(",", refGates));
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

        [Test]
        public void Diag_ScaleSweep_ReferenceTriangle()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var refP0 = new DateTime(2026, 7, 3, 6, 30, 0, DateTimeKind.Utc);

            Console.WriteLine("=== Period sweep: wave values for refP0=Jul3 06:30 ===");
            for (int period = 2; period <= 34; period++)
            {
                var finder = new RunningTriangleSetupFinder(
                    provider, provider.BarSymbol, new EWParams(period, 0.1, 10));
                bool found = false;
                finder.OnWaveGate = (p0, gate, a, b, c, d, e) =>
                {
                    if (p0?.OpenTime == refP0 && gate != "duplicatePoint0" && gate != "duplicate")
                    {
                        found = true;
                        if (gate == "entered" || gate == "assembled" || gate == "notRunning" ||
                            gate == "waveCFail" || gate == "waveDFail" || gate == "assembled")
                        Console.WriteLine($"  p={period,2} gate={gate,-22} " +
                                          $"A={a.Value:F6}@{a.OpenTime:HH:mm} " +
                                          $"B={b.Value:F6}@{b.OpenTime:HH:mm} " +
                                          $"C={c.Value:F6}@{c.OpenTime:HH:mm} " +
                                          $"D={d.Value:F6}@{d.OpenTime:HH:mm} " +
                                          $"E={e.Value:F6}@{e.OpenTime:HH:mm}");
                    }
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                    finder.CheckBar(provider.GetOpenTime(i));
                if (!found)
                    Console.WriteLine($"  p={period,2}  (no assembled candidates)");
            }
        }

        /// <summary>
        /// Shows ABCDE wave values at entry for every emitted signal, and flags cases
        /// where the entry bar's price has already broken past wave D (making the
        /// triangle D-breached). Test for the Jun 22 04:00 issue.
        /// </summary>
        [Test]
        public void Diag_EnteredSignals_CheckWaveDBreach()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(5, 0.1, 15));
            var entries = new List<(BarPoint P0, BarPoint A, BarPoint B, BarPoint C, BarPoint D, BarPoint E, bool IsUp, double Level, DateTime LevelTime, double BarHigh, double BarLow)>();
            finder.OnEnter += (_, args) =>
            {
                var pts = args.WavePoints;
                int barIdx = provider.GetIndexByTime(args.Level.OpenTime);
                double barHigh = provider.GetHighPrice(barIdx);
                double barLow = provider.GetLowPrice(barIdx);
                entries.Add((pts[0], pts[1], pts[2], pts[3], pts[4], pts[5],
                    args.TakeProfit.Value > args.StopLoss.Value,
                    args.Level.Value, args.Level.OpenTime, barHigh, barLow));
            };
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Console.WriteLine($"=== Entered signals: {entries.Count} total (period=5 minBars=15) ===");
            int breached = 0, ok = 0, ePastC = 0;
            foreach (var e in entries.OrderBy(e => e.P0.OpenTime))
            {
                bool barPastD = e.IsUp ? e.BarHigh > e.D.Value : e.BarLow < e.D.Value;
                // §7.3: E past C (but not past A) = sideways rebuild needed
                bool isEPastC = e.IsUp ? e.E.Value < e.C.Value : e.E.Value > e.C.Value;
                if (barPastD) breached++;
                if (isEPastC) ePastC++;
                if (!barPastD) ok++;
                string flags = "";
                if (barPastD) flags += " [D-BREACH]";
                if (isEPastC) flags += " [E-PAST-C: rebuild needed §7.3]";
                Console.WriteLine(
                    $"  p0={e.P0.OpenTime:u} lvl={e.Level:F6}@{e.LevelTime:HH:mm} barH={e.BarHigh:F6} barL={e.BarLow:F6} " +
                    $"isUp={e.IsUp} p0Val={e.P0.Value:F6} " +
                    $"A={e.A.Value:F6} B={e.B.Value:F6} C={e.C.Value:F6} " +
                    $"D={e.D.Value:F6} E={e.E.Value:F6}{flags}");
            }
            Console.WriteLine($"  OK={ok}  D-breached={breached}  E-past-C={ePastC}");

            Console.WriteLine("\n=== Diag ===");
            foreach (var kv in finder.Diag.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {kv.Key}: {kv.Value}");
        }

        /// <summary>
        /// Verifies that the Jul 2–6 running triangle (the second one the user asked about)
        /// IS detected by the finder and passes all structural gates. The triangle:
        /// point0≈Jul3 06:30, A≈Jul2 21:00 low, B≈Jul2 12:30 spike high, thrust down
        /// to ~0.6866 by Jul6-7.
        /// </summary>
        [Test]
        public void RunningTriangle_Jul2ToJul6_IsDetected()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            // The triangle has several potential point0 locations depending on scale.
            // We target the one at Jul 3 06:30 which the sweep showed as "entered".
            var refP0 = new DateTime(2026, 7, 3, 6, 30, 0, DateTimeKind.Utc);
            var refGates = new HashSet<string>();

            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.OnGate = (p0, key) =>
            {
                if (p0 != null && p0.OpenTime == refP0)
                    refGates.Add(key);
            };
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(signals, Is.Not.Empty,
                "No running-triangle setups detected at all.");

            // Verify the Jul 2-6 triangle was structurally recognized.
            var structural = new[]
                { "entered", "tpSlHit", "tooCloseToSl", "duplicate", "duplicatePoint0" };
            Assert.That(refGates.Overlaps(structural), Is.True,
                "The Jul 2-6 running triangle (point 0 = 2026-07-03T06:30) " +
                "was not recognized as a structurally valid running triangle. " +
                "Gates seen: " + string.Join(",", refGates));

            // Also verify that at least one signal in the Jul 2-6 window is present.
            var julStart = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
            var julEnd   = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
            var inWindow = signals.Where(s => s.WavePoints[0].OpenTime >= julStart &&
                                              s.WavePoints[0].OpenTime <= julEnd).ToList();
            Assert.That(inWindow, Is.Not.Empty,
                "No running-triangle signals in the Jul 2-7 window.");

            Console.WriteLine($"=== Jul 2-7 signals: {inWindow.Count} ===");
            foreach (var s in inWindow)
            {
                double p0Val = s.WavePoints[0].Value, aVal = s.WavePoints[1].Value,
                       bVal = s.WavePoints[2].Value, cVal = s.WavePoints[3].Value,
                       dVal = s.WavePoints[4].Value, eVal = s.WavePoints[5].Value;
                bool isUp = s.TakeProfit.Value > s.StopLoss.Value;
                Console.WriteLine($"  p0={s.WavePoints[0].OpenTime:u} isUp={isUp} " +
                                  $"A={aVal:F6} B={bVal:F6} C={cVal:F6} D={dVal:F6} E={eVal:F6} " +
                                  $"TP={s.TakeProfit.Value:F6} SL={s.StopLoss.Value:F6}");
            }
        }

        /// <summary>
        /// The user-anchored BULLISH running triangle of Jul 2–6 2026 (research task
        /// 2026-07-21): 0 = 2026-07-02T14:15 (0.69433), A = 2026-07-02T21:15 (0.69063),
        /// B = 2026-07-03T06:30 (0.69496 — running), C = 2026-07-06T03:15 (0.69217),
        /// D = 2026-07-06T08:00 (0.69384), E = 2026-07-06T12:45 (0.69238); the thrust
        /// after E runs to 0.6959 and hits the TP at wave B the same day (~17:00).
        /// <para>
        /// No single zigzag scale resolves all six pivots (see Diag_July2ToJuly6_Gates):
        /// 0/A/B/C assemble only at period ≥ 26 (the 17.7-pip bounce inside wave A breaks
        /// the fine-scale assembly), while the 16.7/14.6-pip D/E reversals exist only at
        /// period ≤ 21. The finder must therefore assemble it cross-scale (§7.4): 0/A/B/C
        /// from a coarse rung, D/E from a fine rung — firing on E's confirmation bar,
        /// while the entry is still tradeable (≈0.6927 vs TP ≈ 0.6949).
        /// </para>
        /// </summary>
        [Test]
        public void RunningTriangle_Jul2ToJul6_Bullish_IsDetected()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var refP0 = new DateTime(2026, 7, 2, 14, 15, 0, DateTimeKind.Utc);
            var tA = new DateTime(2026, 7, 2, 21, 15, 0, DateTimeKind.Utc);
            var tB = new DateTime(2026, 7, 3, 6, 30, 0, DateTimeKind.Utc);
            var tC = new DateTime(2026, 7, 6, 3, 15, 0, DateTimeKind.Utc);
            var tD = new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc);
            var tE = new DateTime(2026, 7, 6, 12, 45, 0, DateTimeKind.Utc);

            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            var signals = new List<ElliottWaveSignalEventArgs>();
            var tpHits = new List<DateTime>();
            finder.OnEnter += (_, a) => signals.Add(a);
            finder.OnTakeProfit += (_, a) => tpHits.Add(a.Level.OpenTime);
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            ElliottWaveSignalEventArgs hit = signals.FirstOrDefault(
                s => s.WavePoints[0].OpenTime == refP0 &&
                     s.WavePoints[3].OpenTime == tC &&
                     s.WavePoints[4].OpenTime == tD &&
                     s.WavePoints[5].OpenTime == tE);

            foreach (ElliottWaveSignalEventArgs s in signals.Where(
                         s => s.WavePoints[0].OpenTime == refP0))
            {
                string waves = string.Join(", ", s.WavePoints.Select(
                    w => $"{w.OpenTime:MM-dd HH:mm}({w.Value:F5})"));
                Console.WriteLine($"signal: entry={s.Level.OpenTime:u}@{s.Level.Value:F5} " +
                    $"TP={s.TakeProfit.Value:F5} SL={s.StopLoss.Value:F5} waves=[{waves}]");
            }

            Assert.That(hit, Is.Not.Null,
                "The bullish Jul 2–6 running triangle (point 0 = 2026-07-02T14:15, " +
                "C = 2026-07-06T03:15) was not detected. See signal dump above.");

            bool isUp = hit.TakeProfit.Value > hit.StopLoss.Value;
            Assert.That(isUp, Is.True, "The Jul 2–6 triangle must be bullish (up-thrust).");

            // The six anchor pivots must resolve exactly (cross-scale, §7.4).
            Assert.That(hit.WavePoints[1].OpenTime, Is.EqualTo(tA), "wave A time");
            Assert.That(hit.WavePoints[2].OpenTime, Is.EqualTo(tB), "wave B time");
            Assert.That(hit.WavePoints[3].OpenTime, Is.EqualTo(tC), "wave C time");
            Assert.That(hit.WavePoints[4].OpenTime, Is.EqualTo(tD), "wave D time");
            Assert.That(hit.WavePoints[5].OpenTime, Is.EqualTo(tE), "wave E time");
            Assert.That(hit.WavePoints[1].Value, Is.EqualTo(0.69063).Within(1e-5), "wave A");
            Assert.That(hit.WavePoints[2].Value, Is.EqualTo(0.69496).Within(1e-5), "wave B");
            Assert.That(hit.WavePoints[3].Value, Is.EqualTo(0.69217).Within(1e-5), "wave C");
            Assert.That(hit.WavePoints[4].Value, Is.EqualTo(0.69384).Within(1e-5), "wave D");
            Assert.That(hit.WavePoints[5].Value, Is.EqualTo(0.69238).Within(1e-5), "wave E");

            // The entry must be tradeable: fired on E's confirmation bar (well before the
            // thrust completes at ~17:00), with the TP at the running wave B.
            Assert.That(hit.Level.OpenTime, Is.LessThan(new DateTime(2026, 7, 6, 17, 0, 0, DateTimeKind.Utc)),
                "Entry must fire on wave E's confirmation, not after the thrust.");
            Assert.That(hit.TakeProfit.Value, Is.EqualTo(0.69496).Within(2e-4),
                "TP must sit at wave B (running).");
            Assert.That(hit.StopLoss.Value, Is.LessThan(hit.WavePoints[5].Value),
                "SL must be beyond wave A (below E for an up-thrust).");

            Console.WriteLine($"entered {hit.Level.OpenTime:u} @ {hit.Level.Value:F5} " +
                $"TP={hit.TakeProfit.Value:F5} SL={hit.StopLoss.Value:F5}; " +
                $"tpHit={(tpHits.Count > 0 ? tpHits[0].ToString("u") : "none")}");
        }

        [Test]
        public void Diag_July2ToJuly6_D_Dump()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            // Dump extrema on period 4 around Jul6 
            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(4, 0.1, 10));
            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            var zigzagField = typeof(RunningTriangleSetupFinder)
                .GetField("m_ExtremumFinders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (zigzagField?.GetValue(finder) is System.Collections.IList finders && finders.Count > 0)
            {
                var df = (TradeKit.Core.Indicators.DeviationExtremumFinder)finders[0];
                var extrema = df.Extrema;
                Console.WriteLine($"\n=== Period=4 extrema near Jul6 03:15 - Jul7 05:00 ===");
                var from = new DateTime(2026, 7, 5, 21, 0, 0, DateTimeKind.Utc);
                var to   = new DateTime(2026, 7, 7, 6, 0, 0, DateTimeKind.Utc);
                int idx = 0;
                foreach (var kv in extrema.Where(kv => kv.Key >= from && kv.Key <= to))
                {
                    var bp = kv.Value;
                    Console.WriteLine($"  [{idx,3}] {kv.Key:u} val={bp.Value:F6}");
                    idx++;
                }
            }

            // Now also check: does ExtendWave for D on period=4 stop at Jul6 08:00?
            Console.WriteLine("\n=== Now building D on period=4 for p0=Jul2 14:15 ===");
            var f2 = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(4, 0.1, 10));
            f2.OnWaveGate = (p0, gate, a, b, c, d, e) =>
            {
                if (p0?.OpenTime == new DateTime(2026, 7, 2, 14, 15, 0, DateTimeKind.Utc))
                    Console.WriteLine($"  gate={gate} D={d.Value:F6}@{d.OpenTime:u}");
            };
            f2.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                f2.CheckBar(provider.GetOpenTime(i));
        }

        /// <summary>
        /// Gate / pivot diagnostic for the user-anchored BULLISH running triangle of
        /// Jul 2–6 2026 (research task 2026-07-21):
        /// 0 = 2026-07-02T14:15 (high 0.69433), A = 2026-07-02T21:15 (low 0.69063),
        /// B = 2026-07-03T06:30 (high 0.69496 — running, above point 0),
        /// C = 2026-07-06T03:15 (low 0.69217), D = 2026-07-06T08:00 (high 0.69384),
        /// E = 2026-07-06T12:45 (low 0.69238; post-E thrust to 0.6959 hits TP = wave B).
        /// Sweeps single zigzag periods 2..40 and reports, per period:
        /// (1) which of the 6 anchor pivots the zigzag produces at all;
        /// (2) which validation gate a build from point0 = 2026-07-02T14:15 reaches
        ///     (single-rung ladder injected via reflection, IsInSetup reset per bar);
        /// (2b) the same for the production finder (auto period, full ladder);
        /// (3) the full pivot list in the window for representative periods.
        /// </summary>
        [Test]
        public void Diag_July2ToJuly6_Gates()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var t0 = new DateTime(2026, 7, 2, 14, 15, 0, DateTimeKind.Utc);
            var tA = new DateTime(2026, 7, 2, 21, 15, 0, DateTimeKind.Utc);
            var tB = new DateTime(2026, 7, 3, 6, 30, 0, DateTimeKind.Utc);
            var tC = new DateTime(2026, 7, 6, 3, 15, 0, DateTimeKind.Utc);
            var tD = new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc);
            var tE = new DateTime(2026, 7, 6, 12, 45, 0, DateTimeKind.Utc);
            var anchors = new[] { ("0", t0), ("A", tA), ("B", tB), ("C", tC), ("D", tD), ("E", tE) };

            const int P_MIN = 2, P_MAX = 40;

            // ---------- Part 1: anchor-pivot presence per single zigzag period ----------
            Console.WriteLine("=== Part 1: anchor pivot presence per single rung (period 2..40) ===");
            Console.WriteLine("    (dev ≈ period × 0.69 pips at price 0.693; 'extra' = non-anchor pivots in [0..E])");
            var allPresent = new List<int>();
            for (int period = P_MIN; period <= P_MAX; period++)
            {
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                List<BarPoint> piv = zz.Extrema.Values.ToList();
                bool[] flags = anchors.Select(a => piv.Any(p => p.OpenTime == a.Item2)).ToArray();
                int extra = piv.Count(p => p.OpenTime >= t0 && p.OpenTime <= tE) - flags.Count(f => f);
                if (flags.All(f => f))
                    allPresent.Add(period);
                double devPips = period * 1e-4 * 0.693 * 1e4;
                Console.WriteLine($"  p={period,2} dev~{devPips,4:F1}pips  " +
                    string.Join(" ", anchors.Select((a, i) => flags[i] ? a.Item1 : "·")) +
                    $"  extra={extra,2}");
            }
            Console.WriteLine($"  → periods with ALL 6 anchors present: " +
                (allPresent.Count == 0 ? "NONE" : string.Join(",", allPresent)));

            // ---------- Part 2: gate reached per single rung (reflection-swapped ladder) ----------
            Console.WriteLine("\n=== Part 2: gates for point0 = Jul 2 14:15 per single rung ===");
            var enteredPeriods = new List<int>();
            var triedPeriods = new List<int>();
            for (int period = P_MIN; period <= P_MAX; period++)
            {
                var finder = new RunningTriangleSetupFinder(
                    provider, provider.BarSymbol, new EWParams(period, 0.0, 0));

                // Swap the whole scale ladder for the single rung under test.
                var fld = typeof(RunningTriangleSetupFinder).GetField("m_ExtremumFinders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var rungs = (System.Collections.IList)fld!.GetValue(finder)!;
                rungs.Clear();
                rungs.Add(new DeviationExtremumFinder(period, provider));

                var gateCount = new Dictionary<string, int>();
                string firstDetail = null;
                finder.OnWaveGate = (p0, gate, a, b, c, d, e) =>
                {
                    if (p0?.OpenTime != t0)
                        return;
                    gateCount[gate] = gateCount.TryGetValue(gate, out int n) ? n + 1 : 1;
                    firstDetail ??= $"A={a.OpenTime:MM-dd HH:mm}({a.Value:F5}) " +
                                    $"B={b.OpenTime:MM-dd HH:mm}({b.Value:F5}) " +
                                    $"C={c.OpenTime:MM-dd HH:mm}({c.Value:F5}) " +
                                    $"D={d.OpenTime:MM-dd HH:mm}({d.Value:F5}) " +
                                    $"E={e.OpenTime:MM-dd HH:mm}({e.Value:F5})";
                };
                finder.MarkAsInitialized();
                for (int i = 0; i < provider.Count; i++)
                {
                    finder.IsInSetup = false; // keep the rung updating on every bar
                    finder.CheckBar(provider.GetOpenTime(i));
                }

                if (gateCount.ContainsKey("entered"))
                    enteredPeriods.Add(period);
                if (gateCount.Count > 0)
                    triedPeriods.Add(period);
                string gates = gateCount.Count == 0
                    ? "(silent — never assembled)"
                    : string.Join(", ", gateCount.Select(kv => $"{kv.Key}×{kv.Value}"));
                Console.WriteLine($"  p={period,2}: {gates}" +
                    (firstDetail != null ? $"\n        first: {firstDetail}" : ""));
            }
            Console.WriteLine($"  → periods where point0 reached TryEmit: " +
                (triedPeriods.Count == 0 ? "NONE" : string.Join(",", triedPeriods)));
            Console.WriteLine($"  → periods where point0 ENTERED: " +
                (enteredPeriods.Count == 0 ? "NONE" : string.Join(",", enteredPeriods)));

            // ---------- Part 2b: production finder (auto period, full ladder) ----------
            Console.WriteLine("\n=== Part 2b: production finder (EWParams(0,0.1,10), full ladder) ===");
            var prod = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));
            var prodGates = new Dictionary<string, int>();
            string prodFirst = null;
            prod.OnWaveGate = (p0, gate, a, b, c, d, e) =>
            {
                if (p0?.OpenTime != t0)
                    return;
                prodGates[gate] = prodGates.TryGetValue(gate, out int n) ? n + 1 : 1;
                prodFirst ??= $"gate={gate} A={a.OpenTime:MM-dd HH:mm}({a.Value:F5}) " +
                              $"B={b.OpenTime:MM-dd HH:mm}({b.Value:F5}) " +
                              $"C={c.OpenTime:MM-dd HH:mm}({c.Value:F5}) " +
                              $"D={d.OpenTime:MM-dd HH:mm}({d.Value:F5}) " +
                              $"E={e.OpenTime:MM-dd HH:mm}({e.Value:F5})";
            };
            prod.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
            {
                prod.IsInSetup = false;
                prod.CheckBar(provider.GetOpenTime(i));
            }
            Console.WriteLine("  " + (prodGates.Count == 0
                ? "(silent — never assembled)"
                : string.Join(", ", prodGates.Select(kv => $"{kv.Key}×{kv.Value}"))));
            if (prodFirst != null)
                Console.WriteLine($"  first: {prodFirst}");

            // The production finder (auto period, full ladder, cross-scale §7.4) must
            // recognize the user-anchored triangle and reach the entry gate.
            Assert.That(prodGates.ContainsKey("entered"), Is.True,
                "Production finder did not enter the Jul 2–6 cross-scale running triangle. " +
                "Gates seen: " + string.Join(",", prodGates.Keys));

            // ---------- Part 3: representative pivot dumps ----------
            Console.WriteLine("\n=== Part 3: pivot lists in [Jul 2 00:00 .. Jul 7 00:00] ===");
            var anchorMap = anchors.ToDictionary(a => a.Item2, a => a.Item1);
            var from = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
            foreach (int period in new[] { 10, 15, 20, 21, 25, 26, 30, 34 })
            {
                var zz = new DeviationExtremumFinder(period, provider);
                for (int i = 0; i < provider.Count; i++)
                    zz.OnCalculate(provider.GetOpenTime(i));

                List<BarPoint> inWin = zz.Extrema.Values
                    .Where(p => p.OpenTime >= from && p.OpenTime <= to).ToList();
                Console.WriteLine($"  p={period,2} ({inWin.Count} pivots):");
                foreach (BarPoint p in inWin)
                    Console.WriteLine($"    {p.OpenTime:MM-dd HH:mm} {p.Value:F6}" +
                        (anchorMap.TryGetValue(p.OpenTime, out string nm) ? $"  ← {nm}" : ""));
            }
        }
    }
}
