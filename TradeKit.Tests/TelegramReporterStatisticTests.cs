using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Telegram;

namespace TradeKit.Tests
{
    /// <summary>
    /// The result of a setup is counted in the risks planned at the entry, by the price the
    /// position was <b>really</b> closed at (DIAGONAL.md §6.7): a stop swept with slippage is
    /// more than one risk, a part closed early is already realized.
    /// </summary>
    internal class TelegramReporterStatisticTests
    {
        private const double ENTRY = 1.20000;

        private static TelegramReporter.SignalArgs Build(double takeProfit, double stopLoss)
        {
            var level = new BarPoint(ENTRY, new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc),
                TimeFrameHelper.Hour1, 0);
            var tp = new BarPoint(takeProfit, level.OpenTime, level.BarTimeFrame, 0);
            var sl = new BarPoint(stopLoss, level.OpenTime, level.BarTimeFrame, 0);
            return new TelegramReporter.SignalArgs
            {
                SignalEventArgs = new SignalEventArgs(level, tp, sl),
                PipSize = 0.0001,
                Digits = 5,
                SymbolName = "EURUSD"
            };
        }

        [Test]
        public void ShortSetup_PlannedLevels()
        {
            // A short: the stop is above the entry, the target below it.
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.19500, stopLoss: 1.20500);

            Assert.That(TelegramReporter.GetSetupResult(args, 1.20500), Is.EqualTo(-1).Within(1e-9));
            Assert.That(TelegramReporter.GetSetupResult(args, 1.19500), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void ShortSetup_SlippagePastTheStopIsMoreThanOneRisk()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.19500, stopLoss: 1.20500);

            // The spread of a thin market swept the stop and filled half a risk further.
            Assert.That(TelegramReporter.GetSetupResult(args, 1.20750), Is.EqualTo(-1.5).Within(1e-9));
            Assert.That(TelegramReporter.GetSetupResult(args, 1.21000), Is.EqualTo(-2).Within(1e-9));
        }

        [Test]
        public void LongSetup_SlippagePastTheStopIsMoreThanOneRisk()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.20500, stopLoss: 1.19500);

            Assert.That(TelegramReporter.GetSetupResult(args, 1.19500), Is.EqualTo(-1).Within(1e-9));
            Assert.That(TelegramReporter.GetSetupResult(args, 1.19250), Is.EqualTo(-1.5).Within(1e-9));
            Assert.That(TelegramReporter.GetSetupResult(args, 1.20500), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void AStopFilledBetterThanPlanned_IsLessThanOneRisk()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.20500, stopLoss: 1.19500);

            Assert.That(TelegramReporter.GetSetupResult(args, 1.19700), Is.EqualTo(-0.6).Within(1e-9));
        }

        [Test]
        public void Breakeven_IsZero()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.20500, stopLoss: 1.19500);

            Assert.That(TelegramReporter.GetSetupResult(args, ENTRY), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void ThePartClosedEarlyIsAlreadyRealized()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.20500, stopLoss: 1.19500);
            args.ClosedPart = 0.5;
            args.ClosedResult = 0.5;

            // Half of the position took +1R, the rest was stopped at −1R — the setup is even.
            Assert.That(TelegramReporter.GetSetupResult(args, 1.19500), Is.EqualTo(0).Within(1e-9));
            Assert.That(TelegramReporter.GetSetupResult(args, 1.20500), Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void DegenerateRisk_IsZero()
        {
            TelegramReporter.SignalArgs args = Build(takeProfit: 1.20500, stopLoss: ENTRY);

            Assert.That(TelegramReporter.GetSetupResult(args, 1.19500), Is.EqualTo(0));
        }
    }
}
