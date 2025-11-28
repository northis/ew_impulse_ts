using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for the ImpulseSetupFinder class.
    /// </summary>
    internal class ImpulseSetupFinderTests
    {
        private TestBarsProvider m_BarsProvider;
        private ImpulseSetupFinder m_SetupFinder;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Minute5", "m5");
        private readonly ISymbol m_Symbol = new SymbolBase("EURUSD", "EURUSD", 1, 5, 0.0001, 0.0001, 100000);
        private List<ImpulseSignalEventArgs> m_ReceivedSignals;
        private List<LevelEventArgs> m_TakeProfitEvents;
        private List<LevelEventArgs> m_StopLossEvents;
        private List<LevelEventArgs> m_BreakEvenEvents;

        private void SetupInner(double breakeven)
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame, m_Symbol);

            // Create default impulse parameters
            ImpulseParams impulseParams = new ImpulseParams(Period: 10,
                EnterRatio: 0.4,
                TakeRatio: 1.0,
                MaxZigzagPercent: 20,
                MaxOverlapseLengthPercent: 30,
                HeterogeneityMax: 50,
                BreakEvenRatio: breakeven, 
                MinSizePercent: 0.1,
                AreaPercent: 0.5,
                BarsCount: 3);

            m_SetupFinder = new ImpulseSetupFinder(m_BarsProvider, new TestTradeViewManager(m_BarsProvider) ,impulseParams);

            // Set up event handlers
            m_ReceivedSignals = new List<ImpulseSignalEventArgs>();
            m_TakeProfitEvents = new List<LevelEventArgs>();
            m_StopLossEvents = new List<LevelEventArgs>();
            m_BreakEvenEvents = new List<LevelEventArgs>();

            m_SetupFinder.OnEnter += (_, args) => m_ReceivedSignals.Add(args);
            m_SetupFinder.OnTakeProfit += (_, args) => m_TakeProfitEvents.Add(args);
            m_SetupFinder.OnStopLoss += (_, args) => m_StopLossEvents.Add(args);
            m_SetupFinder.OnBreakeven += (_, args) => m_BreakEvenEvents.Add(args);
            m_BarsProvider.BarClosed += (bp, args) =>
            {
                m_SetupFinder.CheckBar(m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1));
            };
        }

        [SetUp]
        public void Setup()
        {
            SetupInner(0.5);
        }

        /// <summary>
        /// Creates an upward impulse pattern followed by a correction.
        /// </summary>
        private void CreateUpwardImpulseWithCorrection()
        {
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            double currentPrice = 100.0;

            // Create initial movement (before impulse) - 50 bars
            for (int i = 0; i < 50; i++)
            {
                // Ensure smooth transition between candles (close of previous = open of next)
                double open = currentPrice;
                double close = open - 0.2 - (i % 3) * 0.1; // Slight downward bias
                double high = Math.Max(open, close) + 0.1;
                double low = Math.Min(open, close) - 0.2;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }

            // Create impulse (strong upward movement) - 40 bars
            for (int i = 0; i < 40; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open + 0.2 + (i % 4) * 0.1; // Strong upward bias
                double high = Math.Max(open, close) + 0.5;
                double low = Math.Min(open, close) - 0.1;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }

            // Create correction (partial retracement) - 25 bars
            for (int i = 0; i < 25; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open - 0.2 - (i % 3) * 0.1; // Downward correction
                double high = Math.Max(open, close) + 0.2;
                double low = Math.Min(open, close) - 0.4;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }
        }

        /// <summary>
        /// Creates a downward impulse pattern followed by a correction.
        /// </summary>
        private void CreateDownwardImpulseWithCorrection()
        {
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            double currentPrice = 50.0;

            // Create initial movement (before impulse) - 50 bars
            for (int i = 0; i < 50; i++)
            {
                // Ensure smooth transition between candles (close of previous = open of next)
                double open = currentPrice;
                double close = open + 0.2 + (i % 3) * 0.1; // Slight upward bias
                double high = Math.Max(open, close) + 0.2;
                double low = Math.Min(open, close) - 0.1;

                Candle candle = new Candle(open, high, low, close, null, m_BarsProvider.Count);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }

            // Create impulse (strong downward movement) - 40 bars
            for (int i = 0; i < 40; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open - 0.2 - (i % 4) * 0.1; // Strong downward bias
                double high = Math.Max(open, close) + 0.1;
                double low = Math.Min(open, close) - 0.5;

                Candle candle = new Candle(open, high, low, close, null, m_BarsProvider.Count);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }

            // Create correction (partial retracement) - 25 bars
            for (int i = 0; i < 25; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open + 0.2 + (i % 3) * 0.1; // Upward correction
                double high = Math.Max(open, close) + 0.4;
                double low = Math.Min(open, close) - 0.2;

                Candle candle = new Candle(open, high, low, close, null, m_BarsProvider.Count);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }
        }

        /// <summary>
        /// Creates a non-impulse pattern (choppy market).
        /// </summary>
        private void CreateChoppyMarket()
        {
            DateTime startTime = new DateTime(2023, 1, 1, 10, 0, 0);
            double currentPrice = 100.0;

            // Create choppy market with overlapping candles and no clear direction - 200 bars
            for (int i = 0; i < 200; i++)
            {
                // Ensure smooth transition between candles (close of previous = open of next)
                double open = currentPrice;

                // Random oscillating movement with no clear trend
                double direction = Math.Sin(i * 0.3) * 0.3; // Oscillating direction
                double volatility = 0.2 + (i % 5) * 0.1; // Variable volatility

                double close = open + direction + ((i % 3) - 1) * 0.2; // Oscillating price
                double high = Math.Max(open, close) + volatility;
                double low = Math.Min(open, close) - volatility;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, startTime);

                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }
        }

        /// <summary>
        /// Adds bars that move toward the take profit level
        /// </summary>
        /// <param name="signal">The signal containing the take profit level</param>
        private void AddBarsTowardTakeProfit(ImpulseSignalEventArgs signal)
        {
            // Get the current price and the take profit level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            double currentPrice = m_BarsProvider.GetClosePrice(m_BarsProvider.Count - 1);
            double tpLevel = signal.TakeProfit.Value;

            // Add several candles that move toward the take profit level
            double priceStep = (tpLevel - currentPrice) / 5; // Divide the distance into 5 steps

            for (int i = 0; i < 4; i++) // Add 4 candles moving toward TP
            {
                DateTime newTime = lastTime.AddMinutes(5);
                double open = currentPrice;
                double close = open + priceStep;
                double high = Math.Max(open, close) + 0.2;
                double low = Math.Min(open, close) - 0.1;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, newTime);

                currentPrice = close;
                lastTime = newTime;
            }

            // Add the final candle that hits the take profit level
            DateTime tpTime = lastTime.AddMinutes(5);
            double tpOpen = currentPrice;
            double tpClose = tpLevel;
            double tpHigh = tpLevel + 0.5; // Make sure it exceeds the TP level
            double tpLow = tpOpen - 0.2;

            Candle tpCandle = new Candle(tpOpen, tpHigh, tpLow, tpClose);
            m_BarsProvider.AddCandle(tpCandle, tpTime);
        }

        /// <summary>
        /// Adds bars that move toward the stop loss level
        /// </summary>
        /// <param name="signal">The signal containing the stop loss level</param>
        private void AddBarsTowardStopLoss(ImpulseSignalEventArgs signal)
        {
            // Get the current price and the stop loss level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            double currentPrice = m_BarsProvider.GetClosePrice(m_BarsProvider.Count - 1);
            double slLevel = signal.StopLoss.Value;

            // For upward impulse, stop loss is below the current price
            // Add several candles that move toward the stop loss level
            double priceStep = (slLevel - currentPrice) / 5; // Divide the distance into 5 steps

            for (int i = 0; i < 4; i++) // Add 4 candles moving toward SL
            {
                DateTime newTime = lastTime.AddMinutes(5);
                double open = currentPrice;
                double close = open + priceStep; // Moving down toward SL (for upward impulse)
                double high = Math.Max(open, close) + 0.2;
                double low = Math.Min(open, close) - 0.1;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, newTime);

                currentPrice = close;
                lastTime = newTime;
            }

            // Add the final candle that hits the stop loss level
            DateTime slTime = lastTime.AddMinutes(5);
            double slOpen = currentPrice;
            double slClose = slLevel;
            double slHigh = Math.Max(slOpen, slClose) + 0.2;
            double slLow = Math.Min(slOpen, slClose) - 0.5; // Make sure it goes below the SL level

            Candle slCandle = new Candle(slOpen, slHigh, slLow, slClose);
            m_BarsProvider.AddCandle(slCandle, slTime);
        }

        /// <summary>
        /// Adds bars that move toward the breakeven level
        /// </summary>
        /// <param name="signal">The signal containing the breakeven level</param>
        private void AddBarsTowardBreakeven(ImpulseSignalEventArgs signal)
        {
            // Get the current price and the breakeven level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            double currentPrice = m_BarsProvider.GetClosePrice(m_BarsProvider.Count - 1);
            double beLevel = signal.BreakEvenPrice;

            // Add several candles that move toward the breakeven level
            double priceStep = (beLevel - currentPrice) / 5; // Divide the distance into 5 steps

            for (int i = 0; i < 4; i++) // Add 4 candles moving toward breakeven
            {
                DateTime newTime = lastTime.AddMinutes(5);
                double open = currentPrice;
                double close = open + priceStep;
                double high = Math.Max(open, close) + 0.2;
                double low = Math.Min(open, close) - 0.1;

                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, newTime);

                currentPrice = close;
                lastTime = newTime;
            }

            // Add the final candle that hits the breakeven level
            DateTime beTime = lastTime.AddMinutes(5);
            double beOpen = currentPrice;
            double beClose = beLevel;
            double beHigh = beLevel + 0.5; // Make sure it exceeds the BE level
            double beLow = beOpen - 0.2;

            Candle beCandle = new Candle(beOpen, beHigh, beLow, beClose);
            m_BarsProvider.AddCandle(beCandle, beTime);
        }

        [Test]
        public void ImpulseSetupFinder_UpwardImpulse_GeneratesSignal()
        {
            // Arrange
            CreateUpwardImpulseWithCorrection();

            // Act - generate a chart to visualize the pattern
            //int lastIndex = m_BarsProvider.Count - 1;
            //BarPoint start = new BarPoint(90, 5, m_BarsProvider);
            //BarPoint end = new BarPoint(110, 9, m_BarsProvider);
            //string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            //TestContext.WriteLine($"Upward impulse chart saved to: {chartPath}");

            // Assert
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");

            if (m_ReceivedSignals.Count > 0)
            {
                ImpulseSignalEventArgs signal = m_ReceivedSignals[0];
                Assert.That(signal.Level.Value, Is.GreaterThan(90), "Entry price should be in the correction zone");
                Assert.That(signal.TakeProfit.Value, Is.GreaterThan(signal.Level.Value), "Take profit should be above entry for upward impulse");
                Assert.That(signal.StopLoss.Value, Is.LessThan(signal.Level.Value), "Stop loss should be below entry for upward impulse");
            }
        }

        [Test]
        public void ImpulseSetupFinder_DownwardImpulse_GeneratesSignal()
        {
            // Arrange
            CreateDownwardImpulseWithCorrection();

            // Act - generate a chart to visualize the pattern

            // Assert
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");
            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];
            Assert.That(signal.Level.Value, Is.LessThan(110), "Entry price should be in the correction zone");
            Assert.That(signal.TakeProfit.Value, Is.LessThan(signal.Level.Value), "Take profit should be below entry for downward impulse");
            Assert.That(signal.StopLoss.Value, Is.GreaterThan(signal.Level.Value), "Stop loss should be above entry for downward impulse");
        }

        private string GetBarProviderChart()
        {
            BarPoint start = new BarPoint(m_BarsProvider.GetOpenPrice(0), 0, m_BarsProvider);
            BarPoint end = new BarPoint(m_BarsProvider.GetClosePrice(m_BarsProvider.Count - 1), m_BarsProvider.Count - 1,
                m_BarsProvider);
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            TestContext.WriteLine($"Downward impulse chart saved to: {chartPath}");
            return chartPath;
        }

        [Test]
        public void ImpulseSetupFinder_ChoppyMarket_NoSignals()
        {
            // Arrange
            CreateChoppyMarket();

            // Act - generate a chart to visualize the pattern
            //int lastIndex = m_BarsProvider.Count - 1;
            //BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            //BarPoint end = new BarPoint(100, lastIndex, m_BarsProvider);
            //string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            //TestContext.WriteLine($"Choppy market chart saved to: {chartPath}");

            // Assert
            Assert.That(m_ReceivedSignals.Count, Is.EqualTo(0), "Should not generate signals in choppy market");
        }

        [Test]
        public void ImpulseSetupFinder_TakeProfit_TriggersEvent()
        {
            // Arrange
            CreateUpwardImpulseWithCorrection();

            // Ensure we have a signal
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");

            if (m_ReceivedSignals.Count == 0)
                return;

            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];

            AddBarsTowardTakeProfit(signal);

            // Assert
            Assert.That(m_TakeProfitEvents.Count, Is.EqualTo(1), "Should trigger take profit event");
            Assert.That(m_SetupFinder.IsInSetup, Is.False, "Should exit setup after take profit");
        }

        [Test]
        public void ImpulseSetupFinder_StopLoss_TriggersEvent()
        {
            SetupInner(0);
            // Arrange
            CreateUpwardImpulseWithCorrection();

            // Ensure we have a signal
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");

            if (m_ReceivedSignals.Count == 0)
                return;

            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];

            AddBarsTowardStopLoss(signal);
            GetBarProviderChart();

            // Assert
            Assert.That(m_StopLossEvents.Count, Is.EqualTo(1), "Should trigger stop loss event");
            Assert.That(m_SetupFinder.IsInSetup, Is.False, "Should exit setup after stop loss");
        }

        [Test]
        public void ImpulseSetupFinder_BreakEven_TriggersEvent()
        {
            // Arrange
            CreateUpwardImpulseWithCorrection();

            // Ensure we have a signal
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");

            if (m_ReceivedSignals.Count == 0)
                return;

            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];

            AddBarsTowardBreakeven(signal);

            // Assert
            Assert.That(m_BreakEvenEvents.Count, Is.EqualTo(1), "Should trigger breakeven event");
            Assert.That(m_SetupFinder.IsInSetup, Is.True, "Should remain in setup after breakeven");

            // Verify the stop loss was moved to breakeven
            Assert.That(signal.HasBreakeven, Is.True, "Signal should have breakeven flag set");
            Assert.That(signal.StopLoss.Value, Is.EqualTo(signal.BreakEvenPrice).Within(0.001), "Stop loss should be moved to breakeven level");
        }
    }
}
