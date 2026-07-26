using System.Text;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Stage 5 checks: TradeKit is replayed on the candles embedded in the Pine reference
    /// exports under <c>data/golden</c> and its results are matched against the patterns the
    /// indicator confirmed on exactly those candles.
    /// </summary>
    [TestFixture]
    public class HarmonicPineComparisonTests
    {
        private const double PRICE_TOLERANCE = 1e-8;
        private const double RATIO_TOLERANCE = 1e-6;

        private sealed class TypeDiff
        {
            public HarmonicPatternType PatternType { get; init; }
            public int PineCount { get; set; }
            public int TradeKitCount { get; set; }
            public List<string> MissingInTradeKit { get; } = new();
            public List<string> ExtraInTradeKit { get; } = new();
            public int Matched { get; set; }
        }

        /// <summary>
        /// The model / direction / X-A-B-C-D identity of a pattern.
        /// </summary>
        private readonly record struct PatternKey(
            HarmonicPatternType PatternType, bool IsBull,
            int X, int A, int B, int C, int D)
        {
            public override string ToString()
            {
                return $"{PatternType}|{(IsBull ? "bull" : "bear")}|{X}|{A}|{B}|{C}|{D}";
            }
        }

        /// <summary>
        /// Names the single point that differs from the nearest counterpart on the other side,
        /// or "-" when the figure has no counterpart at all. Turns a bare count difference into
        /// an actionable statement about which point the two engines picked differently.
        /// </summary>
        private static string Classify(PatternKey key, IReadOnlyCollection<PatternKey> others)
        {
            foreach (PatternKey other in others)
            {
                if (other.PatternType != key.PatternType || other.IsBull != key.IsBull)
                    continue;

                int differences = 0;
                string point = "-";
                if (other.X != key.X) { differences++; point = "X"; }
                if (other.A != key.A) { differences++; point = "A"; }
                if (other.B != key.B) { differences++; point = "B"; }
                if (other.C != key.C) { differences++; point = "C"; }
                if (other.D != key.D) { differences++; point = "D"; }

                if (differences == 1)
                    return point;
            }

            return "-";
        }

        public static IEnumerable<string> Groups => HarmonicPineReference.GetGroups();

        [TestCaseSource(nameof(Groups))]
        public void TradeKitMatchesThePineReference(string group)
        {
            IReadOnlyList<PineRefFile> files = HarmonicPineReference.ReadGroup(group);
            if (files.Count == 0)
                Assert.Inconclusive($"No golden Pine export found for {group}.");

            PineRefFile head = files[0];
            TestBarsProvider provider = LoadCandles(group, head);
            AssertSameCandles(files, provider);

            // The reference indicator runs with every model enabled and a 500 bar history buffer;
            // the export just filters the records by the selected model. Two of its behaviours are
            // reproduced only here: its leg symmetry test is a no-op, and its after-C entry (score
            // above 90 by default) consumes a candidate before a real point D can confirm it.
            var parameters = new HarmonicParams
            {
                BarsDepth = 500,
                CheckLegSymmetry = false,
                AfterCEntryScore = 0.9d
            };
            List<(int ConfirmationIndex, HarmonicItem Item)> found = RunFinder(provider, parameters);

            var report = new StringBuilder();
            var diffs = new List<TypeDiff>();

            foreach (PineRefFile file in files)
            {
                var diff = new TypeDiff { PatternType = file.PatternType };
                diffs.Add(diff);

                Dictionary<PatternKey, PineRefRecord> pine = file.Records
                    .GroupBy(GetKey)
                    .ToDictionary(a => a.Key, a => a.First());
                diff.PineCount = pine.Count;

                Dictionary<PatternKey, HarmonicItem> mine = found
                    .Where(a => a.Item.PatternType == file.PatternType &&
                                InWindow(provider, a.ConfirmationIndex, file))
                    .GroupBy(a => GetKey(a.Item))
                    .ToDictionary(a => a.Key, a => a.First().Item);
                diff.TradeKitCount = mine.Count;

                foreach (KeyValuePair<PatternKey, PineRefRecord> pair in pine)
                {
                    if (!mine.TryGetValue(pair.Key, out HarmonicItem? item))
                    {
                        diff.MissingInTradeKit.Add(
                            $"[{Classify(pair.Key, mine.Keys)}] {pair.Value}");
                        continue;
                    }

                    diff.Matched++;
                    AssertRecordMatches(file, pair.Value, item);
                }

                foreach (KeyValuePair<PatternKey, HarmonicItem> pair in mine)
                {
                    if (!pine.ContainsKey(pair.Key))
                    {
                        diff.ExtraInTradeKit.Add(
                            $"[{Classify(pair.Key, pine.Keys)}] {pair.Key} @ " +
                            $"{pair.Value.ItemD.OpenTime:yyyy-MM-dd HH:mm} " +
                            $"score={pair.Value.Score.Total:F4}");
                    }
                }
            }

            report.AppendLine($"{group}: bars={provider.Count}");
            report.AppendLine("| Model | Pine | TradeKit | Matched | Missing | Extra |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (TypeDiff diff in diffs.OrderBy(a => (int)a.PatternType))
            {
                report.AppendLine(
                    $"| {diff.PatternType} | {diff.PineCount} | {diff.TradeKitCount} | " +
                    $"{diff.Matched} | {diff.MissingInTradeKit.Count} | {diff.ExtraInTradeKit.Count} |");
            }

            report.AppendLine();
            report.AppendLine("Differing point of the unmatched figures (- = no counterpart):");
            report.AppendLine($"  missing: {Summarize(diffs.SelectMany(a => a.MissingInTradeKit))}");
            report.AppendLine($"  extra:   {Summarize(diffs.SelectMany(a => a.ExtraInTradeKit))}");
            report.AppendLine();

            foreach (TypeDiff diff in diffs.OrderBy(a => (int)a.PatternType))
            {
                foreach (string missing in diff.MissingInTradeKit.Take(6))
                    report.AppendLine($"  MISSING {diff.PatternType} {missing}");

                foreach (string extra in diff.ExtraInTradeKit.Take(6))
                    report.AppendLine($"  EXTRA   {diff.PatternType} {extra}");
            }

            TestContext.WriteLine(report.ToString());

            int totalPine = diffs.Sum(a => a.PineCount);
            int totalMine = diffs.Sum(a => a.TradeKitCount);
            int totalMatched = diffs.Sum(a => a.Matched);

            Assert.That(totalPine, Is.GreaterThan(0), "The golden export carries no records.");
            Assert.That(totalMine, Is.GreaterThan(0), "TradeKit found no patterns at all.");

            // The two engines are not expected to agree on every figure: the reference indicator
            // re-completes a pattern on a deeper point D through its pending list, which the first
            // TradeKit version deliberately does not do. The floors below are regression guards -
            // the measured values are ~87% recall / ~94% precision on EURUSD and ~93% / ~88% on
            // XAUUSD. Every figure the engines do share must agree numerically, which is asserted
            // per record above.
            Assert.That((double)totalMatched / totalPine, Is.GreaterThanOrEqualTo(0.80d),
                $"Recall dropped: {totalMatched} of {totalPine} reference patterns reproduced.");
            Assert.That((double)totalMatched / totalMine, Is.GreaterThanOrEqualTo(0.80d),
                $"Precision dropped: {totalMatched} of {totalMine} TradeKit patterns are in the reference.");
        }

        private static string Summarize(IEnumerable<string> entries)
        {
            return string.Join("  ", entries
                .Select(a => a.Substring(1, a.IndexOf(']') - 1))
                .GroupBy(a => a)
                .OrderByDescending(a => a.Count())
                .Select(a => $"{a.Key}={a.Count()}"));
        }

        private static bool InWindow(TestBarsProvider provider, int confirmationIndex, PineRefFile file)
        {
            DateTime time = provider.GetOpenTime(confirmationIndex);
            return time >= file.From && time <= file.To;
        }

        private static PatternKey GetKey(PineRefRecord record)
        {
            return new PatternKey(record.PatternType, record.IsBull, record.XIndex,
                record.AIndex, record.BIndex, record.CIndex, record.DIndex);
        }

        private static PatternKey GetKey(HarmonicItem item)
        {
            return new PatternKey(item.PatternType, item.IsBull, item.ItemX.BarIndex,
                item.ItemA.BarIndex, item.ItemB.BarIndex, item.ItemC.BarIndex,
                item.ItemD.BarIndex);
        }

        private static List<(int ConfirmationIndex, HarmonicItem Item)> RunFinder(
            TestBarsProvider provider, HarmonicParams parameters)
        {
            var finder = new HarmonicPatternFinder(provider, parameters);
            var result = new List<(int, HarmonicItem)>();

            for (int i = 0; i < provider.Count; i++)
            {
                foreach (HarmonicItem item in finder.FindPatterns(i))
                    result.Add((i, item));
            }

            return result;
        }

        private static TestBarsProvider LoadCandles(string group, PineRefFile file)
        {
            string path = Path.Combine(HarmonicPineReference.FindGoldenDir()!, file.FileName);
            var symbol = new SymbolBase(file.SymbolName, file.SymbolName, 1, 5, 0.00001, 0.00001, 100_000);
            var provider = new TestBarsProvider(
                HarmonicCsvData.GetTimeFrame($"{group}.csv"), symbol);
            provider.LoadCandles(path);

            Assert.That(provider.Count, Is.EqualTo(file.Times.Count),
                $"{file.FileName}: the candle loader and the reference reader disagree on the row count.");
            return provider;
        }

        private static void AssertSameCandles(IReadOnlyList<PineRefFile> files, TestBarsProvider provider)
        {
            foreach (PineRefFile file in files)
            {
                Assert.That(file.Times, Has.Count.EqualTo(provider.Count),
                    $"{file.FileName} has a different bar count than the rest of the group.");

                for (int i = 0; i < file.Times.Count; i++)
                {
                    if (file.Times[i] != provider.GetOpenTime(i))
                    {
                        Assert.Fail($"{file.FileName}: bar {i} time {file.Times[i]:s} differs from " +
                                    $"{provider.GetOpenTime(i):s} of the group.");
                    }
                }
            }
        }

        private static void AssertRecordMatches(
            PineRefFile file, PineRefRecord expected, HarmonicItem actual)
        {
            string id = $"{file.FileName} {expected}";

            Assert.Multiple(() =>
            {
                Assert.That(actual.ItemX.Value, Is.EqualTo(expected.XPrice).Within(PRICE_TOLERANCE), $"{id} X");
                Assert.That(actual.ItemA.Value, Is.EqualTo(expected.APrice).Within(PRICE_TOLERANCE), $"{id} A");
                Assert.That(actual.ItemB.Value, Is.EqualTo(expected.BPrice).Within(PRICE_TOLERANCE), $"{id} B");
                Assert.That(actual.ItemC.Value, Is.EqualTo(expected.CPrice).Within(PRICE_TOLERANCE), $"{id} C");
                Assert.That(actual.ItemD.Value, Is.EqualTo(expected.DPrice).Within(PRICE_TOLERANCE), $"{id} D");

                Assert.That(actual.Score.AbToXaRatio, Is.EqualTo(expected.RAbXa).Within(RATIO_TOLERANCE), $"{id} AB/XA");
                Assert.That(actual.Score.BcToAbRatio, Is.EqualTo(expected.RBcAb).Within(RATIO_TOLERANCE), $"{id} BC/AB");
                Assert.That(actual.Score.CdToBcRatio, Is.EqualTo(expected.RCdBc).Within(RATIO_TOLERANCE), $"{id} CD/BC");

                // The reference indicator always stores AD/XA in its final-ratio field, even for
                // Cypher, whose model rule is actually CD/XC. Compare against the same quantity.
                double adToXa = Math.Abs(actual.ItemA.Value - actual.ItemD.Value) /
                                Math.Abs(actual.ItemA.Value - actual.ItemX.Value);
                Assert.That(adToXa, Is.EqualTo(expected.RFinal).Within(RATIO_TOLERANCE), $"{id} AD/XA");

                Assert.That(actual.Prz.ConfluentLow, Is.EqualTo(expected.PrzConfLow).Within(PRICE_TOLERANCE), $"{id} PRZ low");
                Assert.That(actual.Prz.ConfluentHigh, Is.EqualTo(expected.PrzConfHigh).Within(PRICE_TOLERANCE), $"{id} PRZ high");
                Assert.That(actual.Prz.Lower, Is.EqualTo(expected.PrzLower).Within(PRICE_TOLERANCE), $"{id} PRZ lower");
                Assert.That(actual.Prz.Upper, Is.EqualTo(expected.PrzUpper).Within(PRICE_TOLERANCE), $"{id} PRZ upper");

                Assert.That(actual.Score.FibError, Is.EqualTo(expected.EFib).Within(RATIO_TOLERANCE), $"{id} E_fib");
                Assert.That(actual.Prz.Score, Is.EqualTo(expected.PrzScore).Within(RATIO_TOLERANCE), $"{id} PRZ score");
                Assert.That(actual.Score.DConfluenceError, Is.EqualTo(expected.EDist).Within(RATIO_TOLERANCE), $"{id} E_D");
                Assert.That(actual.Score.Total, Is.EqualTo(expected.Score).Within(RATIO_TOLERANCE), $"{id} score");
            });
        }
    }
}
