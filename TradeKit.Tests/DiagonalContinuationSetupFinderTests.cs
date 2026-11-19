using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Checks the wrapper that suppresses the counter-trend diagonal signal for the
    /// continuation robot (DIAGONAL.md §9.43). The wrapper must never raise its own
    /// <c>OnEnter</c>, but it must store the diagonal context for the continuation manager.
    /// </summary>
    public class DiagonalContinuationSetupFinderTests
    {
        private const string H1_FILE =
            "AUDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv";

        [Test]
        public void ContinuationWrapper_SuppressesCounterTrendAndStoresSignal()
        {
            string dataDir = FindDataDir();
            string path = Path.Combine(dataDir, H1_FILE);
            Assert.That(File.Exists(path), Is.True, $"Missing archive file: {path}");

            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            provider.LoadCandles(path);

            var finder = new DiagonalContinuationSetupFinder(
                provider,
                provider.BarSymbol,
                new EWParams(0, 0.1, 10),
                BuildExactParams(DiagonalInitialMode.LEADING));

            int counterTrendEnters = 0;
            finder.OnEnter += (_, _) => counterTrendEnters++;

            finder.MarkAsInitialized();
            for (int i = 0; i < provider.Count; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(counterTrendEnters, Is.Zero,
                "The wrapper must not forward the counter-trend OnEnter.");
            Assert.That(finder.CurrentDiagonalSignal, Is.Not.Null,
                "The wrapper must store the diagonal context for the continuation manager.");

            var signal = finder.ConsumeDiagonalSignal();
            Assert.That(signal, Is.Not.Null);
            Assert.That(signal.WavePoints.Length, Is.EqualTo(6));
            Assert.That(signal.WavePoints[0].BarIndex, Is.LessThan(signal.WavePoints[5].BarIndex));
            Assert.That(finder.ConsumeDiagonalSignal(), Is.Null,
                "Consuming the signal must clear it.");
        }

        private static DiagonalParams BuildExactParams(DiagonalInitialMode mode)
        {
            return new DiagonalParams(
                TakeProfitRatio: 1.0,
                TakeProfitMode: DiagonalTakeProfitMode.DIAGONAL_RETRACE,
                MinConvergence: 0,
                MaxConvergence: 0,
                RequireInsideWedge: false,
                MaxSpillAreaRatio: 1.0,
                RequireWave5Ratio: false,
                RequireWave4Ratio: false,
                InitialMode: mode,
                MinWave3Penetration: 0.03,
                MaxWaveDurationRatio: 8.0,
                RetraceAction: DiagonalRetraceAction.NONE,
                MinRiskRewardRatio: 0,
                Wave3RetraceRatio: 0,
                MinWave4Wave2Level: 0,
                RequireWave4Shorter: true,
                RequireWave2Shorter: false,
                MinWave2Retrace: 0.5,
                MaxWave2Retrace: 0,
                MaxWave5SpillRatio: 0,
                MinWave4Wave2DurationRatio: 0,
                MinWave3Wave1DurationRatio: 0,
                MinWave2Wave1DurationRatio: 0,
                RequireThickWaveEnds: false,
                ThinMarketGuard: null,
                TakeProfitRetraceRatio: 0.236,
                StopWave3Ratio: 1.0,
                MinWave4RetraceW3: 0,
                MaxWave4RetraceW3: 0.382,
                TriggerMode: DiagonalTriggerMode.WAVE3_BREAK,
                Wave5SpillMode: DiagonalWave5SpillMode.ALL,
                Wave5SpillThreshold: 0.005,
                MinSignalRiskRewardRatio: 0);
        }

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
