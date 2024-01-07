using cAlgo.API;
using Newtonsoft.Json;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.Impulse;
using TradeKit.Json;
using TradeKit.PatternGeneration;
using File = System.IO.File;

namespace TradeKit.Tests;

public class PatternGenTests
{
    private PatternGenerator m_PatternGenerator;

    private string GetTempString => 
        Path.GetFileNameWithoutExtension(Path.GetTempFileName());

    private static readonly string FOLDER_TO_SAVE = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "images");
    
    private readonly TimeFrame m_TimeFrame = TimeFrame.Minute;

    private (DateTime, DateTime) GetDateRange(int barCount)
    {
        DateTime dt = DateTime.UtcNow;
        dt = new DateTime(dt.Year, dt.Month, dt.Day);

        TimeSpan step = TimeFrameHelper.TimeFrames[m_TimeFrame].TimeSpan;
        DateTime dtStart = dt.Add(-barCount * step);

        return (dtStart, dt);
    }

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator();

        if (!Directory.Exists(FOLDER_TO_SAVE))
            Directory.CreateDirectory(FOLDER_TO_SAVE);
    }

    private void SaveResultFiles(ModelPattern model)
    {
        string name = model.Model.ToString().ToLowerInvariant();

        string fileName = $"{name}_{model.Candles.Count}_{GetTempString}";
        SaveChart(model.Candles, name.Replace("_"," ").ToUpperInvariant(), 
            fileName, model);

        string jsonFilePath = Path.Join(FOLDER_TO_SAVE, $"{fileName}.json");
        string json = JsonConvert.SerializeObject(
            model.ToJson(m_PatternGenerator), Formatting.Indented);
        File.WriteAllText(jsonFilePath, json);
    }

    private void SaveChart(
        List<JsonCandleExport> candles, string name, string fileName,
        ModelPattern? model = null)
    {
        Console.WriteLine($" bar count is {candles.Count}");
        Console.WriteLine("Model result:");

        if (model != null) Console.WriteLine(model.ToString());
        
        GenericChart.GenericChart chart = ChartGenerator.GetCandlestickChart(
            candles, name);

        if (model?.PatternKeyPoints != null)
        {
            var annotations = new List<Annotation>();
            string[] names =
                PatternGenerator.ModelRules[model.Model].Models.Keys.ToArray();

            for (int i = 0; i < model.PatternKeyPoints.Count; i++)
            {
                (DateTime, double) annotation = model.PatternKeyPoints[i];
                annotations.Add(ChartGenerator.GetAnnotation(
                    annotation.Item1,
                    annotation.Item2,
                    ChartGenerator.WHITE_COLOR,
                    ChartGenerator.CHART_FONT_MAIN,
                    ChartGenerator.SEMI_WHITE_COLOR, names[i]));
            }

            chart.WithAnnotations(annotations);
            chart.WithTitle(name);
        }

        string pngPath = Path.Combine(FOLDER_TO_SAVE, fileName);
        chart.SavePNG(pngPath, null, 1000, 1000);
    }

    [Test]
    public void ExtendedFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame)
                    {Min = 30}, ElliottModelType.FLAT_EXTENDED);
            SaveResultFiles(model);
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
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame)
                    { Max = 70 }, ElliottModelType.DIAGONAL_CONTRACTING_ENDING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void ExpandingDiagonalTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.DIAGONAL_EXPANDING_INITIAL);
            SaveResultFiles(model);

            model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.DIAGONAL_EXPANDING_ENDING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void ZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.ZIGZAG);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void FlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.FLAT_REGULAR);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void DoubleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.DOUBLE_ZIGZAG);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void TripleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.TRIPLE_ZIGZAG);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void TriangleTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, m_TimeFrame)
                    {Max = 120}, ElliottModelType.TRIANGLE_CONTRACTING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void TriangleRunningTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, m_TimeFrame)
                    { Max = 120, Min = 50}, ElliottModelType.TRIANGLE_RUNNING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void TriangleExpandingTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, m_TimeFrame)
                    { Min = 50 }, ElliottModelType.TRIANGLE_EXPANDING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void CombinationTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, m_TimeFrame) 
                    { Min = 50, Max = 110 }, ElliottModelType.COMBINATION);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void RunningFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, m_TimeFrame)
                    {Max = 66, Min = 34}, ElliottModelType.FLAT_RUNNING);
            SaveResultFiles(model);
        }
    }

    [Test]
    public void ImpulseTest()
    {
        for (int i = 30; i <= 30; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                (DateTime, DateTime) dates = GetDateRange(i);
                ModelPattern model = m_PatternGenerator.GetPattern(
                    new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame)
                        {Max = 61}, ElliottModelType.IMPULSE);

                SaveResultFiles(model);
            }
        }
    }
}