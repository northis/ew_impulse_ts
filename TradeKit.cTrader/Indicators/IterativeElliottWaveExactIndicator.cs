using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Core.PatternGeneration;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeElliottWaveExactIndicator : Indicator
{
    /// <summary>
    /// Notation level used for the main (top-level) wave labels.
    /// case 4 = Minuette: impulse waves → (i) (ii) (iii) (iv) (v);
    ///                    corrective waves → (a) (b) (c) etc.
    /// </summary>
    private const byte MAIN_NOTATION_LEVEL = 4;

    [Parameter(nameof(BarsCount), DefaultValue = 100, MinValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
    public int BarsCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether candle information should be saved to file.
    /// </summary>
    [Parameter("Save candles", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveCandles { get; set; }

    /// <summary>
    /// Gets or sets the date range for saving candles to a .csv file.
    /// </summary>
    [Parameter("Dates to save", DefaultValue = Helper.DATE_COLLECTION_PATTERN, Group = Helper.DEV_SETTINGS_NAME)]
    public string DateRangeToCollect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the candles of the currently identified markup
    /// range should be saved to a .csv file whenever a best result is found.
    /// </summary>
    [Parameter("Save markup candles", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveMarkupCandles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether PNG charts for all markup variants should be
    /// saved once, immediately after the first successful parse.
    /// </summary>
    [Parameter("Save markup charts", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveMarkupCharts { get; set; }

    private IBarsProvider m_BarProvider;
    private ElliottWaveExactMarkup m_Markup;
    private bool m_CandlesSaved;
    private bool m_MarkupCandlesSaved;
    private bool m_MarkupChartsSaved;
    private ExactParsedNode m_CachedBest;

    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_Markup = new ElliottWaveExactMarkup(m_BarProvider);
    }

    public override void Calculate(int index)
    {
        if (SaveCandles && !m_CandlesSaved)
        {
            string savedFilePath = m_BarProvider.SaveCandlesForDateRange(DateRangeToCollect);
            if (!string.IsNullOrEmpty(savedFilePath))
            {
                m_CandlesSaved = true;
                Print($"Candles saved to: {savedFilePath}");
            }
        }

        if (!IsLastBar)
            return;

        // Skip full recalculation while the price stays within the already-marked range.
        if (m_CachedBest != null && index <= m_CachedBest.EndPoint.BarIndex)
            return;

        int startBarIndex = Math.Max(0, index - BarsCount + 1);

        double maxValue = double.MinValue;
        double minValue = double.MaxValue;
        int maxBarIndex = startBarIndex;
        int minBarIndex = startBarIndex;

        for (int i = startBarIndex; i <= index; i++)
        {
            double high = Bars.HighPrices[i];
            double low = Bars.LowPrices[i];

            if (high > maxValue)
            {
                maxValue = high;
                maxBarIndex = i;
            }

            if (low < minValue)
            {
                minValue = low;
                minBarIndex = i;
            }
        }

        if (maxBarIndex == minBarIndex)
            return;

        // farther = older (smaller bar index), closer = newer (larger bar index)
        int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
        int closerBarIndex = Math.Max(maxBarIndex, minBarIndex);

        double startValue = fartherBarIndex == maxBarIndex ? maxValue : minValue;
        double endValue = closerBarIndex == maxBarIndex ? maxValue : minValue;

        BarPoint startPoint = new BarPoint(startValue, fartherBarIndex, m_BarProvider);
        BarPoint endPoint = new BarPoint(endValue, closerBarIndex, m_BarProvider);

        bool isUp = endPoint.Value > startPoint.Value;
        SimpleExtremumFinder innerFinder = new SimpleExtremumFinder(0.01, m_BarProvider, !isUp);
        innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

        List<BarPoint> innerPoints = innerFinder.ToExtremaList()
            .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
            .ToList();

        if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
            innerPoints.Insert(0, startPoint);

        if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
            innerPoints.Add(endPoint);

        List<ExactParsedNode> parsed = m_Markup.Parse(innerPoints);

        if (SaveMarkupCharts && !m_MarkupChartsSaved && parsed.Count > 0)
        {
            m_MarkupChartsSaved = true;
            IBarsProvider provider = m_BarProvider;
            string symName = m_BarProvider.BarSymbol.Name;
            string tfName = m_BarProvider.TimeFrame.ShortName;
            byte level = MAIN_NOTATION_LEVEL;

            ThreadPool.QueueUserWorkItem(markups =>
            {
                ExactParsedNode[] markupArray = (ExactParsedNode[])markups;
                int variantIdx = 0;
                if (markupArray != null)
                {
                    foreach (ExactParsedNode node in markupArray)
                    {
                        string chartFileName = string.Format("{0}_{1}_{2}_{3:D2}",
                            symName, tfName, node.ModelType, variantIdx++);
                        string chartFilePath = Path.Combine(Helper.DirectoryToSaveResults, chartFileName);
                        string savedPath = ChartGenerator.GenerateMarkupChart(node, provider, level, chartFilePath);
                        if (!string.IsNullOrEmpty(savedPath))
                            Print($"Markup chart saved: {savedPath}");
                    }
                }
            }, parsed.ToArray());
        }

        ExactParsedNode best = parsed.Count > 0
            ? parsed
                .OrderByDescending(a => a.GetDepth())
                .ThenByDescending(a => a.Score)
                .First()
            : null;

        if (best == null)
            return;

        m_CachedBest = best;

        if (SaveMarkupCandles && !m_MarkupCandlesSaved)
        {
            DateTime markupStart = m_BarProvider.GetOpenTime(best.StartPoint.BarIndex);
            DateTime markupEnd   = m_BarProvider.GetOpenTime(best.EndPoint.BarIndex);
            if (markupStart != default && markupEnd != default)
            {
                string markupFileName = string.Format(
                    Helper.CANDLE_FILE_NAME_FORMAT,
                    m_BarProvider.BarSymbol.Name,
                    m_BarProvider.TimeFrame.ShortName,
                    markupStart.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"),
                    markupEnd.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"));
                string markupFilePath = Path.Combine(Helper.DirectoryToSaveResults, markupFileName);
                m_BarProvider.SaveCandles(markupStart, markupEnd, markupFilePath);
                m_MarkupCandlesSaved = true;
                Print($"Markup candles saved to: {markupFilePath}");
            }
        }

        Chart.RemoveAllObjects();

        // Draw best result with model-type colours and stacked wave labels
        DrawMarkupNode(best, "EW_", MAIN_NOTATION_LEVEL);

        var projections = m_Markup.GetProjections(best);
        if (projections.Count <= 0)
            return;

        int lastIndex = best.EndPoint.BarIndex;
        double lastValue = best.EndPoint.Value;
        foreach (var proj in projections)
        {
            string pName = $"PROJ_{lastIndex}_{proj.BarIndex}_{proj.Name}";

            Chart.DrawTrendLine(pName + "_line", lastIndex, lastValue, proj.BarIndex, proj.Value, Color.Gray, 1, LineStyle.LinesDots);

            double pyOffset = lastValue < proj.Value ? Symbol.PipSize * 2 : -Symbol.PipSize * 2;
            Chart.DrawText(pName, $"({proj.Name})", proj.BarIndex, proj.Value + pyOffset, Color.Gray);

            lastIndex = proj.BarIndex;
            lastValue = proj.Value;
        }
    }

    /// <summary>Returns the cTrader chart color for an Elliott wave model type.</summary>
    private static Color GetWaveColor(ElliottModelType modelType) => modelType switch
    {
        ElliottModelType.IMPULSE or ElliottModelType.SIMPLE_IMPULSE or
        ElliottModelType.DIAGONAL_CONTRACTING_INITIAL or ElliottModelType.DIAGONAL_CONTRACTING_ENDING or
        ElliottModelType.DIAGONAL_EXPANDING_INITIAL  or ElliottModelType.DIAGONAL_EXPANDING_ENDING
            => Color.FromHex("#3D85C6"),
        ElliottModelType.ZIGZAG or ElliottModelType.DOUBLE_ZIGZAG or ElliottModelType.TRIPLE_ZIGZAG
            => Color.FromHex("#FF9800"),
        ElliottModelType.TRIANGLE_CONTRACTING or ElliottModelType.TRIANGLE_EXPANDING or
        ElliottModelType.TRIANGLE_RUNNING
            => Color.FromHex("#787B86"),
        _ => Color.FromHex("#6AA84F")
    };

    private record MarkupLabelItem(
        int BarIndex, double Value, bool IsUp,
        string Name, string LabelText,
        byte NotationLevel, Color LabelColor);

    /// <summary>
    /// Draws all wave lines and stacks labels at shared bar endpoints:
    /// youngest (innermost sub-wave) closest to the price bar, oldest furthest.
    /// </summary>
    private void DrawMarkupNode(ExactParsedNode node, string prefix, byte notationLevel)
    {
        if (node == null || node.WaveCount == 0) return;
        var labels = new List<MarkupLabelItem>();
        DrawMarkupLines(node, prefix, notationLevel, labels);
        DrawStackedLabels(labels);
    }

    private void DrawMarkupLines(
        ExactParsedNode node, string prefix, byte notationLevel,
        List<MarkupLabelItem> labels)
    {
        if (node == null || node.WaveCount == 0) return;

        NotationItem[] notation = TryGetNotation(node.ModelType, notationLevel);
        Color lineColor = GetWaveColor(node.ModelType);

        for (int i = 0; i < node.WaveCount; i++)
        {
            ExactParsedNode sw = node.SubWaves?[i];
            if (sw == null) continue;

            string labelText = (notation != null && i < notation.Length)
                ? notation[i].NotationKey
                : ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);

            string name = $"{prefix}{sw.StartPoint.BarIndex}_{sw.EndPoint.BarIndex}_{labelText}";
            Color labelColor = GetWaveColor(
                sw.ModelType != ElliottModelType.SIMPLE_IMPULSE ? sw.ModelType : node.ModelType);

            Chart.DrawTrendLine(name + "_l",
                sw.StartPoint.BarIndex, sw.StartPoint.Value,
                sw.EndPoint.BarIndex,   sw.EndPoint.Value,
                lineColor, 1, LineStyle.Lines);

            labels.Add(new MarkupLabelItem(
                sw.EndPoint.BarIndex, sw.EndPoint.Value, sw.IsUp,
                name, labelText, notationLevel, labelColor));

            if (notationLevel > 0 && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                DrawMarkupLines(sw, prefix + "s_", (byte)(notationLevel - 1), labels);
        }
    }

    /// <summary>
    /// Groups collected label items by bar index and draws them vertically stacked,
    /// with the youngest wave label (lowest notation level) closest to the price bar.
    /// Each consecutive label is offset by 6 pips further from the bar.
    /// </summary>
    private void DrawStackedLabels(List<MarkupLabelItem> labels)
    {
        foreach (var grp in labels.GroupBy(x => x.BarIndex))
        {
            var sorted = grp.OrderBy(x => x.NotationLevel).ToList();
            bool isUp  = sorted[0].IsUp;
            double sign   = isUp ? 1.0 : -1.0;
            double offset = 2.0;
            foreach (var item in sorted)
            {
                Chart.DrawText(item.Name + "_t", item.LabelText,
                    item.BarIndex, item.Value + sign * Symbol.PipSize * offset,
                    item.LabelColor);
                offset += 6.0;
            }
        }
    }

    /// <summary>
    /// Returns the notation items for the given model at the specified wave-degree level,
    /// or <c>null</c> when the model is not registered in <see cref="NotationHelper"/>.
    /// </summary>
    private static NotationItem[] TryGetNotation(ElliottModelType model, byte level)
    {
        try { return NotationHelper.GetNotation(model, level); }
        catch { return null; }
    }
}
