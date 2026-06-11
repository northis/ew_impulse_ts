using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests;

public class PatternGenTests
{
    private PatternGenerator m_PatternGenerator;
    
    private static readonly string FOLDER_TO_SAVE = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "images");
    
    private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

    private static (DateTime, DateTime) GetDateRange(int barCount)
    {
        return Helper.GetDateRange(barCount, TIME_FRAME);
    }

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator(true);

        if (!Directory.Exists(FOLDER_TO_SAVE))
            Directory.CreateDirectory(FOLDER_TO_SAVE);
    }

    [Test]
    public void ExtendedFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME)
                    {Min = 30}, ElliottModelType.FLAT_EXTENDED);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void DiagonalTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            //ModelPattern model = m_PatternGenerator.GetPattern(
            //    new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.DIAGONAL_CONTRACTING_INITIAL);
            //SaveResultFiles(model);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME)
                    { Max = 70 }, ElliottModelType.DIAGONAL_CONTRACTING_ENDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ExpandingDiagonalTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DIAGONAL_EXPANDING_INITIAL);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);

            model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DIAGONAL_EXPANDING_ENDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                (DateTime, DateTime) dates = GetDateRange(i);
                ModelPattern model = m_PatternGenerator.GetPattern(
                    new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.ZIGZAG);
                ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
            }
        }
    }

    [Test]
    public void FlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.FLAT_REGULAR);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void DoubleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DOUBLE_ZIGZAG);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TripleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.TRIPLE_ZIGZAG);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleTest()
    {
        for (int i = 100; i <= 100; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    {Max = 120}, ElliottModelType.TRIANGLE_CONTRACTING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleRunningTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    { Max = 120, Min = 50}, ElliottModelType.TRIANGLE_RUNNING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleExpandingTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    { Min = 50 }, ElliottModelType.TRIANGLE_EXPANDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void CombinationTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME) 
                    { Min = 50, Max = 110 }, ElliottModelType.COMBINATION);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void RunningFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME)
                    {Max = 66, Min = 34}, ElliottModelType.FLAT_RUNNING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ImpulseTest()
    {
        (DateTime, DateTime) dates = GetDateRange(15);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.IMPULSE, true);
        ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE, model.Level);
    }

    [Test]
    public void SimpleImpulseTest()
    {
        (DateTime, DateTime) dates = GetDateRange(15);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);
        ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE, model.Level);
    }

    [Test]
    public void RunningFlatFiboScoreTest()
    {
        // Generate a FLAT_RUNNING pattern
        (DateTime start, DateTime end) = GetDateRange(150);
        var args = new PatternArgsItem(60, 40, start, end, TIME_FRAME)
            { Max = 66, Min = 34 };
            
        ModelPattern model = m_PatternGenerator.GetPattern(
            args, ElliottModelType.FLAT_RUNNING);

        Assert.That(model.PatternKeyPoints.Count, Is.GreaterThan(0),
            "Pattern should have key points");

        // Extract main-level (outermost) wave endpoints
        List<PatternKeyPoint> mainWaves = model.PatternKeyPoints
            .SelectMany(kvp => kvp.Value)
            .Where(kp => kp.Notation.Level == model.Level)
            .OrderBy(kp => kp.Index)
            .ToList();

        Assert.That(mainWaves.Count, Is.GreaterThanOrEqualTo(3),
            "FLAT_RUNNING should have at least 3 main-level key points (A, B, C)");

        // The generated pattern has no explicit start key point — use the first candle
        JsonCandleExport firstCandle = model.Candles[0];
        double startValue = firstCandle.O;
        int startIndex = 0;

        // Build Segment list: start → A → B → C
        var segments = new List<ElliottWaveExactMarkupV2.Segment>();
        var allPoints = new List<(double Value, int Index)>
            { (startValue, startIndex) };
        allPoints.AddRange(mainWaves.Select(kp => (kp.Value, kp.Index)));
        
        for (int i = 1; i < allPoints.Count; i++)
        {
            var startPt = new BarPoint(
                allPoints[i - 1].Value,
                model.Candles[allPoints[i - 1].Index].OpenDate,
                TIME_FRAME,
                allPoints[i - 1].Index);
            var endPt = new BarPoint(
                allPoints[i].Value,
                model.Candles[allPoints[i].Index].OpenDate,
                TIME_FRAME,
                allPoints[i].Index);

            segments.Add(new ElliottWaveExactMarkupV2.Segment(startPt, endPt));
        }

        Assert.That(segments.Count, Is.EqualTo(3),
            "FLAT_RUNNING should produce exactly 3 segments (A, B, C)");

        // Log wave lengths for debugging
        for (int i = 0; i < segments.Count; i++)
        {
            TestContext.WriteLine(
                $"Wave {i}: length={segments[i].Length:F5}, " +
                $"bars={segments[i].BarsCount}, " +
                $"isUp={segments[i].IsUp}");
                
            if (i > 0)
            {
                double ratio = segments[i].Length / segments[i - 1].Length;
                TestContext.WriteLine($"  Ratio wave[{i}]/wave[{i-1}] = {ratio:F4}");
            }
        }

        // Calculate pure Fibo score
        double score = ElliottWaveExactMarkup.CalculatePureFiboScore(
            model.Model, segments);

        TestContext.WriteLine($"FLAT_RUNNING Fibo score: {score:F6}");

        // Sanity checks: score should be between 0 and 1
        Assert.That(score, Is.GreaterThan(0.0),
            "Fibo score should be positive");
        Assert.That(score, Is.LessThanOrEqualTo(1.0),
            "Fibo score should be at most 1.0");
        Assert.That(double.IsFinite(score), Is.True,
            "Fibo score should be finite");
    }
}