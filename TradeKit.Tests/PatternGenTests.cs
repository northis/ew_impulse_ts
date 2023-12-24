using Plotly.NET;
using Plotly.NET.ImageExport;
using TradeKit.Core;
using TradeKit.PatternGeneration;

namespace TradeKit.Tests;

public class PatternGenTests
{
    private PatternGenerator m_PatternGenerator;

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator();
    }

    [Test]
    public void RandomSetTest()
    {
        List<ICandle> candles = m_PatternGenerator.GetRandomSet(
            new PatternArgsItem(1.08, 1.05, 10));

        SaveChart(candles, "Random set", "img_rnd_set");
    }

    private void SaveChart(List<ICandle> candles, string name, string fileName)
    {
        var dt = DateTime.UtcNow;
        var step = TimeSpan.FromMinutes(15);
        DateTime dtStart = dt.Add(-candles.Count * step);

        GenericChart.GenericChart chart = ChartGenerator.GetCandlestickChart(
            candles, name, dtStart, step);

        string pngPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, fileName);
        chart.SavePNG(pngPath, null, 1000, 1000);
    }

    [Test]
    public void ExtendedFlatTest()
    {
        int bars = 13;
        List<ICandle> candles = m_PatternGenerator.GetExtendedFlat(
            new PatternArgsItem(100, 105, bars), 98);

        SaveChart(candles, "Extended flat", "img_ex_flat");
    }

}