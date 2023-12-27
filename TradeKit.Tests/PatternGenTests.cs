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

    private void SaveChart(List<ICandle> candles, string name, string fileName)
    {
        var dt = DateTime.UtcNow;
        dt = new DateTime(dt.Year, dt.Month, dt.Day);

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
        int bars = 30;
        List<ICandle> candles = m_PatternGenerator.GetExtendedFlat(
            new PatternArgsItem(40, 60, bars), 30).Candles;

        SaveChart(candles, "Extended flat", "img_ex_flat");
    }
}