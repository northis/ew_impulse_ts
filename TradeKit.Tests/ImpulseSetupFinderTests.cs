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

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(m_TimeFrame, m_Symbol);
            
            // Create default impulse parameters
            ImpulseParams impulseParams = new ImpulseParams(Period: 10,
                BarsCount: 3,
                EnterRatio: 0.4,
                TakeRatio: 1.0,
                MaxZigzagPercent:20,
                MaxOverlapseLengthPercent: 30,
                HeterogeneityMax: 50,
                BreakEvenRatio: 0.5);
            
            m_SetupFinder = new ImpulseSetupFinder(m_BarsProvider, impulseParams);

            // Set up event handlers
            m_ReceivedSignals = new List<ImpulseSignalEventArgs>();
            m_TakeProfitEvents = new List<LevelEventArgs>();
            m_StopLossEvents = new List<LevelEventArgs>();
            m_BreakEvenEvents = new List<LevelEventArgs>();
            
            m_SetupFinder.OnEnter += (_, args) => m_ReceivedSignals.Add(args);
            m_SetupFinder.OnTakeProfit += (_, args) => m_TakeProfitEvents.Add(args);
            m_SetupFinder.OnStopLoss += (_, args) => m_StopLossEvents.Add(args);
            m_SetupFinder.OnBreakeven += (_, args) => m_BreakEvenEvents.Add(args);
            m_BarsProvider.BarOpened += (bp, args) =>
            {
                m_SetupFinder.CheckBar(m_BarsProvider.Count - 1);
            };
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
            
            // Create impulse (strong upward movement) - 70 bars
            for (int i = 0; i < 70; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open + 0.4 + (i % 4) * 0.2; // Strong upward bias
                double high = Math.Max(open, close) + 0.5;
                double low = Math.Min(open, close) - 0.1;
                
                Candle candle = new Candle(open, high, low, close);
                m_BarsProvider.AddCandle(candle, startTime);
                
                // Update for next candle
                currentPrice = close;
                startTime = startTime.AddMinutes(5);
            }
            
            // Create correction (partial retracement) - 80 bars
            for (int i = 0; i < 80; i++)
            {
                // Ensure smooth transition between candles
                double open = currentPrice;
                double close = open - 0.3 - (i % 3) * 0.1; // Downward correction
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

        [Test]
        public void ImpulseSetupFinder_UpwardImpulse_GeneratesSignal()
        {
            // Arrange
            CreateUpwardImpulseWithCorrection();
            
            // Act - generate a chart to visualize the pattern
            int lastIndex = m_BarsProvider.Count - 1;
            BarPoint start = new BarPoint(90, 5, m_BarsProvider);
            BarPoint end = new BarPoint(110, 9, m_BarsProvider);
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            TestContext.WriteLine($"Upward impulse chart saved to: {chartPath}");
            
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
            //int lastIndex = m_BarsProvider.Count - 1;
            //BarPoint start = new BarPoint(m_BarsProvider.GetOpenPrice(0), 0, m_BarsProvider);
            //BarPoint end = new BarPoint(m_BarsProvider.GetClosePrice(m_BarsProvider.Count - 1), m_BarsProvider.Count - 1,
            //    m_BarsProvider);
            //string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            //TestContext.WriteLine($"Downward impulse chart saved to: {chartPath}");

            // Assert
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");
            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];
            Assert.That(signal.Level.Value, Is.LessThan(110), "Entry price should be in the correction zone");
            Assert.That(signal.TakeProfit.Value, Is.LessThan(signal.Level.Value), "Take profit should be below entry for downward impulse");
            Assert.That(signal.StopLoss.Value, Is.GreaterThan(signal.Level.Value), "Stop loss should be above entry for downward impulse");
        }

        [Test]
        public void ImpulseSetupFinder_ChoppyMarket_NoSignals()
        {
            // Arrange
            CreateChoppyMarket();
            
            // Act - generate a chart to visualize the pattern
            int lastIndex = m_BarsProvider.Count - 1;
            BarPoint start = new BarPoint(100, 0, m_BarsProvider);
            BarPoint end = new BarPoint(100, lastIndex, m_BarsProvider);
            string chartPath = ChartGenerator.GenerateCandlestickChart(start, end, m_BarsProvider);
            TestContext.WriteLine($"Choppy market chart saved to: {chartPath}");
            
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
            
            // Act - add a candle that hits the take profit level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            DateTime newTime = lastTime.AddMinutes(5);
            double tpLevel = signal.TakeProfit.Value;
            
            // Create a candle that reaches the take profit level
            Candle tpCandle = new Candle(tpLevel - 1, tpLevel + 0.5, tpLevel - 1.5, tpLevel);
            m_BarsProvider.AddCandle(tpCandle, newTime);
            
            // Assert
            Assert.That(m_TakeProfitEvents.Count, Is.EqualTo(1), "Should trigger take profit event");
            Assert.That(m_SetupFinder.IsInSetup, Is.False, "Should exit setup after take profit");
        }

        [Test]
        public void ImpulseSetupFinder_StopLoss_TriggersEvent()
        {
            // Arrange
            CreateUpwardImpulseWithCorrection();
            
            // Ensure we have a signal
            Assert.That(m_ReceivedSignals.Count, Is.GreaterThan(0), "Should generate at least one signal");
            
            if (m_ReceivedSignals.Count == 0)
                return;
                
            ImpulseSignalEventArgs signal = m_ReceivedSignals[0];
            
            // Act - add a candle that hits the stop loss level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            DateTime newTime = lastTime.AddMinutes(5);
            double slLevel = signal.StopLoss.Value;
            
            // Create a candle that reaches the stop loss level
            Candle slCandle = new Candle(slLevel + 1, slLevel + 1.5, slLevel - 0.5, slLevel);
            m_BarsProvider.AddCandle(slCandle, newTime);
            
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
            
            // Calculate breakeven level (should be halfway between entry and TP)
            double beLevel = signal.BreakEvenPrice;
            
            // Act - add a candle that reaches the breakeven level
            DateTime lastTime = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
            DateTime newTime = lastTime.AddMinutes(5);
            
            // Create a candle that reaches the breakeven level
            Candle beCandle = new Candle(beLevel - 1, beLevel + 0.5, beLevel - 1.5, beLevel);
            m_BarsProvider.AddCandle(beCandle, newTime);
            
            // Assert
            Assert.That(m_BreakEvenEvents.Count, Is.EqualTo(1), "Should trigger breakeven event");
            Assert.That(m_SetupFinder.IsInSetup, Is.True, "Should remain in setup after breakeven");
            
            // Verify the stop loss was moved to breakeven
            Assert.That(signal.HasBreakeven, Is.True, "Signal should have breakeven flag set");
            Assert.That(signal.StopLoss.Value, Is.EqualTo(beLevel).Within(0.001), "Stop loss should be moved to breakeven level");
        }
    }
}
