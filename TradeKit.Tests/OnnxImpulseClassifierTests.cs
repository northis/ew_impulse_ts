using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.ML;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests;

/// <summary>
/// Tests for <see cref="OnnxImpulseClassifier"/>.
/// </summary>
public class OnnxImpulseClassifierTests
{
    private PatternGenerator m_PatternGenerator;
    private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

    private static readonly string MODEL_PATH = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
        "TradeKit.Core", "Resources", "simpleImpulse.onnx");

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator(true);
    }

    /// <summary>
    /// Verifies that the classifier returns a probability for a generated SIMPLE_IMPULSE pattern
    /// with more candles than the model expects.
    /// </summary>
    [Test]
    public void PredictProbability_SimpleImpulse_ReturnsProbability()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);

        Assert.That(model.Candles.Count, Is.GreaterThan(120),
            "Generated pattern must have more than 120 candles for this test.");

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxImpulseClassifier classifier = new OnnxImpulseClassifier(fullModelPath);
        float probability = classifier.PredictProbability(model.Candles);
        double prop = probability * 1000000;

        Assert.That(probability, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1f),
            "Probability must be in [0, 1] range.");

        TestContext.WriteLine(
            $"Candles: {model.Candles.Count}, Impulse probability: {probability:F4}");
    }

    /// <summary>
    /// Verifies that the classifier throws when the candle count is less than expected.
    /// </summary>
    [Test]
    public void PredictProbability_TooFewCandles_ThrowsArgumentException()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(5, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxImpulseClassifier classifier = new OnnxImpulseClassifier(fullModelPath);

        Assert.That(() => classifier.PredictProbability(model.Candles),
            Throws.TypeOf<ArgumentException>(),
            "Should throw when candle count is less than expected.");
    }
}
