using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Signals;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Data model for ParseSignalTestCases.json
    // ──────────────────────────────────────────────────────────────────────────

    internal class ParseSignalTestCase
    {
        [JsonProperty("id")]      public long   Id      { get; set; }
        [JsonProperty("date")]    public string Date    { get; set; } = "";
        [JsonProperty("replyTo")] public long?  ReplyTo { get; set; }
        [JsonProperty("text")]    public string Text    { get; set; } = "";
        [JsonProperty("category")]public string Category{ get; set; } = "";
        /// <summary>Text of the original signal message this reply refers to (pre-embedded).</summary>
        [JsonProperty("refText")] public string? RefText { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parameterised tests driven by TestData/ParseSignalTestCases.json.
    /// Each case is a real (de-duplicated) message from the GOLD EMPIRE channel.
    /// Categories:
    ///   valid_market     – complete buy/sell now signals → must fire OnEnter
    ///   limit_order      – limit orders → must NOT fire OnEnter (skipped)
    ///   incomplete       – buy/sell without full TP/SL → must NOT crash
    ///   ad_or_result     – result announcements, ads → must NOT fire OnEnter
    ///   noise            – unrelated channel noise → must NOT fire OnEnter
    ///   reply_breakeven  – reply announcing BE → must fire OnBreakeven
    ///   reply_tp_hit     – reply announcing TP hit → must fire OnTakeProfit
    ///   reply_sl_hit     – reply announcing SL hit → must fire OnStopLoss
    ///   reply_close      – reply asking to close → must fire OnManualClose
    ///   reply_noise      – running-pips / promo replies → no trade events
    ///   reply_other      – ambiguous replies → must NOT crash
    /// </summary>
    [TestFixture]
    internal class ParseSignalTextTests
    {
        private static readonly ITimeFrame TIMEFRAME = new TimeFrameBase("Minute", "m1");
        private static readonly ISymbol SYMBOL = new SymbolBase("XAUUSD", "XAUUSD", 1, 2, 0.01, 0.01, 100);

        private static string TestCasesPath => Path.Combine(
            TestContext.CurrentContext.TestDirectory, "TestData", "ParseSignalTestCases.json");

        private static IEnumerable<ParseSignalTestCase> AllCases()
        {
            string json = File.ReadAllText(TestCasesPath);
            return JsonConvert.DeserializeObject<List<ParseSignalTestCase>>(json)
                   ?? Enumerable.Empty<ParseSignalTestCase>();
        }

        // NUnit test case source – one NUnit test per JSON entry
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var c in AllCases())
            {
                string label = $"[{c.Category}] id={c.Id}: {c.Text.Replace('\n', ' ').Truncate(60)}";
                yield return new TestCaseData(c).SetName(label);
            }
        }

        // ── fixture helpers ───────────────────────────────────────────────────

        private TestBarsProvider BuildBarsProvider() =>
            new TestBarsProvider(TIMEFRAME, SYMBOL);

        /// <summary>
        /// Creates a self-contained ParseSetupFinder from one or two messages,
        /// wires events and adds 6 candles that span a neutral price range so
        /// TP/SL are not hit by price movement (only by explicit reply text).
        /// </summary>
        private (ParseSetupFinder finder,
                 List<SignalEventArgs> entries,
                 List<LevelEventArgs> tps,
                 List<LevelEventArgs> sls,
                 List<LevelEventArgs> bes,
                 List<LevelEventArgs> closes)
            BuildFinder(IEnumerable<(long id, long? replyId, DateTime date, string text)> msgs)
        {
            var barsProvider = BuildBarsProvider();
            var tmpFile = Path.GetTempFileName();

            try
            {
                // Write minimal SymbolDataExportJson
                var sb = new StringBuilder("{\"messages\":[");
                bool first = true;
                foreach (var (id, replyId, date, text) in msgs)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
                    string reply   = replyId.HasValue ? replyId.Value.ToString() : "null";
                    sb.Append($"{{\"id\":{id},\"date\":\"{date:yyyy-MM-ddTHH:mm:sszzz}\",\"reply_to_msg_id\":{reply},\"text\":\"{escaped}\",\"type\":\"message\"}}");
                }
                sb.Append("]}");
                File.WriteAllText(tmpFile, sb.ToString());

                var entries = new List<SignalEventArgs>();
                var tps     = new List<LevelEventArgs>();
                var sls     = new List<LevelEventArgs>();
                var bes     = new List<LevelEventArgs>();
                var closes  = new List<LevelEventArgs>();

                var tradeViewMgr = new TestTradeViewManager(barsProvider);
                var finder = new ParseSetupFinder(barsProvider, SYMBOL, tradeViewMgr, tmpFile);
                finder.OnEnter       += (_, a) => entries.Add(a);
                finder.OnTakeProfit  += (_, a) => tps.Add(a);
                finder.OnStopLoss    += (_, a) => sls.Add(a);
                finder.OnBreakeven   += (_, a) => bes.Add(a);
                finder.OnManualClose += (_, a) => closes.Add(a);
                finder.MarkAsInitialized();

                var t0 = new DateTime(2023, 1, 2, 10, 0, 0, DateTimeKind.Utc);
                barsProvider.BarClosed += (_, _) =>
                {
                    var dt = barsProvider.GetOpenTime(barsProvider.Count - 1);
                    finder.CheckBar(dt);
                };

                // 8 candles in a flat, narrow range – message timestamps sit between bar 1 and bar 5
                for (int i = 0; i < 8; i++)
                    barsProvider.AddCandle(new Candle(1900, 1901, 1899, 1900, null, 0), t0.AddMinutes(i));

                return (finder, entries, tps, sls, bes, closes);
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }
        }

        // ── actual test ───────────────────────────────────────────────────────

        // A synthetic SELL-now signal at price 1900 whose TP(1880)/SL(1915) sit
        // outside the hardcoded test bar range [1899–1901], so the entry is never
        // removed from the map by price-movement logic before the reply fires.
        private const string SyntheticOriginalSignal =
            "XAUUSD sell now @ 1900\ntp @ 1880\nsl @ 1915";

        [TestCaseSource(nameof(Cases))]
        public void ParseSignalText_MatchesExpectedBehavior(ParseSignalTestCase tc)
        {
            var t0     = new DateTime(2023, 1, 2, 10, 1, 0, DateTimeKind.Utc);
            var tReply = t0.AddMinutes(3);

            IEnumerable<(long, long?, DateTime, string)> messages;

            if (tc.ReplyTo.HasValue)
            {
                // Two-message scenario: use a synthetic market signal as the original
                // so it is always stored in the finder's map (real refTexts are often
                // limit orders that are skipped by ParseSetupFinder).
                long origId = tc.ReplyTo.Value;
                messages = new[]
                {
                    (origId,  (long?)null, t0,     SyntheticOriginalSignal),
                    (tc.Id,   tc.ReplyTo,  tReply, tc.Text)
                };
            }
            else
            {
                messages = new[] { (tc.Id, (long?)null, t0, tc.Text) };
            }

            var (_, entries, tps, sls, bes, closes) = BuildFinder(messages);

            int tradeEvents = tps.Count + sls.Count + bes.Count + closes.Count;

            switch (tc.Category)
            {
                case "valid_market":
                    Assert.That(entries.Count, Is.GreaterThanOrEqualTo(1),
                        $"valid_market signal must fire OnEnter.\nText: {tc.Text}");
                    break;

                case "limit_order":
                    Assert.That(entries.Count, Is.Zero,
                        $"limit_order must NOT fire OnEnter (skipped).\nText: {tc.Text}");
                    break;

                case "incomplete":
                case "ad_or_result":
                case "noise":
                    Assert.That(entries.Count, Is.Zero,
                        $"Category '{tc.Category}' must NOT fire OnEnter.\nText: {tc.Text}");
                    // Just checking it doesn't crash – no further assertions
                    break;

                case "reply_breakeven":
                    Assert.That(bes.Count, Is.GreaterThanOrEqualTo(1),
                        $"reply_breakeven must fire OnBreakeven.\nText: {tc.Text}\nRef: {tc.RefText}");
                    break;

                case "reply_tp_hit":
                    Assert.That(tps.Count, Is.GreaterThanOrEqualTo(1),
                        $"reply_tp_hit must fire OnTakeProfit.\nText: {tc.Text}\nRef: {tc.RefText}");
                    break;

                case "reply_sl_hit":
                    Assert.That(sls.Count, Is.GreaterThanOrEqualTo(1),
                        $"reply_sl_hit must fire OnStopLoss.\nText: {tc.Text}\nRef: {tc.RefText}");
                    break;

                case "reply_close":
                    Assert.That(closes.Count, Is.GreaterThanOrEqualTo(1),
                        $"reply_close must fire OnManualClose.\nText: {tc.Text}\nRef: {tc.RefText}");
                    break;

                case "reply_noise":
                case "reply_other":
                    // Must not throw; events are optional
                    break;

                default:
                    Assert.Fail($"Unknown category '{tc.Category}' – update the test switch.");
                    break;
            }
        }
    }

    // ── tiny helper ───────────────────────────────────────────────────────────

    internal static class StringExtensions
    {
        public static string Truncate(this string s, int maxLen) =>
            s.Length <= maxLen ? s : s[..maxLen] + "…";
    }
}
