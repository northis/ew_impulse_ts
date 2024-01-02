using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core;
using TradeKit.Impulse;
using TradeKit.PatternGeneration;

namespace TradeKit.Tests;

public class PatternGenTests
{
    private PatternGenerator m_PatternGenerator;

    private static readonly string FOLDER_TO_SAVE = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "images");

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator();

        if (!Directory.Exists(FOLDER_TO_SAVE))
            Directory.CreateDirectory(FOLDER_TO_SAVE);
    }

    [Test]
    public void ImpulseRandomSetTest()
    {
        List<ICandle> candles = m_PatternGenerator.GetImpulseRandomSet(
            new PatternArgsItem(10, 20, 8));

        SaveChart(candles, "Random set (impulse)", "imp_img_rnd_set");
    }

    [Test]
    public void CorrectiveRandomSetTest()
    {
        List<ICandle> candles = m_PatternGenerator.GetCorrectiveRandomSet(
            new PatternArgsItem(500, 450, 20));

        SaveChart(candles, "Random set (corrective)", "cor_img_rnd_set");
    }

    [Test]
    public void SideRandomSetTest()
    {
        List<ICandle> candles = m_PatternGenerator.GetSideRandomSet(
            new PatternArgsItem(50, 70, 15), 40, 80);

        SaveChart(candles, "Random set (side)", "side_img_rnd_set");
    }

    private void SaveChart(
        List<ICandle> candles, string name, string fileName,
        ModelPattern? model = null)
    {
        DateTime dt = DateTime.UtcNow;
        dt = new DateTime(dt.Year, dt.Month, dt.Day);

        TimeSpan step = TimeSpan.FromMinutes(15);
        DateTime dtStart = dt.Add(-candles.Count * step);

        GenericChart.GenericChart chart = ChartGenerator.GetCandlestickChart(
            candles, name, dtStart, step);

        if (model?.PatternKeyPoints != null)
        {
            var annotations = new List<Annotation>();
            string[] names = 
                m_PatternGenerator.ModelRules[model.Model].Models.Keys.ToArray();

            for (int i = 0; i < model.PatternKeyPoints.Count; i++)
            {
                var annotation = model.PatternKeyPoints[i];
                DateTime date = dtStart.Add(step * annotation.Key);
                annotations.Add(ChartGenerator.GetAnnotation(
                    date,
                    annotation.Value,
                    ChartGenerator.WHITE_COLOR,
                    ChartGenerator.CHART_FONT_MAIN,
                    ChartGenerator.SEMI_WHITE_COLOR, names[i]));
            }

            chart.WithAnnotations(annotations);
        }

        string pngPath = Path.Combine(FOLDER_TO_SAVE, fileName);
        chart.SavePNG(pngPath, null, 1000, 1000);
    }

    [Test]
    public void ExtendedFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, i) {Min = 30}, ElliottModelType.FLAT_EXTENDED);
            Console.WriteLine($" bar count is {i}");
            Console.WriteLine("Model result:");
            Console.WriteLine(model.ToString());
            SaveChart(model.Candles, "Extended flat", $"img_ex_flat_{i}", model);
        }
    }

    [Test]
    public void ImpulseTest()
    {
        for (int i = 25; i <= 30; i++)
        {
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, i), ElliottModelType.IMPULSE);

            Console.WriteLine($" bar count is {i}");
            Console.WriteLine("Model result:");
            Console.WriteLine(model.ToString());
            SaveChart(model.Candles, "Impulse", $"img_imp_{i}", model);
        }
    }
}