using NUnit.Framework;
using TradeKit.Core.Harmonic;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Stage 1 checks: Fibonacci rules, tolerances, PRZ, score, targets, stop loss and R:R.
    /// </summary>
    [TestFixture]
    public class HarmonicMathTests
    {
        private const double ERROR_PERCENT = 15d;
        private const double XA = 100d;

        [Test]
        public void EveryModel_ValidatesARealizablePattern()
        {
            foreach (HarmonicPatternDefinition definition in HarmonicPatternDefinition.All)
            {
                Assert.That(TryBuildPattern(definition, out double x, out double a,
                        out double b, out double c, out double d), Is.True,
                    $"No admissible leg combination validates for {definition.PatternType}.");

                AssertValid(definition, x, a, b, c, d, ERROR_PERCENT);
            }
        }

        [Test]
        public void Models_ValidateOnNominalRatios()
        {
            // Gartley, Bat, Butterfly, Shark and Cypher have leg combinations that are
            // consistent at the nominal Fibonacci values.
            var nominal = new[]
            {
                HarmonicPatternType.GARTLEY, HarmonicPatternType.BAT,
                HarmonicPatternType.BUTTERFLY, HarmonicPatternType.SHARK,
                HarmonicPatternType.CYPHER
            };

            foreach (HarmonicPatternType patternType in nominal)
            {
                HarmonicPatternDefinition definition = HarmonicPatternDefinition.Get(patternType);
                Assert.That(TryBuildPattern(definition, out double x, out double a,
                        out double b, out double c, out double d, new[] { 1d }), Is.True,
                    $"No nominal ratio combination validates for {patternType}.");

                AssertValid(definition, x, a, b, c, d, ERROR_PERCENT);
            }

            // Crab is the exception: AD/XA = 1.618 combined with any nominal AB/XA and BC/AB
            // lands CD/BC in the gap between the admissible 2.24 and 3.618, so a Crab only
            // exists once the earlier legs are stretched inside the allowed error.
            Assert.That(TryBuildPattern(HarmonicPatternDefinition.Get(HarmonicPatternType.CRAB),
                out _, out _, out _, out _, out _, new[] { 1d }), Is.False);
        }

        [Test]
        public void Tolerance_BoundsAreInclusive()
        {
            const double expected = HarmonicFib.F618;
            double upper = expected * 1.15;
            double lower = expected * 0.85;

            Assert.Multiple(() =>
            {
                Assert.That(HarmonicMath.IsWithin(expected, expected, ERROR_PERCENT), Is.True);
                Assert.That(HarmonicMath.IsWithin(upper, expected, ERROR_PERCENT), Is.True,
                    "The upper tolerance bound must be inclusive.");
                Assert.That(HarmonicMath.IsWithin(lower, expected, ERROR_PERCENT), Is.True,
                    "The lower tolerance bound must be inclusive.");
                Assert.That(HarmonicMath.IsWithin(upper * 1.0001, expected, ERROR_PERCENT), Is.False);
                Assert.That(HarmonicMath.IsWithin(lower * 0.9999, expected, ERROR_PERCENT), Is.False);
            });
        }

        [Test]
        public void Shark_AbToXa_UsesStrictLessThanOne()
        {
            HarmonicPatternDefinition shark =
                HarmonicPatternDefinition.Get(HarmonicPatternType.SHARK);

            Assert.Multiple(() =>
            {
                Assert.That(shark.TestAb(0.999 * XA, XA, ERROR_PERCENT), Is.True);
                Assert.That(shark.TestAb(XA, XA, ERROR_PERCENT), Is.False,
                    "AB/XA equal to 1.0 must be rejected.");
                Assert.That(shark.TestAb(1.001 * XA, XA, ERROR_PERCENT), Is.False);
                Assert.That(shark.GetAbError(0.5), Is.Null,
                    "Shark defines no theoretical AB/XA ratio, so it has no error component.");
            });
        }

        [Test]
        public void Cypher_UsesCdToXc_AndSkipsCdToBc()
        {
            HarmonicPatternDefinition cypher =
                HarmonicPatternDefinition.Get(HarmonicPatternType.CYPHER);

            // Bullish Cypher: X = 0, A = 100, AB/XA = 0.618, BC/AB = 1.272.
            double x = 0d;
            double a = XA;
            double b = a - HarmonicFib.F618 * XA;
            double c = b + HarmonicFib.F1272 * (a - b);
            double xc = c - x;
            double d = c - HarmonicFib.F786 * xc;

            double cd = Math.Abs(c - d);
            double bc = Math.Abs(b - c);
            double ad = Math.Abs(a - d);

            Assert.Multiple(() =>
            {
                Assert.That(cypher.TestCd(cd, bc, XA, xc, ad, ERROR_PERCENT), Is.True);
                Assert.That(cypher.GetCdError(cd / bc), Is.Null,
                    "CD/BC is not defined for Cypher.");
                Assert.That(Math.Abs(cd / xc - HarmonicFib.F786), Is.LessThan(1e-12));

                // A D at 0.5 of XC is far outside the CD/XC tolerance and must be rejected,
                // even though CD/BC is never validated for a Cypher.
                double wrongD = c - 0.5d * xc;
                Assert.That(cypher.TestCd(Math.Abs(c - wrongD), bc, XA, xc,
                    Math.Abs(a - wrongD), ERROR_PERCENT), Is.False);
            });
        }

        [Test]
        public void Symmetry_RejectsTooAsymmetricPattern()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HarmonicMath.TestSymmetry(10, 10, 10, 10, 250d), Is.True);
                Assert.That(HarmonicMath.TestSymmetry(10, 10, 10, 400, 250d), Is.False,
                    "A CD leg far longer than the average must be rejected.");
                Assert.That(HarmonicMath.TestSymmetry(10, 10, 10, null, 250d), Is.True,
                    "Without a CD leg the reference implementation performs no check.");
            });
        }

        [Test]
        public void Prz_PicksTheTwoClosestLevels()
        {
            (double low, double high) = HarmonicMath.GetClosestLevels(
                new[] { 100d, 130d, 132d, 200d });

            Assert.Multiple(() =>
            {
                Assert.That(low, Is.EqualTo(130d));
                Assert.That(high, Is.EqualTo(132d));
            });

            (double singleLow, double singleHigh) = HarmonicMath.GetClosestLevels(new[] { 42d });
            Assert.Multiple(() =>
            {
                Assert.That(singleLow, Is.EqualTo(42d));
                Assert.That(singleHigh, Is.EqualTo(42d));
            });
        }

        [Test]
        public void Prz_MatchesTheReferenceProjection()
        {
            HarmonicPatternDefinition gartley =
                HarmonicPatternDefinition.Get(HarmonicPatternType.GARTLEY);

            double x = 100d;
            double a = 200d;
            double b = a - HarmonicFib.F618 * XA;
            double c = b + HarmonicFib.F886 * (a - b);

            HarmonicPrz prz = HarmonicMath.CalculatePrz(gartley, x, a, b, c);

            double bcNear = c - HarmonicFib.F1272 * (c - b);
            double bcFar = c - HarmonicFib.F1618 * (c - b);
            double xaLevel = a - HarmonicFib.F786 * (a - x);

            Assert.Multiple(() =>
            {
                Assert.That(prz.Levels, Is.EquivalentTo(new[] { bcNear, bcFar, xaLevel }));
                Assert.That(prz.Upper, Is.EqualTo(Math.Max(bcNear, Math.Max(bcFar, xaLevel))));
                Assert.That(prz.Lower, Is.EqualTo(Math.Min(bcNear, Math.Min(bcFar, xaLevel))));
                Assert.That(prz.Score, Is.EqualTo(
                    1d - (prz.ConfluentHigh - prz.ConfluentLow) / Math.Abs(a - x)).Within(1e-12));
            });
        }

        [Test]
        public void Score_UsesTheReferenceWeights()
        {
            var parameters = new HarmonicParams();
            HarmonicPatternDefinition gartley =
                HarmonicPatternDefinition.Get(HarmonicPatternType.GARTLEY);

            double x = 100d;
            double a = 200d;
            double b = a - HarmonicFib.F618 * XA;
            double c = b + HarmonicFib.F886 * (a - b);
            double d = a - HarmonicFib.F786 * XA;

            HarmonicPrz prz = HarmonicMath.CalculatePrz(gartley, x, a, b, c);
            HarmonicScore score = HarmonicMath.CalculateScore(
                gartley, prz, parameters, x, a, b, c, d, 0, 10, 20, 30, 40);

            Assert.That(score.DConfluenceError, Is.Not.Null);
            double expected =
                ((1d - score.FibError) * parameters.FibErrorWeight +
                 prz.Score * parameters.PrzWeight +
                 (1d - score.DConfluenceError!.Value) * parameters.DConfluenceWeight) /
                (parameters.FibErrorWeight + parameters.PrzWeight + parameters.DConfluenceWeight);

            Assert.Multiple(() =>
            {
                Assert.That(score.Total, Is.EqualTo(expected).Within(1e-12));
                Assert.That(score.AbToXaError, Is.Not.Null);
                Assert.That(score.CdToBcError, Is.Not.Null);
                Assert.That(score.FinalError, Is.Not.Null);
            });
        }

        [Test]
        public void Score_ForCypher_ExcludesThePrzComponent()
        {
            var parameters = new HarmonicParams();
            HarmonicPatternDefinition cypher =
                HarmonicPatternDefinition.Get(HarmonicPatternType.CYPHER);

            double x = 0d;
            double a = XA;
            double b = a - HarmonicFib.F618 * XA;
            double c = b + HarmonicFib.F1272 * (a - b);
            double d = c - HarmonicFib.F786 * (c - x);

            HarmonicPrz prz = HarmonicMath.CalculatePrz(cypher, x, a, b, c);
            HarmonicScore score = HarmonicMath.CalculateScore(
                cypher, prz, parameters, x, a, b, c, d, 0, 10, 20, 30, 40);

            Assert.That(score.DConfluenceError, Is.Not.Null);
            double expected =
                ((1d - score.FibError) * parameters.FibErrorWeight +
                 (1d - score.DConfluenceError!.Value) * parameters.DConfluenceWeight) /
                (parameters.FibErrorWeight + parameters.DConfluenceWeight);

            Assert.That(score.Total, Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void Targets_AreSymmetricForLongAndShort()
        {
            var target = new HarmonicTarget(HarmonicTargetBasis.AD, HarmonicFib.F618);

            // Long: X low, A high, D low.
            double longTp = target.Resolve(100d, 200d, 140d, 190d, 120d);
            Assert.That(longTp, Is.EqualTo(120d + HarmonicFib.F618 * 80d).Within(1e-12));
            Assert.That(longTp, Is.GreaterThan(120d));

            // Short: the mirrored figure.
            double shortTp = target.Resolve(200d, 100d, 160d, 110d, 180d);
            Assert.That(shortTp, Is.EqualTo(180d - HarmonicFib.F618 * 80d).Within(1e-12));
            Assert.That(shortTp, Is.LessThan(180d));
        }

        [Test]
        public void Targets_SupportAbsolutePoints()
        {
            var target = new HarmonicTarget(HarmonicTargetBasis.POINT_C);
            Assert.That(target.Resolve(100d, 200d, 140d, 190d, 120d), Is.EqualTo(190d));
        }

        [Test]
        public void Targets_DefaultsMatchTheReferenceIndicator()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HarmonicTarget.DefaultTakeProfit2[HarmonicPatternType.SHARK].Basis,
                    Is.EqualTo(HarmonicTargetBasis.POINT_C));
                Assert.That(HarmonicTarget.DefaultTakeProfit1[HarmonicPatternType.CYPHER].Basis,
                    Is.EqualTo(HarmonicTargetBasis.CD));
                Assert.That(HarmonicTarget.DefaultTakeProfit2[HarmonicPatternType.CYPHER].Basis,
                    Is.EqualTo(HarmonicTargetBasis.XA));
                Assert.That(HarmonicTarget.DefaultTakeProfit2[HarmonicPatternType.CRAB].Ratio,
                    Is.EqualTo(HarmonicFib.F1618));
            });
        }

        [Test]
        public void StopLoss_EveryModeIsCalculatedForBothDirections()
        {
            var prz = new HarmonicPrz(new[] { 110d, 130d }, 110d, 130d, 110d, 130d, 0.8d);
            const double entry = 125d;
            const double takeProfit1 = 165d;
            const double x = 100d;
            const double d = 120d;
            const double percent = 10d;

            Assert.Multiple(() =>
            {
                Assert.That(Bull(HarmonicStopMode.PERCENT_BEYOND_D),
                    Is.EqualTo(d * 0.9).Within(1e-12));
                Assert.That(Bull(HarmonicStopMode.PERCENT_BEYOND_X_OR_D),
                    Is.EqualTo(x * 0.9).Within(1e-12));
                Assert.That(Bull(HarmonicStopMode.PERCENT_BEYOND_ENTRY),
                    Is.EqualTo(entry * 0.9).Within(1e-12));
                Assert.That(Bull(HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY),
                    Is.EqualTo(entry - 0.1 * (takeProfit1 - entry)).Within(1e-12));
                Assert.That(Bull(HarmonicStopMode.PERCENT_BEYOND_FARTHEST_PRZ),
                    Is.EqualTo(prz.Lower * 0.9).Within(1e-12));
            });

            // The mirrored short setup: D above the entry, TP below it.
            const double shortEntry = 125d;
            const double shortTakeProfit1 = 85d;
            const double shortX = 150d;
            const double shortD = 130d;

            Assert.Multiple(() =>
            {
                Assert.That(Bear(HarmonicStopMode.PERCENT_BEYOND_D),
                    Is.EqualTo(shortD * 1.1).Within(1e-12));
                Assert.That(Bear(HarmonicStopMode.PERCENT_BEYOND_X_OR_D),
                    Is.EqualTo(shortX * 1.1).Within(1e-12));
                Assert.That(Bear(HarmonicStopMode.PERCENT_BEYOND_ENTRY),
                    Is.EqualTo(shortEntry * 1.1).Within(1e-12));
                Assert.That(Bear(HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY),
                    Is.EqualTo(shortEntry + 0.1 * (shortEntry - shortTakeProfit1)).Within(1e-12));
                Assert.That(Bear(HarmonicStopMode.PERCENT_BEYOND_FARTHEST_PRZ),
                    Is.EqualTo(prz.Upper * 1.1).Within(1e-12));
            });

            double Bull(HarmonicStopMode mode) => HarmonicMath.CalculateStopLoss(
                mode, percent, true, x, d, prz, takeProfit1, entry);

            double Bear(HarmonicStopMode mode) => HarmonicMath.CalculateStopLoss(
                mode, percent, false, shortX, shortD, prz, shortTakeProfit1, shortEntry);
        }

        [Test]
        public void RiskReward_RejectsWronglyOrderedLevels()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HarmonicMath.GetRiskReward(true, 100d, 130d, 90d),
                    Is.EqualTo(3d).Within(1e-12));
                Assert.That(HarmonicMath.GetRiskReward(false, 100d, 70d, 110d),
                    Is.EqualTo(3d).Within(1e-12));
                Assert.That(HarmonicMath.GetRiskReward(true, 100d, 90d, 80d), Is.Null,
                    "A long take profit below the entry is invalid.");
                Assert.That(HarmonicMath.GetRiskReward(true, 100d, 130d, 100d), Is.Null,
                    "A zero stop distance is invalid.");
            });
        }

        /// <summary>
        /// Builds a bullish pattern of the model by probing the admissible AB/XA and BC/AB
        /// values, optionally stretched inside the allowed error.
        /// </summary>
        private static bool TryBuildPattern(
            HarmonicPatternDefinition definition,
            out double x, out double a, out double b, out double c, out double d,
            double[]? multipliers = null)
        {
            multipliers ??= new[] { 1d, 0.95d, 1.05d, 0.9d, 1.1d, 0.85d, 1.15d };

            x = 0d;
            a = XA;
            b = 0d;
            c = 0d;
            d = 0d;

            double[] abRatios = definition.AbToXaLessThanOne
                ? new[] { 0.5d }
                : definition.AbToXa;

            foreach (double abRatio in abRatios)
            foreach (double abMultiplier in multipliers)
            {
                b = a - abRatio * abMultiplier * XA;
                double ab = a - b;

                foreach (double bcRatio in definition.BcToAb)
                foreach (double bcMultiplier in multipliers)
                {
                    c = b + bcRatio * bcMultiplier * ab;
                    double bc = c - b;
                    double xc = c - x;

                    if (!definition.TestAb(ab, XA, ERROR_PERCENT) ||
                        !definition.TestBc(bc, ab, ERROR_PERCENT))
                    {
                        continue;
                    }

                    foreach (double finalRatio in definition.FinalRatios)
                    {
                        d = definition.FinalIsCdToXc
                            ? c - finalRatio * xc
                            : a - finalRatio * XA;

                        if (definition.TestCd(Math.Abs(c - d), bc, XA, xc, Math.Abs(a - d),
                                ERROR_PERCENT))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void AssertValid(
            HarmonicPatternDefinition definition,
            double x, double a, double b, double c, double d, double errorPercent)
        {
            double xa = Math.Abs(a - x);
            double ab = Math.Abs(a - b);
            double bc = Math.Abs(b - c);

            Assert.Multiple(() =>
            {
                Assert.That(definition.TestAb(ab, xa, errorPercent), Is.True,
                    $"AB/XA failed for {definition.PatternType}.");
                Assert.That(definition.TestBc(bc, ab, errorPercent), Is.True,
                    $"BC/AB failed for {definition.PatternType}.");
                Assert.That(definition.TestCd(Math.Abs(c - d), bc, xa, Math.Abs(c - x),
                    Math.Abs(a - d), errorPercent), Is.True,
                    $"CD failed for {definition.PatternType}.");
            });
        }
    }
}
