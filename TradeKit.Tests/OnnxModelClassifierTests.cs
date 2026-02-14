using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.ML;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests;

/// <summary>
/// Tests for <see cref="OnnxModelClassifier"/>.
/// </summary>
public class OnnxModelClassifierTests
{
    private PatternGenerator m_PatternGenerator;
    private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

    private static readonly string MODEL_PATH = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
        "TradeKit.Core", "Resources", "multiModel.onnx");

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator(true);
    }

    /// <summary>
    /// Verifies that the classifier returns probabilities for all model types
    /// when given a generated SIMPLE_IMPULSE pattern with more candles than expected.
    /// </summary>
    [Test]
    public void Predict_SimpleImpulse_ReturnsAllProbabilities()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);

        Assert.That(model.Candles.Count, Is.GreaterThan(50),
            "Generated pattern must have more than 50 candles for this test.");

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);
        Dictionary<ElliottModelType, float> probabilities = classifier.Predict(model.Candles);

        Assert.That(probabilities, Is.Not.Empty,
            "Result must contain probabilities.");

        float sum = probabilities.Values.Sum();
        Assert.That(sum, Is.EqualTo(1f).Within(0.01f),
            "Probabilities must sum to approximately 1.");

        foreach (KeyValuePair<ElliottModelType, float> kvp in probabilities)
        {
            Assert.That(kvp.Value, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1f),
                $"Probability for {kvp.Key} must be in [0, 1] range.");
        }

        TestContext.WriteLine("Prediction results (sorted):");
        foreach (KeyValuePair<ElliottModelType, float> kvp in
            probabilities.OrderByDescending(a => a.Value))
        {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value:F4}");
        }
    }

    /// <summary>
    /// Verifies that the classifier returns probabilities for a generated ZIGZAG pattern.
    /// </summary>
    [Test]
    public void Predict_Zigzag_ReturnsAllProbabilities()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            60, 40, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.ZIGZAG, true);

        Assert.That(model.Candles.Count, Is.GreaterThan(50),
            "Generated pattern must have more than 50 candles for this test.");

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);
        Dictionary<ElliottModelType, float> probabilities = classifier.Predict(model.Candles);

        Assert.That(probabilities, Is.Not.Empty,
            "Result must contain probabilities.");

        float sum = probabilities.Values.Sum();
        Assert.That(sum, Is.EqualTo(1f).Within(0.01f),
            "Probabilities must sum to approximately 1.");

        TestContext.WriteLine("Prediction results (sorted):");
        foreach (KeyValuePair<ElliottModelType, float> kvp in
            probabilities.OrderByDescending(a => a.Value))
        {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value:F4}");
        }
    }

    /// <summary>
    /// Verifies that the classifier returns probabilities for a generated TRIANGLE_CONTRACTING pattern.
    /// </summary>
    [Test]
    public void Predict_TriangleContracting_ReturnsAllProbabilities()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            70, 90, dates.Item1, dates.Item2, TIME_FRAME) { Max = 120 };
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.TRIANGLE_CONTRACTING, true);

        Assert.That(model.Candles.Count, Is.GreaterThan(50),
            "Generated pattern must have more than 50 candles for this test.");

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);
        Dictionary<ElliottModelType, float> probabilities = classifier.Predict(model.Candles);

        Assert.That(probabilities, Is.Not.Empty,
            "Result must contain probabilities.");

        float sum = probabilities.Values.Sum();
        Assert.That(sum, Is.EqualTo(1f).Within(0.01f),
            "Probabilities must sum to approximately 1.");

        TestContext.WriteLine("Prediction results (sorted):");
        foreach (KeyValuePair<ElliottModelType, float> kvp in
            probabilities.OrderByDescending(a => a.Value))
        {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value:F4}");
        }
    }

    /// <summary>
    /// Verifies that the classifier returns empty (zero) probabilities when candle count is less than expected.
    /// </summary>
    [Test]
    public void Predict_TooFewCandles_ReturnsZeroProbabilities()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(5, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);
        Dictionary<ElliottModelType, float> probabilities = classifier.Predict(model.Candles);

        Assert.That(probabilities, Is.Not.Empty,
            "Result must contain probabilities even for too few candles.");

        foreach (KeyValuePair<ElliottModelType, float> kvp in probabilities)
        {
            Assert.That(kvp.Value, Is.EqualTo(0f),
                $"Probability for {kvp.Key} must be 0 when candle count is too low.");
        }
    }

    /// <summary>
    /// Verifies that the classifier returns probabilities for a generated FLAT_REGULAR pattern.
    /// </summary>
    [Test]
    public void Predict_FlatRegular_ReturnsAllProbabilities()
    {
        (DateTime, DateTime) dates = Helper.GetDateRange(300, TIME_FRAME);

        PatternArgsItem paramArgs = new PatternArgsItem(
            60, 40, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.FLAT_REGULAR, true);

        Assert.That(model.Candles.Count, Is.GreaterThan(50),
            "Generated pattern must have more than 50 candles for this test.");

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);
        Dictionary<ElliottModelType, float> probabilities = classifier.Predict(model.Candles);

        Assert.That(probabilities, Is.Not.Empty,
            "Result must contain probabilities.");

        float sum = probabilities.Values.Sum();
        Assert.That(sum, Is.EqualTo(1f).Within(0.01f),
            "Probabilities must sum to approximately 1.");

        TestContext.WriteLine("Prediction results (sorted):");
        foreach (KeyValuePair<ElliottModelType, float> kvp in
            probabilities.OrderByDescending(a => a.Value))
        {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value:F4}");
        }
    }

    /// <summary>
    /// Evaluates model accuracy across all Elliott wave pattern types.
    /// Generates multiple patterns per type, predicts the class, and reports
    /// per-type and overall hit rates.
    /// </summary>
    [Test]
    public void Predict_AccuracyEvaluation_AllModelTypes()
    {
        const int SAMPLES_PER_TYPE = 1;
        const int BAR_COUNT = 100;

        string fullModelPath = Path.GetFullPath(MODEL_PATH);
        Assert.That(File.Exists(fullModelPath), Is.True,
            $"ONNX model file not found at {fullModelPath}");

        Dictionary<ElliottModelType, (double startValue, double endValue, double? min, double? max)> modelParams =
            new Dictionary<ElliottModelType, (double, double, double?, double?)>
            {
                { ElliottModelType.IMPULSE, (40, 60, null, null) },
                { ElliottModelType.SIMPLE_IMPULSE, (40, 60, null, null) },
                { ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, (40, 60, null, null) },
                { ElliottModelType.DIAGONAL_CONTRACTING_ENDING, (40, 60, null, 70d) },
                { ElliottModelType.DIAGONAL_EXPANDING_INITIAL, (40, 60, null, null) },
                { ElliottModelType.DIAGONAL_EXPANDING_ENDING, (40, 60, null, null) },
                { ElliottModelType.TRIANGLE_CONTRACTING, (70, 90, null, 120d) },
                { ElliottModelType.TRIANGLE_EXPANDING, (70, 90, 50d, null) },
                { ElliottModelType.TRIANGLE_RUNNING, (70, 90, 50d, 120d) },
                { ElliottModelType.ZIGZAG, (60, 40, null, null) },
                { ElliottModelType.DOUBLE_ZIGZAG, (60, 40, null, null) },
                { ElliottModelType.TRIPLE_ZIGZAG, (60, 40, null, null) },
                { ElliottModelType.FLAT_REGULAR, (60, 40, null, null) },
                { ElliottModelType.FLAT_EXTENDED, (40, 60, 30d, null) },
                { ElliottModelType.FLAT_RUNNING, (60, 40, 34d, 66d) },
                { ElliottModelType.COMBINATION, (70, 90, 50d, 110d) },
            };

        using OnnxModelClassifier classifier = new OnnxModelClassifier(fullModelPath);

        int totalCorrect = 0;
        int totalAttempted = 0;
        Dictionary<ElliottModelType, (int correct, int total, int generated)> stats =
            new Dictionary<ElliottModelType, (int, int, int)>();

        foreach (ElliottModelType modelType in OnnxModelClassifier.ClassLabels)
        {
            if(modelType!= ElliottModelType.SIMPLE_IMPULSE)
                continue;
            
            int correct = 0;
            int attempted = 0;
            int generated = 0;

            (double startValue, double endValue, double? min, double? max) p = modelParams.TryGetValue(
                modelType, out var val) ? val : (40, 60, (double?)null, (double?)null);

            for (int i = 0; i < SAMPLES_PER_TYPE; i++)
            {
                ModelPattern? pattern = TryGeneratePattern(
                    modelType, BAR_COUNT, p.startValue, p.endValue, p.min, p.max);

                if (pattern == null || pattern.Candles.Count < 51)
                    continue;

                generated++;
                Dictionary<ElliottModelType, float> probabilities = classifier.Predict(pattern.Candles);
                if (probabilities == null || probabilities.Count == 0)
                    continue;

                ElliottModelType predicted = probabilities.MaxBy(a => a.Value).Key;
                attempted++;

                if (predicted == modelType)
                    correct++;
            }

            stats[modelType] = (correct, attempted, generated);
            totalCorrect += correct;
            totalAttempted += attempted;
        }

        TestContext.WriteLine("=== Model Accuracy Evaluation ===");
        TestContext.WriteLine($"{"Type",-35} {"Correct",8} {"Total",8} {"Accuracy",10} {"Generated",10}");
        TestContext.WriteLine(new string('-', 75));

        foreach (ElliottModelType modelType in OnnxModelClassifier.ClassLabels)
        {
            (int correct, int total, int generated) s = stats[modelType];
            string accuracy = s.total > 0
                ? $"{(double)s.correct / s.total:P1}"
                : "N/A";

            TestContext.WriteLine(
                $"{modelType,-35} {s.correct,8} {s.total,8} {accuracy,10} {s.generated,10}");
        }

        TestContext.WriteLine(new string('-', 75));
        string overallAccuracy = totalAttempted > 0
            ? $"{(double)totalCorrect / totalAttempted:P1}"
            : "N/A";
        TestContext.WriteLine(
            $"{"OVERALL",-35} {totalCorrect,8} {totalAttempted,8} {overallAccuracy,10}");

        Assert.That(totalAttempted, Is.GreaterThan(0),
            "At least some patterns must be generated and classified.");
    }

    /// <summary>
    /// Tries to generate a pattern for the given model type with appropriate parameters.
    /// </summary>
    /// <param name="modelType">The Elliott model type.</param>
    /// <param name="barCount">The bar count for date range generation.</param>
    /// <param name="startValue">The start value.</param>
    /// <param name="endValue">The end value.</param>
    /// <param name="min">The optional minimum constraint.</param>
    /// <param name="max">The optional maximum constraint.</param>
    /// <returns>The generated pattern or null if generation failed.</returns>
    private ModelPattern? TryGeneratePattern(
        ElliottModelType modelType, int barCount,
        double startValue, double endValue,
        double? min, double? max)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                (DateTime, DateTime) dates = Helper.GetDateRange(barCount, TIME_FRAME);

                PatternArgsItem args = new PatternArgsItem(
                    startValue, endValue,
                    dates.Item1, dates.Item2, TIME_FRAME);

                if (min.HasValue)
                    args.Min = min.Value;

                if (max.HasValue)
                    args.Max = max.Value;

                return m_PatternGenerator.GetPattern(args, modelType, true);
            }
            catch
            {
                // Generation can fail for random parameters, retry
            }
        }

        return null;
    }
}
