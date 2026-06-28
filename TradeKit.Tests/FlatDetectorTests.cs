using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests <see cref="FlatDetector"/> against real price data from
    /// <c>data/</c> — verifies that the detector correctly identifies
    /// impulses that are waves C of flat patterns.
    /// </summary>
    [TestFixture]
    public class FlatDetectorTests
    {
        private const string DATA_DIR = "data";
        private const string TEST_FILE = "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv";
        private const int MAX_BARS = 2000; // first N bars only — fast enough for CI

        private TestBarsProvider m_BarsProvider;
        private DeviationExtremumFinder m_Finder;

        [SetUp]
        public void Setup()
        {
            var timeFrame = TimeFrameHelper.Hour1;
            var symbol = new SymbolBase("EURUSD", "EUR/USD", 1, 5, 0.0001, 0.0001, 100_000);

            m_BarsProvider = new TestBarsProvider(timeFrame, symbol);

            string csvPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", DATA_DIR, TEST_FILE);

            if (!File.Exists(csvPath))
            {
                Assert.Inconclusive($"Data file not found: {csvPath}.  " +
                    "Run from repo root or adjust the relative path.");
            }

            // Load only first MAX_BARS bars via a two-pass date-range load.
            var dummyProvider = new TestBarsProvider(timeFrame, symbol);
            dummyProvider.LoadCandles(csvPath);
            int actualBars = Math.Min(dummyProvider.Count, MAX_BARS);
            DateTime toDate = dummyProvider.GetOpenTime(actualBars - 1);
            DateTime fromDate = dummyProvider.GetOpenTime(0);
            m_BarsProvider.LoadCandles(csvPath, fromDate, toDate);

            // Use a DeviationExtremumFinder similar to ImpulseSetupFinder.
            m_Finder = new DeviationExtremumFinder(10, m_BarsProvider);

            // Feed all bars.
            for (int i = 0; i < m_BarsProvider.Count; i++)
            {
                DateTime t = m_BarsProvider.GetOpenTime(i);
                m_Finder.OnCalculate(t);
            }
        }

        [Test]
        public void FlatDetector_FindsAtLeastOneExtendedFlat()
        {
            int extendedFound = 0;
            int runningFound = 0;
            int totalCandidates = 0;

            var extrema = m_Finder.Extrema;
            int count = extrema.Count;

            for (int cEndIdx = count - 2; cEndIdx >= 3; cEndIdx--)
            {
                BarPoint endItem = extrema.Values[cEndIdx];
                BarPoint startItem = extrema.Values[cEndIdx - 1];

                // Impulse direction.
                bool isUp = endItem.Value > startItem.Value;
                if (!isUp) continue; // only test up-impulses for simplicity

                // Use the actual IsInitialMovement logic.
                bool isInit = m_IsInitialMovement(
                    startItem.Value, endItem.Value, startItem.BarIndex,
                    m_BarsProvider, out var edgeExtremum);

                if (!isInit || edgeExtremum == null)
                    continue;

                totalCandidates++;

                bool isFlat = FlatDetector.IsFlatWaveC(
                    m_BarsProvider, startItem, endItem, edgeExtremum,
                    out ElliottModelType? flatType);

                if (isFlat)
                {
                    if (flatType == ElliottModelType.FLAT_EXTENDED)
                        extendedFound++;
                    else if (flatType == ElliottModelType.FLAT_RUNNING)
                        runningFound++;

                    TestContext.WriteLine(
                        $"FLAT {flatType}: impulse {startItem.BarIndex}→{endItem.BarIndex} " +
                        $"({startItem.Value:F5}→{endItem.Value:F5}), " +
                        $"edge at bar {edgeExtremum.Index}");
                }
            }

            TestContext.WriteLine(
                $"Total candidates={totalCandidates}, " +
                $"FLAT_EXTENDED={extendedFound}, FLAT_RUNNING={runningFound}");

            Assert.That(totalCandidates, Is.GreaterThan(10),
                "Should have at least 10 initial-impulse candidates in 2000 bars.");
            Assert.That(extendedFound + runningFound, Is.GreaterThan(0),
                "Should find at least one flat on EURUSD H1 in 2000 bars.");
        }

        [Test]
        public void FlatDetector_StrictRatios_RejectOutOfRange()
        {
            // Test via internal method: A-B-C with C/A=2.0 (not in strict set).
            // A 1.1000→1.1100, B 1.1100→1.0950, C 1.0950→1.1150
            // C/A = 0.0020/0.0010 = 2.0 → not in {0.618,1,1.618,2.618}±10%.
            var pivots = new List<BarPoint>
            {
                new(1.1000, default, null, 0),  // A start
                new(1.1100, default, null, 1),  // A end / B start
                new(1.0950, default, null, 2),  // B end / C start
                new(1.1150, default, null, 3),  // C end
            };

            bool isFlat = FlatDetector.TryDetectFromPivots(
                pivots, isUp: true, extended: true,
                out ElliottModelType? _);

            Assert.That(isFlat, Is.False,
                "C/A=2.0 not in {0.618,1,1.618,2.618}±10% → not a strict flat.");
        }

        [Test]
        public void FlatDetector_ValidExtendedFlat_Detected()
        {
            // C/A = 0.0016/0.0010 = 1.6 → within 1.618±10% ✓.
            var pivots = new List<BarPoint>
            {
                new(1.1000, default, null, 0),  // A start
                new(1.1100, default, null, 1),  // A end / B start
                new(1.0950, default, null, 2),  // B end / C start  (overshoots 1.1000 ✓)
                new(1.1110, default, null, 3),  // C end (past A end 1.1100 ✓)
            };

            bool isFlat = FlatDetector.TryDetectFromPivots(
                pivots, isUp: true, extended: true,
                out ElliottModelType? flatType);

            Assert.That(isFlat, Is.True,
                "C/A=1.6 within 1.618±10%, B overshoots → FLAT_EXTENDED.");
            Assert.That(flatType, Is.EqualTo(ElliottModelType.FLAT_EXTENDED));
        }

        // ---- helpers ----------------------------------------------------------

        /// <summary>Replica of SingleSetupFinder.IsInitialMovement for testing.</summary>
        private static bool m_IsInitialMovement(
            double startValue, double endValue, int startIndex, IBarsProvider bp,
            out Candle edgeExtremum)
        {
            bool isImpulseUp = endValue > startValue;
            edgeExtremum = null;

            for (int i = startIndex - 1; i >= 0; i--)
            {
                edgeExtremum = Candle.FromIndex(bp, i);
                if (isImpulseUp)
                {
                    if (edgeExtremum.L <= startValue) break;
                    if (edgeExtremum.H - endValue > 0) return true;
                }
                else
                {
                    if (edgeExtremum.H >= startValue) break;
                    if (edgeExtremum.L - endValue < 0) return true;
                }
            }
            return false;
        }
    }
}
