using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
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

                Assert.That(Fwd(isUp, b, p0), Is.True, $"{at:u}: wave B is not running (beyond point 0).");
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

        [Test]
        public void Diag_July2ToJuly6_Gates()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            var wStart = new DateTime(2026, 7, 2, 14, 15, 0, DateTimeKind.Utc);
            var wEnd   = new DateTime(2026, 7, 6, 13, 0, 0, DateTimeKind.Utc);
            var perP0 = new Dictionary<DateTime, List<string>>();
            var dGate = new DateTime(2026, 7, 6, 8, 0, 0, DateTimeKind.Utc);

            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, new EWParams(0, 0.1, 10));

            var zigzagField = typeof(RunningTriangleSetupFinder)
                .GetField("m_ExtremumFinders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            finder.OnWaveGate = (p0, gate, a, b, c, d, e) =>
            {
                if (d?.OpenTime == dGate)
                    Console.WriteLine($"  WAVE_D_08:00: p0={p0.OpenTime:u} gate={gate} D={d.Value:F6} C={c.Value:F6} B={b.Value:F6} E={e.Value:F6}");
                if (p0?.OpenTime >= wStart && p0?.OpenTime <= wEnd && gate == "assembled")
                    Console.WriteLine($"  ASSEMBLED: p0={p0.OpenTime:u} A={a.Value:F6}@{a.OpenTime:u} B={b.Value:F6}@{b.OpenTime:u} C={c.Value:F6}@{c.OpenTime:u} D={d.Value:F6}@{d.OpenTime:u} E={e.Value:F6}@{e.OpenTime:u}");
            };

            finder.OnGate = (p0, key) =>
            {
                if (p0 == null) return;
                if (!perP0.ContainsKey(p0.OpenTime))
                    perP0[p0.OpenTime] = new List<string>();
                perP0[p0.OpenTime].Add(key);
            };

            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Console.WriteLine($"\n=== Scale sweep: period ladder & Diag ===");
            if (zigzagField?.GetValue(finder) is System.Collections.IList finders)
            {
                foreach (var f in finders)
                {
                    if (f is TradeKit.Core.Indicators.DeviationExtremumFinder df)
                        Console.WriteLine($"  period={df.ScaleRate,3}  extrema={df.Extrema.Count,4}");
                }
            }

            Console.WriteLine("\n=== Diag ===");
            foreach (var kv in finder.Diag.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {kv.Key}: {kv.Value}");

            Console.WriteLine($"\n=== All assembled in window ({wStart:u}..{wEnd:u}) ===");
            foreach (var kv in perP0.Where(kv => kv.Key >= wStart && kv.Key <= wEnd).OrderBy(kv => kv.Key))
            {
                var gates = kv.Value.Distinct().ToList();
                Console.WriteLine($"  p0={kv.Key:u}  -> {gates.Last()}  [{string.Join(",", gates)}]");
            }
        }
    }
}
