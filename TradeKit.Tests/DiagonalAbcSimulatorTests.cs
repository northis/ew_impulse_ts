using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Deterministic checks of the corrected diagonal setup (DIAGONAL.md §9.44):
    /// after an up diagonal the impulse A goes down, its 50% correction is the entry,
    /// the stop is the start of A (the diagonal end) and 1.0/1.272 are measured from
    /// the start of A, so 1.0 lands exactly on the end of A.
    /// </summary>
    [TestFixture]
    public class DiagonalAbcSimulatorTests
    {
        private static readonly DateTime S_START =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static TestBarsProvider BuildProvider(params (double O, double H, double L, double C)[] bars)
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            for (int i = 0; i < bars.Length; i++)
            {
                (double o, double h, double l, double c) = bars[i];
                provider.AddCandle(
                    new Candle(o, h, l, c, Index: i),
                    S_START.AddHours(i));
            }

            return provider;
        }

        /// <summary>An up diagonal ending at 1.1000 on bar 20, then A down to 1.0800.</summary>
        private static (double O, double H, double L, double C)[] UpDiagonalBars(
            double correctionHigh = 1.0915,
            bool stopAfterFill = false)
        {
            var bars = new List<(double, double, double, double)>();
            for (int i = 0; i <= 20; i++)
            {
                double b = 1.0 + i * 0.005;
                double high = i == 20 ? 1.1 : b + 0.0002;
                bars.Add((b, high, b - 0.0002, b + 0.0001));
            }

            // A: down impulse to 1.0800 at bar 26 (ranges below the 0.1% zigzag step).
            bars.Add((1.0999, 1.0999, 1.0964, 1.0966)); // 21
            bars.Add((1.0966, 1.0966, 1.0931, 1.0933)); // 22
            bars.Add((1.0933, 1.0933, 1.0898, 1.0900)); // 23
            bars.Add((1.0900, 1.0900, 1.0865, 1.0867)); // 24
            bars.Add((1.0867, 1.0867, 1.0832, 1.0834)); // 25
            bars.Add((1.0834, 1.0834, 1.0800, 1.0802)); // 26

            // B: correction towards the start of A (the low pivot is confirmed on bar 28).
            bars.Add((1.0802, 1.0815, 1.0800, 1.0812)); // 27
            bars.Add((1.0812, 1.0835, 1.0810, 1.0832)); // 28
            bars.Add((1.0832, 1.0855, 1.0830, 1.0852)); // 29
            bars.Add((1.0852, 1.0875, 1.0850, 1.0872)); // 30
            bars.Add((1.0872, 1.0895, 1.0870, 1.0892)); // 31
            bars.Add((1.0892, correctionHigh, 1.0890, 1.0905)); // 32 -> fill 1.0900

            if (stopAfterFill)
            {
                bars.Add((1.0905, 1.1010, 1.0890, 1.1000)); // 33
            }
            else
            {
                bars.Add((1.0905, 1.0906, 1.0790, 1.0840)); // 33 -> TP1 1.0800
                bars.Add((1.0840, 1.0850, 1.0760, 1.0770)); // 34
                bars.Add((1.0770, 1.0780, 1.0740, 1.0750)); // 35 -> TP2 1.07456
                bars.Add((1.0750, 1.0760, 1.0730, 1.0740)); // 36
            }

            for (int i = 0; i < 10; i++)
                bars.Add((1.0740, 1.0750, 1.0730, 1.0740));

            return bars.ToArray();
        }

        /// <summary>An up diagonal ending at 1.1000 on bar 20, then A down through P0 = 1.0.</summary>
        private static (double O, double H, double L, double C)[] UpDiagonalBarsBreakingP0()
        {
            var bars = new List<(double, double, double, double)>();
            for (int i = 0; i <= 20; i++)
            {
                double b = 1.0 + i * 0.005;
                double high = i == 20 ? 1.1 : b + 0.0002;
                bars.Add((b, high, b - 0.0002, b + 0.0001));
            }

            // A: down through the diagonal's origin to 0.9900 at bar 26.
            bars.Add((1.0999, 1.0999, 1.0816, 1.0818)); // 21
            bars.Add((1.0818, 1.0818, 1.0635, 1.0637)); // 22
            bars.Add((1.0637, 1.0637, 1.0454, 1.0456)); // 23
            bars.Add((1.0456, 1.0456, 1.0273, 1.0275)); // 24
            bars.Add((1.0275, 1.0275, 1.0092, 1.0094)); // 25
            bars.Add((1.0094, 1.0094, 0.9900, 0.9902)); // 26

            // B: back above P0 to the 50% level 1.0450.
            bars.Add((0.9902, 0.9980, 0.9900, 0.9978)); // 27
            bars.Add((0.9978, 1.0056, 1.0050, 1.0054)); // 28
            bars.Add((1.0054, 1.0132, 1.0050, 1.0130)); // 29
            bars.Add((1.0130, 1.0208, 1.0200, 1.0206)); // 30
            bars.Add((1.0206, 1.0284, 1.0280, 1.0282)); // 31
            bars.Add((1.0282, 1.0360, 1.0350, 1.0358)); // 32
            bars.Add((1.0358, 1.0436, 1.0430, 1.0434)); // 33
            bars.Add((1.0434, 1.0512, 1.0500, 1.0510)); // 34 -> fill 1.0450

            // C: down through the end of A to the 1.272 extension.
            bars.Add((1.0510, 1.0512, 1.0300, 1.0310)); // 35
            bars.Add((1.0310, 1.0320, 1.0100, 1.0110)); // 36
            bars.Add((1.0110, 1.0120, 0.9900, 0.9910)); // 37 -> TP1 0.9900
            bars.Add((0.9910, 0.9930, 0.9700, 0.9710)); // 38
            bars.Add((0.9710, 0.9720, 0.9590, 0.9600)); // 39 -> TP2 0.9601

            for (int i = 0; i < 10; i++)
                bars.Add((0.9600, 0.9610, 0.9590, 0.9600));

            return bars.ToArray();
        }

        private static DiagonalAbcSetup UpDiagonalSetup(int signalBar = 20)
        {
            return new DiagonalAbcSetup(
                IsUpDiagonal: true,
                SignalBar: signalBar,
                SignalTime: S_START.AddHours(signalBar),
                P0Bar: 0,
                P0: 1.0,
                P4Bar: 17,
                P4: 1.09,
                P5Bar: 20,
                P5: 1.1,
                W3: 0.02);
        }

        private static DiagonalAbcParams LadderParams(
            double enterRatio = 0.5,
            bool ladder = true,
            double target1 = 1.0,
            double target2 = 1.272)
        {
            return new DiagonalAbcParams(
                EnterRatio: enterRatio,
                MinARatio: 0.1,
                MinABars: 3,
                AStartToleranceW3: 0.10,
                AStartBarTolerance: 2,
                StopBuffer: 0,
                Target1Ratio: target1,
                Ladder: ladder,
                Target2Ratio: target2,
                CostRate: 0,
                StressCostRate: 0.0005,
                Execution: DiagonalAbcExecution.TRIGGER,
                MaxWaitBars: 50,
                MaxHoldBars: 50);
        }

        [Test]
        public void Simulate_TradesAgainstTheDiagonal()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(), LadderParams(), timeline);

            // The diagonal is up; the traded impulse runs down.
            Assert.That(trade.IsUpDiagonal, Is.True);
            Assert.That(trade.EntryBar, Is.EqualTo(32));
            Assert.That(trade.Entry, Is.EqualTo(1.09).Within(1e-9));

            // The impulse A started at the diagonal end and ended at 1.08.
            Assert.That(trade.AStartBar, Is.EqualTo(20));
            Assert.That(trade.AStart, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(trade.AEndBar, Is.EqualTo(26));
            Assert.That(trade.AEnd, Is.EqualTo(1.08).Within(1e-9));
            Assert.That(trade.AHeight, Is.EqualTo(0.02).Within(1e-9));

            // The stop is behind the start of A = the diagonal end.
            Assert.That(trade.Stop, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(trade.Risk, Is.EqualTo(0.01).Within(1e-9));

            // 1.0 from the start of A is exactly the end of A; 1.272 is the extension.
            Assert.That(trade.Target1, Is.EqualTo(1.08).Within(1e-9));
            Assert.That(trade.Target2, Is.EqualTo(1.07456).Within(1e-9));
            Assert.That(trade.PlannedRr, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(trade.EntryDepth, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(trade.BarsToEnd, Is.EqualTo(6));
            Assert.That(trade.BarsToFill, Is.EqualTo(6));

            Assert.That(trade.Outcome, Is.EqualTo("TP"));
            Assert.That(trade.Target1Hit, Is.True);
            Assert.That(trade.Target2Hit, Is.True);
            Assert.That(trade.ExitPrice, Is.EqualTo(1.07456).Within(1e-9));

            // 50% at TP1 (1.0R) + 50% at TP2 (1.544R) = 1.272R.
            Assert.That(trade.NetR, Is.EqualTo(1.272).Within(1e-6));

            // The correction high (C) is above the 50% entry; it is not the entry.
            Assert.That(trade.BEnd, Is.EqualTo(1.0915).Within(1e-9));
            Assert.That(trade.BDepth, Is.EqualTo(0.575).Within(1e-9));
            Assert.That(trade.SetupWorked, Is.True);
            Assert.That(trade.WorkedBar, Is.EqualTo(33));
        }

        [Test]
        public void Simulate_MaxABars_RejectsLongImpulse()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(),
                LadderParams() with { MaxABars = 4 }, timeline);

            // The A leg is 6 bars (20..26); a 4-bar cap drops this candidate and the scan
            // finds no other entry, so nothing is filled.
            Assert.That(trade.EntryBar, Is.EqualTo(-1));
            Assert.That(trade.Outcome, Is.EqualTo("NOFILL"));
        }

        [Test]
        public void Simulate_MaxABars_AllowsImpulseWithinCap()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(),
                LadderParams() with { MaxABars = 10 }, timeline);

            Assert.That(trade.EntryBar, Is.EqualTo(32));
            Assert.That(trade.BarsToEnd, Is.EqualTo(6));
            Assert.That(trade.Outcome, Is.EqualTo("TP"));
        }

        [Test]
        public void Simulate_RequireSmoothA_KeepsSmoothImpulse()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);

            Assert.That(
                SmoothImpulseClassifier.IsSmoothImpulse(
                    new BarPoint(1.1, 20, provider),
                    new BarPoint(1.08, 26, provider),
                    provider),
                Is.True,
                "The fixture A must be smooth for this check to mean anything.");

            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(),
                LadderParams() with { RequireSmoothA = true }, timeline);

            Assert.That(trade.EntryBar, Is.EqualTo(32));
            Assert.That(trade.Outcome, Is.EqualTo("TP"));
        }

        [Test]
        public void Simulate_StopAtTheDiagonalEnd()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars(stopAfterFill: true));
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(), LadderParams(), timeline);

            Assert.That(trade.Outcome, Is.EqualTo("SL"));
            Assert.That(trade.ExitBar, Is.EqualTo(33));
            Assert.That(trade.ExitPrice, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(trade.NetR, Is.EqualTo(-1).Within(1e-9));
        }

        [Test]
        public void Simulate_DiagonalBroken_WhenWave5LeavesTheValidRange()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);

            var state = new DiagonalWave5State(
                new BarPoint(1.0, 0, provider),
                new BarPoint(1.005, 5, provider),
                new BarPoint(1.0, 10, provider),
                new BarPoint(1.02, 15, provider),
                new BarPoint(1.09, 18, provider),
                IsUp: true,
                W5: 1.1,
                W5Bar: 20);

            // Wave 5 is still the valid end on bars 21-25; on bar 26 it would reach the
            // |W3| cap, so the live tracker has no state there: the diagonal is broken.
            var wave5 = new Dictionary<int, DiagonalWave5State>();
            for (int b = 21; b <= 25; b++)
                wave5[b] = state;

            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(), LadderParams(), timeline, wave5);

            Assert.That(trade.EntryBar, Is.EqualTo(-1));
            Assert.That(trade.Outcome, Is.EqualTo("NOFILL"));
            Assert.That(trade.NoFillReason, Is.EqualTo("diagonalBroken"));
            Assert.That(trade.DiagonalBrokenBar, Is.EqualTo(26));
        }

        [Test]
        public void Simulate_Skip_WhenTheCorrectionMissesAndTheSecondImpulseBreaksA()
        {
            // The user's example: A ends at 1.0800, the correction reaches only 45% of A
            // (1.0890 < 1.0900), then the second impulse breaks the end of A (1.0800).
            // After that the setup has worked and a later 50% retrace must not be entered.
            var bars = new List<(double, double, double, double)>();
            bars.AddRange(UpDiagonalBars(correctionHigh: 1.0895).Take(31));
            bars.Add((1.0870, 1.0890, 1.0700, 1.0710)); // 31: breaks the end of A
            bars.Add((1.0710, 1.0910, 1.0700, 1.0900)); // 32: late 50% retrace
            bars.Add((1.0900, 1.0910, 1.0700, 1.0710)); // 33: the second impulse continues

            TestBarsProvider provider = BuildProvider(bars.ToArray());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(), LadderParams(), timeline);

            Assert.That(trade.EntryBar, Is.EqualTo(-1));
            Assert.That(trade.Outcome, Is.EqualTo("SKIP"));
            Assert.That(trade.NoFillReason, Is.EqualTo("workedNoEntry"));
            Assert.That(trade.SetupWorked, Is.True);

            // The correction reached only 45% of A, so the 50% limit was never valid.
            Assert.That(trade.BDepth, Is.EqualTo(0.45).Within(0.01));
            Assert.That(trade.WorkedPrice, Is.EqualTo(1.08).Within(1e-9));

            // A was still found and is kept for the chart.
            Assert.That(trade.AStartBar, Is.EqualTo(20));
            Assert.That(trade.AEndBar, Is.EqualTo(26));
        }

        [Test]
        public void Simulate_Invalid_WhenImpulseBreaksTheDiagonalStart()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBarsBreakingP0());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);

            // A traded through P0 = 1.0, so it is no longer an initial impulse.
            DiagonalAbcTrade invalid = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(), LadderParams(), timeline);

            Assert.That(invalid.EntryBar, Is.EqualTo(-1));
            Assert.That(invalid.Outcome, Is.EqualTo("NOFILL"));
            Assert.That(invalid.NoFillReason, Is.EqualTo("impulseBreaksP0"));
            Assert.That(invalid.AEnd, Is.EqualTo(0.99).Within(1e-9));

            // With the rule off the same A is allowed and its 50% correction is filled.
            DiagonalAbcTrade allowed = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(),
                LadderParams() with { RequireAInsideP0 = false }, timeline);

            Assert.That(allowed.NoFillReason, Is.Not.EqualTo("impulseBreaksP0"));
            Assert.That(allowed.EntryBar, Is.EqualTo(34));
            Assert.That(allowed.Entry, Is.EqualTo(1.045).Within(1e-9));
        }

        [Test]
        public void Simulate_Tp1Only()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);
            DiagonalAbcTrade trade = DiagonalAbcSimulator.Simulate(
                provider, UpDiagonalSetup(),
                LadderParams(ladder: false, target1: 1.0), timeline);

            Assert.That(trade.Outcome, Is.EqualTo("TP"));
            Assert.That(trade.Target1Hit, Is.True);
            Assert.That(trade.Target2Hit, Is.False);
            Assert.That(trade.ExitPrice, Is.EqualTo(1.08).Within(1e-9));
            Assert.That(trade.NetR, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void PivotTimeline_IsCausal()
        {
            TestBarsProvider provider = BuildProvider(UpDiagonalBars());
            DiagonalAbcPivotTimeline timeline = DiagonalAbcPivotTimeline.Build(provider, 50);

            // The A end (1.08 at bar 26) cannot be known before the correction bar.
            for (int i = 0; i < 26; i++)
            {
                Assert.That(
                    timeline.Last[i]?.Value ?? 0,
                    Is.Not.EqualTo(1.08).Within(1e-9),
                    $"bar {i} already knows the future A end");
            }

            Assert.That(timeline.Count[27], Is.GreaterThanOrEqualTo(2));
            Assert.That(timeline.Prev[27].Value, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(timeline.Last[27].Value, Is.EqualTo(1.08).Within(1e-9));
        }
    }
}
