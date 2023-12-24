using System.Reflection;
using NUnit.Framework;
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
    public void PatternChartTest()
    {
        var bars = 103;
        var candles = m_PatternGenerator.GetExtendedFlat(
            new PatternArgsItem(100, 105, bars), 98);

        var dt = DateTime.UtcNow;
        var step = TimeSpan.FromMinutes(15);
        DateTime dtStart = dt.Add(-bars* step);

        GenericChart.GenericChart chart = ChartGenerator.GetCandlestickChart(candles, "Extended flat", dtStart, step);

        string pngPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "img_ex_flat");
        chart.SavePNG(pngPath, null, 1000, 1000);
    }

}