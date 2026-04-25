using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Signals;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for <see cref="ParseSetupFinder"/>: message-to-signal parsing, breakeven, TP/SL registration.
    /// </summary>
    [TestFixture]
    internal class ParseSetupFinderTests
    {
        // XAUUSD pip size 0.01, default SL/TP = 1000 pips = $10
        private static readonly ITimeFrame TIMEFRAME = new TimeFrameBase("Minute", "m1");
        private static readonly ISymbol SYMBOL = new SymbolBase("XAUUSD", "XAUUSD", 1, 2, 0.01, 0.01, 100);

        private TestBarsProvider m_BarsProvider;
        private ParseSetupFinder m_SetupFinder;
        private List<SignalEventArgs> m_Entries;
        private List<LevelEventArgs> m_TakeProfits;
        private List<LevelEventArgs> m_StopLosses;
        private List<LevelEventArgs> m_Breakevens;
        private List<LevelEventArgs> m_ManualCloses;
        private string m_TempSignalsFile;

        private static string RealCsvPath => Path.Combine(
            TestContext.CurrentContext.TestDirectory, "TestData",
            "XAUUSD_m1_2022-06-01T00-00-00_2026-04-04T00-00-00.csv");

        private static string RealSignalsPath => Path.Combine(
            TestContext.CurrentContext.TestDirectory, "TestData", "signals.json");

        [SetUp]
        public void Setup()
        {
            m_BarsProvider = new TestBarsProvider(TIMEFRAME, SYMBOL);
            m_Entries = new List<SignalEventArgs>();
            m_TakeProfits = new List<LevelEventArgs>();
            m_StopLosses = new List<LevelEventArgs>();
            m_Breakevens = new List<LevelEventArgs>();
            m_ManualCloses = new List<LevelEventArgs>();
            m_TempSignalsFile = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(m_TempSignalsFile))
                File.Delete(m_TempSignalsFile);
        }

        /// <summary>
        /// Writes a fixture JSON with the given messages (SymbolDataExportJson format) and
        /// wires up ParseSetupFinder events. Call BEFORE adding any candles.
        /// </summary>
        private void InitFinderWithMessages(IEnumerable<(long id, long? replyId, DateTime date, string text)> messages,
            int takeProfitIndex = 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{\"messages\":[");
            bool first = true;
            foreach (var (id, replyId, date, text) in messages)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                string escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
                string replyField = replyId.HasValue ? replyId.Value.ToString() : "null";
                sb.Append($"{{\"id\":{id},\"date\":\"{date:yyyy-MM-ddTHH:mm:sszzz}\",\"reply_to_msg_id\":{replyField},\"text\":\"{escapedText}\",\"type\":\"message\"}}");
            }
            sb.AppendLine("]}");
            File.WriteAllText(m_TempSignalsFile, sb.ToString());

            var tradeViewManager = new TestTradeViewManager(m_BarsProvider);
            m_SetupFinder = new ParseSetupFinder(m_BarsProvider, SYMBOL, tradeViewManager, m_TempSignalsFile,
                takeProfitIndex: takeProfitIndex);
            m_SetupFinder.OnEnter += (_, args) => m_Entries.Add(args);
            m_SetupFinder.OnTakeProfit += (_, args) => m_TakeProfits.Add(args);
            m_SetupFinder.OnStopLoss += (_, args) => m_StopLosses.Add(args);
            m_SetupFinder.OnBreakeven += (_, args) => m_Breakevens.Add(args);
            m_SetupFinder.OnManualClose += (_, args) => m_ManualCloses.Add(args);
            m_SetupFinder.MarkAsInitialized();

            m_BarsProvider.BarClosed += (_, _) =>
            {
                var dt = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
                m_SetupFinder.CheckBar(dt);
            };
        }

        /// <summary>
        /// Adds a sequence of 1-minute candles starting at <paramref name="start"/> (UTC).
        /// Each candle spans [open, close] using simple flat OHLC for predictable TP/SL hits.
        /// </summary>
        private void AddMinuteCandles(DateTime start, int count, double open, double high, double low, double close)
        {
            for (int i = 0; i < count; i++)
            {
                var candle = new Candle(open, high, low, close, null, 0);
                m_BarsProvider.AddCandle(candle, start.AddMinutes(i));
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Signal parsing unit tests (minimal candle data, fixture JSON)
        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void ParseSignals_BuyEntry_FiresEnterWithCorrectPrices()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tSignal = t0.AddMinutes(1);

            InitFinderWithMessages(new[]
            {
                (1L, (long?)null, tSignal, "XAUUSD buy now @ 1900\ntp @ 1910\ntp2 @ 1920\nsl @ 1890")
            });

            // Need at least 2 bars so prevBarDateTime is valid; signal falls between bar 0 and bar 1
            AddMinuteCandles(t0, 3, 1900, 1905, 1895, 1902);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "OnEnter should fire for a buy signal");
            SignalEventArgs entry = m_Entries[0];
            Assert.That(entry.Level.Value, Is.EqualTo(1900).Within(0.01), "Entry price");
            Assert.That(entry.TakeProfit.Value, Is.EqualTo(1910).Within(0.01), "TP price");
            Assert.That(entry.StopLoss.Value, Is.EqualTo(1890).Within(0.01), "SL price");
        }

        [Test]
        public void ParseSignals_SellEntry_FiresEnterWithCorrectDirection()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tSignal = t0.AddMinutes(1);

            InitFinderWithMessages(new[]
            {
                (1L, (long?)null, tSignal, "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\nsl @ 1960")
            });

            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1));
            SignalEventArgs entry = m_Entries[0];
            Assert.That(entry.Level.Value, Is.EqualTo(1950).Within(0.01));
            Assert.That(entry.TakeProfit.Value, Is.EqualTo(1940).Within(0.01));
            Assert.That(entry.StopLoss.Value, Is.EqualTo(1960).Within(0.01));
            // Sell: TP < SL
            Assert.That(entry.TakeProfit.Value, Is.LessThan(entry.Level.Value));
            Assert.That(entry.StopLoss.Value, Is.GreaterThan(entry.Level.Value));
        }

        [Test]
        public void ParseSignals_LimitOrder_IsSkippedNoEnterEvent()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tSignal = t0.AddMinutes(1);

            InitFinderWithMessages(new[]
            {
                (1L, (long?)null, tSignal, "XAUUSD sell limit @ 1950\ntp @ 1940\nsl @ 1960")
            });

            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.Zero, "Limit orders should be skipped");
        }

        [Test]
        public void ParseSignals_BreakevenReply_EntryPointPhrase_SetsBreakeven()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tBE = t0.AddMinutes(3);

            // Use "entry point" keyword (current BREAKEVEN_REGEX matches it)
            InitFinderWithMessages(new[]
            {
                (10L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\nsl @ 1960"),
                (11L, (long?)10L, tBE, "XAUUSD 55+ pips running now Move sl to entry point 1:1RR")
            });

            AddMinuteCandles(t0, 5, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_Breakevens.Count, Is.GreaterThanOrEqualTo(1), "Breakeven should fire");
        }

        [Test]
        public void ParseSignals_BreakevenReply_MoveSlToEntry_SetsBreakeven()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tBE = t0.AddMinutes(3);

            // "move sl to entry" matches the updated BREAKEVEN_REGEX
            InitFinderWithMessages(new[]
            {
                (20L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\nsl @ 1960"),
                (21L, (long?)20L, tBE, "XAUUSD Running 40+ Pips move sl to entry close half lot")
            });

            AddMinuteCandles(t0, 5, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_Breakevens.Count, Is.GreaterThanOrEqualTo(1), "Breakeven via 'move sl to entry' should fire");
        }

        [Test]
        public void ParseSignals_BreakevenReply_BEAbbreviation_SetsBreakeven()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tBE = t0.AddMinutes(3);

            // "b.e" abbreviation - now covered by updated BREAKEVEN_REGEX
            InitFinderWithMessages(new[]
            {
                (30L, (long?)null, tEntry, "XAUUSD buy now @ 1900\ntp @ 1910\nsl @ 1890"),
                (31L, (long?)30L, tBE, "XAUUSD Activated And running 65+ pips 1:1RR Hit B.E")
            });

            AddMinuteCandles(t0, 5, 1905, 1908, 1895, 1906);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_Breakevens.Count, Is.GreaterThanOrEqualTo(1), "Breakeven via 'b.e' should fire");
        }

        [Test]
        public void ParseSignals_ExplicitTpHitReply_FiresTakeProfit()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tTpHit = t0.AddMinutes(5);

            InitFinderWithMessages(new[]
            {
                (40L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\nsl @ 1960"),
                (41L, (long?)40L, tTpHit, "XAUUSD Tp hit Running 55+ Pips")
            });

            // Price never reaches the TP level naturally, so only the explicit reply fires TP
            AddMinuteCandles(t0, 7, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_TakeProfits.Count, Is.GreaterThanOrEqualTo(1), "TP event should fire on 'Tp hit' reply");
        }

        [Test]
        public void ParseSignals_ExplicitSlHitReply_FiresStopLoss()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tSlHit = t0.AddMinutes(5);

            InitFinderWithMessages(new[]
            {
                (50L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\nsl @ 1960"),
                (51L, (long?)50L, tSlHit, "SL Hit bad Move of gold. Wait for recovery trade guys")
            });

            // Price never hits SL naturally, so only the reply fires SL
            AddMinuteCandles(t0, 7, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_StopLosses.Count, Is.GreaterThanOrEqualTo(1), "SL event should fire on 'SL hit' reply");
        }

        [Test]
        public void ParseSignals_CloseNowReply_FiresManualClose()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);
            var tClose = t0.AddMinutes(4);

            InitFinderWithMessages(new[]
            {
                (60L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\nsl @ 1960"),
                (61L, (long?)60L, tClose, "Close now in profit")
            });

            AddMinuteCandles(t0, 6, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry should fire");
            Assert.That(m_ManualCloses.Count, Is.GreaterThanOrEqualTo(1), "Manual close should fire on 'close now' reply");
        }

        [Test]
        public void ParseSignals_TpHitByPriceMovement_FiresTakeProfitEvent()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);

            // Sell entry: tp=1940, sl=1960, enter=1950
            InitFinderWithMessages(new[]
            {
                (70L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\nsl @ 1960")
            });

            // First few candles within the range then one that touches below TP
            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);
            // This candle LOW goes below the TP level (1940) → should fire TP
            m_BarsProvider.AddCandle(new Candle(1945, 1947, 1938, 1939, null, 0), t0.AddMinutes(3));

            Assert.That(m_TakeProfits.Count, Is.GreaterThanOrEqualTo(1), "TP should fire when price crosses TP via new bar");
        }

        [Test]
        public void ParseSignals_SlHitByPriceMovement_FiresStopLossEvent()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tEntry = t0.AddMinutes(1);

            // Sell entry: tp=1940, sl=1960, enter=1950
            InitFinderWithMessages(new[]
            {
                (80L, (long?)null, tEntry, "XAUUSD sell now @ 1950\ntp @ 1940\nsl @ 1960")
            });

            AddMinuteCandles(t0, 3, 1950, 1952, 1945, 1950);
            // This candle HIGH exceeds SL (1960) → should fire SL
            m_BarsProvider.AddCandle(new Candle(1955, 1965, 1953, 1962, null, 0), t0.AddMinutes(3));

            Assert.That(m_StopLosses.Count, Is.GreaterThanOrEqualTo(1), "SL should fire when price crosses SL level");
        }

        [Test]
        public void ParseSignals_WholeNumberSlPrice_IsParsedCorrectly()
        {
            // Regression test: SL_REGEX previously required a decimal point, missing whole-number SL prices.
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tSignal = t0.AddMinutes(1);

            InitFinderWithMessages(new[]
            {
                (90L, (long?)null, tSignal, "XAUUSD buy now @ 1900\ntp @ 1910\nsl @ 1890")
            });

            AddMinuteCandles(t0, 3, 1900, 1905, 1895, 1902);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1));
            // Whole-number SL (1890) must be parsed, not replaced by default ±10
            Assert.That(m_Entries[0].StopLoss.Value, Is.EqualTo(1890).Within(0.01),
                "Whole-number SL (no decimal) should be parsed correctly");
        }

        [Test]
        public void ParseSignals_PriceSeparatorDash_IsParsedCorrectly()
        {
            // Some messages use dash as separator: "XAUUSD sell now - 1654"
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            var tSignal = t0.AddMinutes(1);

            InitFinderWithMessages(new[]
            {
                (100L, (long?)null, tSignal, "XAUUSD sell now - 1950\nTp - 1945\nTp - 1940\nSl - 1955")
            });

            AddMinuteCandles(t0, 3, 1950, 1952, 1944, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1), "Entry with dash separator should fire");
            Assert.That(m_Entries[0].Level.Value, Is.EqualTo(1950).Within(0.01),
                "Entry price with dash separator should be parsed");
        }

        [Test]
        public void ParseSignals_MultipleSignals_EachFiresEnter()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);

            InitFinderWithMessages(new[]
            {
                (110L, (long?)null, t0.AddMinutes(1), "XAUUSD sell now @ 1950\ntp @ 1940\nsl @ 1960"),
                (111L, (long?)null, t0.AddMinutes(5), "XAUUSD buy now @ 1935\ntp @ 1945\nsl @ 1925")
            });

            AddMinuteCandles(t0, 8, 1950, 1955, 1930, 1940);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(2), "Each independent entry signal should fire OnEnter");
        }

        [Test]
        public void ParseSignals_TakeProfitIndex1_UsesClosestTp()
        {
            // Signal has 3 TPs: 1940 (closest), 1930, 1920 (farthest). Entry sell @ 1950.
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            InitFinderWithMessages(new[]
            {
                (100L, (long?)null, t0.AddMinutes(1),
                    "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\ntp3 @ 1920\nsl @ 1960")
            }, takeProfitIndex: 1);
            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(m_Entries[0].TakeProfit.Value, Is.EqualTo(1940).Within(0.01),
                "Index 1 should select the closest TP (1940)");
        }

        [Test]
        public void ParseSignals_TakeProfitIndex2_UsesSecondClosestTp()
        {
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            InitFinderWithMessages(new[]
            {
                (101L, (long?)null, t0.AddMinutes(1),
                    "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1930\ntp3 @ 1920\nsl @ 1960")
            }, takeProfitIndex: 2);
            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(m_Entries[0].TakeProfit.Value, Is.EqualTo(1930).Within(0.01),
                "Index 2 should select the second-closest TP (1930)");
        }

        [Test]
        public void ParseSignals_TakeProfitIndexExceedsCount_UsesFarthestTp()
        {
            // Only 2 TPs available but index=5 requested → farthest (1920) should be used.
            var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
            InitFinderWithMessages(new[]
            {
                (102L, (long?)null, t0.AddMinutes(1),
                    "XAUUSD sell now @ 1950\ntp @ 1940\ntp2 @ 1920\nsl @ 1960")
            }, takeProfitIndex: 5);
            AddMinuteCandles(t0, 3, 1950, 1955, 1945, 1948);

            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(m_Entries[0].TakeProfit.Value, Is.EqualTo(1920).Within(0.01),
                "When index exceeds TP count the farthest TP should be used");
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Integration test: real CSV + real signals.json (narrow time slice)
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scenario: signal #737 (sell @ 1655.50, TP 1651, SL 1659) on 2022-10-18 13:22 UTC
        /// Reply #741 at 16:56 UTC: "Move sl to entry point" → breakeven
        /// Reply #747 at 17:26 UTC: "XAUUSD Tp hit" → TP
        /// We load only the 2022-10-18 daily slice to keep the test fast.
        /// </summary>
        [Test]
        [Category("Integration")]
        public void Integration_RealData_Signal737_BreakevenThenTpHit()
        {
            if (!File.Exists(RealCsvPath))
                Assert.Ignore("Real CSV not found; skipping integration test.");
            if (!File.Exists(RealSignalsPath))
                Assert.Ignore("Real signals.json not found; skipping integration test.");

            var from = new DateTime(2022, 10, 18, 13, 0, 0, DateTimeKind.Utc);
            var to   = new DateTime(2022, 10, 18, 23, 59, 0, DateTimeKind.Utc);

            var tradeViewManager = new TestTradeViewManager(m_BarsProvider);
            m_SetupFinder = new ParseSetupFinder(m_BarsProvider, SYMBOL, tradeViewManager, RealSignalsPath);
            m_SetupFinder.OnEnter        += (_, args) => m_Entries.Add(args);
            m_SetupFinder.OnTakeProfit   += (_, args) => m_TakeProfits.Add(args);
            m_SetupFinder.OnStopLoss     += (_, args) => m_StopLosses.Add(args);
            m_SetupFinder.OnBreakeven    += (_, args) => m_Breakevens.Add(args);
            m_SetupFinder.OnManualClose  += (_, args) => m_ManualCloses.Add(args);
            m_SetupFinder.MarkAsInitialized();

            m_BarsProvider.BarClosed += (_, _) =>
            {
                var dt = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
                m_SetupFinder.CheckBar(dt);
            };

            m_BarsProvider.LoadCandles(RealCsvPath, from, to);

            TestContext.WriteLine($"Bars loaded (2022-10-18 slice): {m_BarsProvider.Count}");
            TestContext.WriteLine($"Enter events: {m_Entries.Count}");
            TestContext.WriteLine($"TP events: {m_TakeProfits.Count}");
            TestContext.WriteLine($"SL events: {m_StopLosses.Count}");
            TestContext.WriteLine($"Breakeven events: {m_Breakevens.Count}");

            foreach (var e in m_Entries)
                TestContext.WriteLine($"  Enter: time={e.Level.OpenTime:HH:mm} entry={e.Level.Value} tp={e.TakeProfit.Value} sl={e.StopLoss.Value}");
            foreach (var be in m_Breakevens)
                TestContext.WriteLine($"  BE:    time={be.Level.OpenTime:HH:mm} level={be.Level.Value}");
            foreach (var tp in m_TakeProfits)
                TestContext.WriteLine($"  TP:    time={tp.Level.OpenTime:HH:mm} level={tp.Level.Value}");

            // Signal 737 should trigger an entry at ~1655.50
            Assert.That(m_Entries.Count, Is.GreaterThanOrEqualTo(1),
                "Signal #737 (sell @ 1655.50) should generate at least one entry");

            SignalEventArgs? signal737 = m_Entries.FirstOrDefault(
                e => Math.Abs(e.Level.Value - 1655.50) < 1.0);
            Assert.That(signal737, Is.Not.Null,
                "An entry close to 1655.50 should be found (signal #737)");

            // Reply #741 announces breakeven
            Assert.That(m_Breakevens.Count, Is.GreaterThanOrEqualTo(1),
                "Signal #741 reply should trigger OnBreakeven");

            // Reply #747 announces TP hit explicitly OR price reaches 1651
            Assert.That(m_TakeProfits.Count, Is.GreaterThanOrEqualTo(1),
                "Signal #747 reply (or price action) should trigger OnTakeProfit");
        }

        /// <summary>
        /// Broad smoke test: load the first month of real data and ensure the finder
        /// processes bars without throwing, and fires at least some entries and outcomes.
        /// </summary>
        [Test]
        [Category("Integration")]
        public void Integration_RealData_FirstMonth_RunsWithoutErrorAndFindsSignals()
        {
            if (!File.Exists(RealCsvPath))
                Assert.Ignore("Real CSV not found; skipping integration test.");
            if (!File.Exists(RealSignalsPath))
                Assert.Ignore("Real signals.json not found; skipping integration test.");

            var from = new DateTime(2022, 9, 27, 0, 0, 0, DateTimeKind.Utc);
            var to   = new DateTime(2022, 10, 31, 23, 59, 0, DateTimeKind.Utc);

            var tradeViewManager = new TestTradeViewManager(m_BarsProvider);
            m_SetupFinder = new ParseSetupFinder(m_BarsProvider, SYMBOL, tradeViewManager, RealSignalsPath);
            m_SetupFinder.OnEnter        += (_, args) => m_Entries.Add(args);
            m_SetupFinder.OnTakeProfit   += (_, args) => m_TakeProfits.Add(args);
            m_SetupFinder.OnStopLoss     += (_, args) => m_StopLosses.Add(args);
            m_SetupFinder.OnBreakeven    += (_, args) => m_Breakevens.Add(args);
            m_SetupFinder.MarkAsInitialized();

            m_BarsProvider.BarClosed += (_, _) =>
            {
                var dt = m_BarsProvider.GetOpenTime(m_BarsProvider.Count - 1);
                m_SetupFinder.CheckBar(dt);
            };

            Assert.DoesNotThrow(
                () => m_BarsProvider.LoadCandles(RealCsvPath, from, to),
                "Loading candles and running ParseSetupFinder must not throw");

            TestContext.WriteLine($"Bars (Sep-Oct 2022): {m_BarsProvider.Count}");
            TestContext.WriteLine($"Entries: {m_Entries.Count}  TPs: {m_TakeProfits.Count}  SLs: {m_StopLosses.Count}  BEs: {m_Breakevens.Count}");

            Assert.That(m_Entries.Count, Is.GreaterThan(0), "At least one XAUUSD entry should fire in Sep-Oct 2022");
            int outcomes = m_TakeProfits.Count + m_StopLosses.Count + m_Breakevens.Count;
            Assert.That(outcomes, Is.GreaterThan(0), "At least one TP/SL/BE outcome should register after entries");
        }
    }
}
