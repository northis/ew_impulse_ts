using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Telegram;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// The thin-market protection of a diagonal setup (DIAGONAL.md §4.7) and the correction of
    /// the finder by the fact the broker reports (§6.7). Runs over a slice of the archive: the
    /// slice is the same in every run, so the signals are reproducible.
    /// </summary>
    internal class DiagonalThinMarketTests
    {
        private const string H1_FILE =
            "AUDUSD_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv";

        private static readonly DateTime SLICE_FROM = new(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime SLICE_TO = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The daily rollover break the guard is built around in these tests.</summary>
        private const string DAILY_BREAK = "21:00";

        private static EWParams EwParams() => new(0, 0.1, 10);

        private static TestBarsProvider LoadProvider()
        {
            var provider = new TestBarsProvider(TimeFrameHelper.Hour1);
            provider.LoadCandles(Path.Combine(FindDataDir(), H1_FILE), SLICE_FROM, SLICE_TO);
            return provider;
        }

        /// <summary>The close time of an h1 bar — the moment a signal on it really enters.</summary>
        private static DateTime BarCloseTime(DateTime barOpen) => barOpen.AddHours(1);

        /// <summary>The close time of the bar a signal was emitted on.</summary>
        private static DateTime BarClose(BarPoint level) => BarCloseTime(level.OpenTime);

        private static string TimeOfDay(DateTime time) =>
            $"{time.Hour:D2}:{time.Minute:D2}";

        /// <summary>
        /// Runs the finder over the slice and returns the signals it emitted together with every
        /// resolved setup: the entry bar, the outcome the bars alone produced and the level it
        /// was closed at.
        /// </summary>
        private static (List<ElliottWaveSignalEventArgs> Signals,
            List<(DateTime Entry, bool IsProfit, double Level)> Resolved) Simulate(
            DiagonalSetupFinder finder, TestBarsProvider provider,
            Action<DiagonalSetupFinder, ElliottWaveSignalEventArgs> onBar = null)
        {
            var signals = new List<ElliottWaveSignalEventArgs>();
            var results = new List<(DateTime, bool, double)>();
            ElliottWaveSignalEventArgs open = null;
            finder.OnEnter += (_, e) =>
            {
                signals.Add(e);
                open = e;
            };
            finder.OnTakeProfit += (_, e) => results.Add((e.FromLevel.OpenTime, true, e.Level.Value));
            finder.OnStopLoss += (_, e) => results.Add((e.FromLevel.OpenTime, false, e.Level.Value));
            finder.MarkAsInitialized();

            for (int i = 0; i < provider.Count; i++)
            {
                finder.CheckBar(provider.GetOpenTime(i));
                if (finder.IsInSetup && open != null)
                    onBar?.Invoke(finder, open);
            }

            return (signals, results);
        }

        [TestCase(2.0, 0.0, 0.00040)]
        [TestCase(0.0, 0.5, 0.00040)]
        public void Diagonal_ThinWindow_WidensTheStop_AndRestoresItAfter(
            double spreadBufferRatio, double riskBufferRatio, double spread)
        {
            TestBarsProvider provider = LoadProvider();
            var view = new TestTradeViewManager(provider) { Bid = 0.71000, Ask = 0.71000 + spread };
            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, EwParams(),
                tradeViewManager: view,
                thinMarketGuard: new ThinMarketGuardParams(
                    true, 15, 15, spreadBufferRatio, riskBufferRatio, DAILY_BREAK, "22:00"));

            ElliottWaveSignalEventArgs signal = null;
            var edits = new List<ElliottWaveSignalEventArgs>();
            finder.OnEnter += (_, e) => signal ??= e;
            finder.OnEdit += (_, e) => edits.Add(e);
            finder.MarkAsInitialized();

            for (int i = 0; i < provider.Count && signal == null; i++)
                finder.CheckBar(provider.GetOpenTime(i));

            Assert.That(signal, Is.Not.Null, $"no diagonal setup in the slice");
            Assert.That(finder.IsInSetup, Is.True);
            Assert.That(finder.ThinStopGuard, Is.Not.Null);

            double plannedStop = signal.StopLoss.Value;
            double risk = Math.Abs(signal.Level.Value - plannedStop);
            bool isLong = signal.TakeProfit.Value > plannedStop;
            double expectedBuffer = Math.Max(spreadBufferRatio * spread, riskBufferRatio * risk);
            Assert.That(expectedBuffer, Is.GreaterThan(0), "the test has to move the stop");

            // A tick inside the window — the guard widens the stop and says so through OnEdit.
            DateTime thin = BarClose(signal.Level).Date.Add(TimeSpan.Parse("21:30"));
            while (thin < BarClose(signal.Level))
                thin = thin.AddDays(1);

            finder.CheckTick(new SymbolTickEventArgs(
                signal.Level.Value, signal.Level.Value + spread, provider.BarSymbol, thin));

            Assert.That(finder.ThinStopGuard.IsActive, Is.True);
            Assert.That(finder.ThinStopGuard.Buffer, Is.EqualTo(expectedBuffer).Within(1e-12));
            Assert.That(edits, Has.Count.EqualTo(1), "one edit when the window opens");

            double expectedStop = isLong
                ? Math.Round(plannedStop - expectedBuffer, provider.BarSymbol.Digits,
                    MidpointRounding.ToZero)
                : Math.Round(plannedStop + expectedBuffer, provider.BarSymbol.Digits,
                    MidpointRounding.ToPositiveInfinity);
            Assert.That(edits[0].StopLoss.Value, Is.EqualTo(expectedStop));
            Assert.That(edits[0].TakeProfit.Value, Is.EqualTo(signal.TakeProfit.Value),
                "the target never moves");
            Assert.That(signal.StopLoss.Value, Is.EqualTo(plannedStop),
                "the signal itself is not mutated: the result stays counted in the planned risks");

            // A tick after the window — the stop returns to its level.
            DateTime thick = thin.Date.Add(TimeSpan.Parse("23:00"));
            finder.CheckTick(new SymbolTickEventArgs(
                signal.Level.Value, signal.Level.Value + spread, provider.BarSymbol, thick));

            Assert.That(finder.ThinStopGuard.IsActive, Is.False);
            Assert.That(finder.ThinStopGuard.Buffer, Is.EqualTo(0));
            Assert.That(edits, Has.Count.EqualTo(2));
            Assert.That(edits[1].StopLoss.Value, Is.EqualTo(plannedStop));
        }

        [Test]
        public void Diagonal_ThinWindow_RefusesTheEntryInsideIt()
        {
            TestBarsProvider plainProvider = LoadProvider();
            var plainFinder = new DiagonalSetupFinder(
                plainProvider, plainProvider.BarSymbol, EwParams());
            (List<ElliottWaveSignalEventArgs> plain, _) = Simulate(plainFinder, plainProvider);

            Assert.That(plain, Is.Not.Empty, "no diagonal setup in the slice");

            // The guard is told the trading day closes exactly when the first setup would enter.
            DateTime blockedEntry = plain[0].Level.OpenTime;
            string breakTime = TimeOfDay(BarCloseTime(blockedEntry));

            TestBarsProvider provider = LoadProvider();
            var finder = new DiagonalSetupFinder(
                provider, provider.BarSymbol, EwParams(),
                tradeViewManager: new TestTradeViewManager(provider),
                thinMarketGuard: new ThinMarketGuardParams(
                    true, 1, 1, 2.0, 0.25, breakTime, breakTime));

            (List<ElliottWaveSignalEventArgs> guarded, _) = Simulate(finder, provider);

            Assert.That(finder.Diag.GetValueOrDefault("thinMarketWindow"), Is.GreaterThan(0),
                "the window has to be reported in the funnel");
            Assert.That(guarded.Select(a => a.Level.OpenTime), Has.None.EqualTo(blockedEntry),
                "no entry inside the thin window");
            Assert.That(guarded, Is.Not.Empty,
                "the candidate is parked by the window, the finder is not switched off");
        }

        [Test]
        public void Diagonal_BrokerStopOut_OverridesTheSimulatedTakeProfit()
        {
            // What the bars alone say: this setup ends in profit.
            TestBarsProvider plainProvider = LoadProvider();
            var plainFinder = new DiagonalSetupFinder(
                plainProvider, plainProvider.BarSymbol, EwParams());
            (_, List<(DateTime Entry, bool IsProfit, double Level)> plain) =
                Simulate(plainFinder, plainProvider);

            (DateTime Entry, bool IsProfit, double Level) target =
                plain.FirstOrDefault(a => a.IsProfit);
            Assert.That(target.Entry, Is.Not.EqualTo(default(DateTime)),
                "no setup resolves in profit in the slice");

            // The same run, but the position behind that setup was swept by the spread of a
            // thin market and filled half a risk past the stop.
            const double slipR = 0.5;
            TestBarsProvider provider = LoadProvider();
            var finder = new DiagonalSetupFinder(provider, provider.BarSymbol, EwParams());
            double slippedStop = 0;
            bool done = false;
            ElliottWaveSignalEventArgs correctedSignal = null;

            (_, List<(DateTime Entry, bool IsProfit, double Level)> guarded) = Simulate(
                finder, provider,
                (sf, open) =>
                {
                    if (done || open.Level.OpenTime != target.Entry)
                        return;

                    done = true;
                    correctedSignal = open;
                    bool isLong = open.TakeProfit.Value > open.StopLoss.Value;
                    double risk = Math.Abs(open.Level.Value - open.StopLoss.Value);
                    slippedStop = open.StopLoss.Value + (isLong ? -1 : 1) * slipR * risk;
                    sf.NotifyPositionClosed(
                        open,
                        new ClosedPositionEventArgs(PositionClosedState.STOP_LOSS, null, slippedStop),
                        slippedStop);
                });

            Assert.That(done, Is.True, "the setup was not reached");
            Assert.That(finder.IsInSetup, Is.False, "the finder has to let the setup go");
            Assert.That(finder.CurrentSignalEventArgs, Is.Null);

            (DateTime Entry, bool IsProfit, double Level)[] ofTarget =
                guarded.Where(a => a.Entry == target.Entry).ToArray();
            Assert.That(ofTarget, Has.Length.EqualTo(1), "one setup — one outcome");
            Assert.That(ofTarget[0].IsProfit, Is.False,
                "the simulated take profit must not be reported on a stopped-out position");
            Assert.That(ofTarget[0].Level, Is.EqualTo(slippedStop).Within(1e-12),
                "the exit is reported at the real fill, so the slippage is countable");

            // The statistics count that fill as one and a half risks, not as one.
            var args = new TelegramReporter.SignalArgs
            {
                SignalEventArgs = correctedSignal,
                PipSize = provider.BarSymbol.PipSize,
                Digits = provider.BarSymbol.Digits,
                SymbolName = provider.BarSymbol.Name
            };
            Assert.That(TelegramReporter.GetSetupResult(args, slippedStop),
                Is.EqualTo(-(1 + slipR)).Within(1e-9));
        }

        /// <summary>
        /// Locates the repo <c>data/</c> folder the same way
        /// <see cref="DiagonalSetupFinderTests"/> does.
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
