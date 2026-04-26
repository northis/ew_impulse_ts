using System;
using System.Collections.Generic;
using System.Linq;
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

    private IBarsProvider m_BarProvider;
    private ElliottWaveExactMarkup m_Markup;

    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_Markup = new ElliottWaveExactMarkup(m_BarProvider);
    }

    public override void Calculate(int index)
    {
        if (!IsLastBar)
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

        ExactParsedNode best = parsed.Count > 0
            ? parsed
                .OrderByDescending(a => a.GetDepth())
                .ThenByDescending(a => a.Score)
                .First()
            : null;

        if (best == null)
            return;

        Chart.RemoveAllObjects();

        // Draw best result in yellow with Minuette (and sub-wave Subminuette) notation
        DrawMarkupNode(best, Color.Yellow, "EW_", MAIN_NOTATION_LEVEL);

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

    /// <summary>
    /// Recursively draws the sub-wave labels and trend lines for <paramref name="node"/>
    /// using Elliott Wave notation at <paramref name="notationLevel"/>.<br/>
    /// Main level (level 4 = Minuette): impulse waves → <c>(i)</c>, <c>(ii)</c> …;
    ///   corrective waves → <c>(a)</c>, <c>(b)</c>, <c>(c)</c>.<br/>
    /// Sub-level (level 3 = Subminuette): impulse waves → <c>i</c>, <c>ii</c> …
    /// </summary>
    private void DrawMarkupNode(
        ExactParsedNode node, Color color, string prefix, byte notationLevel)
    {
        if (node == null || node.WaveCount == 0) return;

        NotationItem[] notation = TryGetNotation(node.ModelType, notationLevel);

        for (int i = 0; i < node.WaveCount; i++)
        {
            ExactParsedNode sw = node.SubWaves?[i];
            if (sw == null) continue;

            string labelText = (notation != null && i < notation.Length)
                ? notation[i].NotationKey
                : ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);

            string name = $"{prefix}{sw.StartPoint.BarIndex}_{sw.EndPoint.BarIndex}_{labelText}";
            double yOffset = sw.IsUp ? Symbol.PipSize * 2 : -Symbol.PipSize * 2;

            Chart.DrawText(name + "_t", labelText,
                sw.EndPoint.BarIndex, sw.EndPoint.Value + yOffset, color);
            Chart.DrawTrendLine(name + "_l",
                sw.StartPoint.BarIndex, sw.StartPoint.Value,
                sw.EndPoint.BarIndex, sw.EndPoint.Value,
                color, 1, LineStyle.Lines);

            // Recurse one level deeper when the sub-wave has been identified as a real model
            if (notationLevel > 0 && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                DrawMarkupNode(sw, color, prefix + "s_", (byte)(notationLevel - 1));
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
