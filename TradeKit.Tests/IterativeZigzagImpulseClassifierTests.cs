using NUnit.Framework;
using System;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.PatternGeneration;
using TradeKit.Core.Json;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests;

/// <summary>
/// Tests for <see cref="IterativeZigzagImpulseClassifier"/>.
/// </summary>
public class IterativeZigzagImpulseClassifierTests
{
    private PatternGenerator m_PatternGenerator;
    private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator(true);
    }

    /// <summary>
    /// Verifies that a generated SIMPLE_IMPULSE pattern is classified as an impulse.
    /// </summary>
    [Test]
    public void IsImpulse_SimpleImpulse_ReturnsTrue()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);

        TestBarsProvider provider = new TestBarsProvider(TIME_FRAME);
        int index = 0;
        foreach (JsonCandleExport candle in model.Candles)
        {
            provider.AddCandle(new Candle(candle.O, candle.H, candle.L, candle.C, null, index), candle.OpenDate);
            index++;
        }

        BarPoint start = new BarPoint(model.Candles.First().O, 0, provider);
        BarPoint end = new BarPoint(model.Candles.Last().C, provider.Count - 1, provider);

        bool result = IterativeZigzagImpulseClassifier.IsImpulse(start, end, provider);

        Assert.That(result, Is.True, "A simple impulse pattern should be classified as an impulse.");
    }

    /// <summary>
    /// Verifies that a generated ZIGZAG pattern is not classified as an impulse.
    /// </summary>
    [Test]
    public void IsImpulse_Zigzag_ReturnsFalse()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(150, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.ZIGZAG, true);

        TestBarsProvider provider = new TestBarsProvider(TIME_FRAME);
        int index = 0;
        foreach (JsonCandleExport candle in model.Candles)
        {
            provider.AddCandle(new Candle(candle.O, candle.H, candle.L, candle.C, null, index), candle.OpenDate);
            index++;
        }

        BarPoint start = new BarPoint(model.Candles.First().O, 0, provider);
        BarPoint end = new BarPoint(model.Candles.Last().C, provider.Count - 1, provider);

        bool result = IterativeZigzagImpulseClassifier.IsImpulse(start, end, provider);

        Assert.That(result, Is.False, "A zigzag pattern should not be classified as an impulse.");
    }
}
