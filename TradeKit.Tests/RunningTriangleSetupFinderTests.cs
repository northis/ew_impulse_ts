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

        private static List<ElliottWaveSignalEventArgs> RunFinder(
            TestBarsProvider provider, EWParams ewParams)
        {
            var finder = new RunningTriangleSetupFinder(
                provider, provider.BarSymbol, ewParams);
            var signals = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, args) => signals.Add(args);
            finder.MarkAsInitialized();

            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            return signals;
        }

        [Test]
        public void RunningTriangle_ReferenceExample_IsDetected()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Minute15);
            provider.LoadCandles(Path.Combine(FindDataDir(), REFERENCE_FILE));

            // Auto period (0), permissive size/bars — the running triangles here are small.
            List<ElliottWaveSignalEventArgs> signals = RunFinder(provider, new EWParams(0, 0.1, 10));

            Assert.That(signals, Is.Not.Empty, "No running-triangle setups detected.");

            // Every emitted setup must satisfy the running property: wave B breaks beyond
            // point 0 in the thrust direction (EW_R_TRIANGLE.md §4 R-B-RUN).
            foreach (ElliottWaveSignalEventArgs s in signals)
            {
                BarPoint p0 = s.WavePoints[0];
                BarPoint b = s.WavePoints[2];
                bool isUp = s.TakeProfit.Value > s.StopLoss.Value;
                bool running = isUp ? b.Value > p0.Value : b.Value < p0.Value;
                Assert.That(running, Is.True,
                    $"Setup at {p0.OpenTime:u} is not running (wave B does not break point 0).");
            }

            // The reference down-thrust: at least one BEARISH running triangle whose thrust
            // targets the reference down-move zone (≈0.6866 by 2026-06-30).
            var thrustFrom = new DateTime(2026, 6, 26, 0, 0, 0, DateTimeKind.Utc);
            var thrustTo = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            ElliottWaveSignalEventArgs? bearish = signals.FirstOrDefault(s =>
                s.TakeProfit.Value < s.StopLoss.Value          // down thrust
                && s.Level.OpenTime >= thrustFrom
                && s.Level.OpenTime <= thrustTo
                && s.TakeProfit.Value <= 0.6875);              // heads to the reference target

            Assert.That(bearish, Is.Not.Null,
                "No bearish running-triangle down-thrust toward the reference target " +
                "(≈0.6866, late June) was detected.");
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
