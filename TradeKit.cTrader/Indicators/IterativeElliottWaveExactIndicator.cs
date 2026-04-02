using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeElliottWaveExactIndicator : Indicator
{
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
        ExactParsedNode best = parsed.Count > 0 ? parsed[0] : null;
        ExactParsedNode second = parsed.Count > 1 ? parsed[1] : null;

        if (best == null)
            return;

        Chart.RemoveAllObjects();

        // Draw best result in yellow
        DrawMarkup(best, Color.Yellow, "EW_");

        // Draw second-best result in gray (alternative markup)
        if (second != null)
            DrawMarkup(second, Color.Gray, "EW2_");

        var projections = m_Markup.GetProjections(best);
        if (projections.Count <= 0)
            return;

        int lastIndex = best.EndPoint.BarIndex;
        double lastValue = best.EndPoint.Value;
        foreach (var proj in projections)
        {
            string pName = $"PROJ_{lastIndex}_{proj.BarIndex}_{proj.Name}";

            Chart.DrawTrendLine(pName + "_line", lastIndex, lastValue, proj.BarIndex, proj.Value, Color.Gray, 1, LineStyle.LinesDots);

            double pyOffset = lastValue < proj.Value ? Symbol.PipSize * 10 : -Symbol.PipSize * 10;
            Chart.DrawText(pName, $"({proj.Name})", proj.BarIndex, proj.Value + pyOffset, Color.Gray);

            lastIndex = proj.BarIndex;
            lastValue = proj.Value;
        }
    }

    private void DrawMarkup(ExactParsedNode node, Color color, string prefix)
    {
        List<MarkupResult> flat = node.ToMarkupResult().Flatten().ToList();
        foreach (MarkupResult res in flat)
        {
            if (string.IsNullOrEmpty(res.NodeName))
                continue;

            string name = $"{prefix}{res.Start.BarIndex}_{res.End.BarIndex}_{res.Level}_{res.NodeName}";
            double yOffset = res.IsUp ? Symbol.PipSize * 10 : -Symbol.PipSize * 10;

            Chart.DrawText(name, res.NodeName, res.End.BarIndex, res.End.Value + yOffset, color);
            Chart.DrawTrendLine(name + "_line", res.Start.BarIndex, res.Start.Value,
                res.End.BarIndex, res.End.Value, color, 1, LineStyle.Lines);
        }
    }
}
