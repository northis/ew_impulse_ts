using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Core.PatternGeneration;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Predictive Elliott Wave indicator that analyses from the farthest extremum
/// to the last closed bar, producing projections for incomplete wave models.
/// </summary>
[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class PredictiveElliottWaveIndicator : ElliottWaveIndicatorBase
{
    [Parameter(nameof(BarsCount), DefaultValue = 200, MinValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
    public int BarsCount { get; set; }

    [Parameter("Markup depth", DefaultValue = ElliottWaveExactMarkup.MAX_MARKUP_DEPTH, MinValue = 1, MaxValue = 5, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MarkupDepth { get; set; }

    [Parameter("Show target zones", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
    public bool ShowTargetZones { get; set; }

    private int m_LastCalculatedIndex = -1;
    private PredictionResult m_CachedPrediction;

    protected override void Initialize()
    {
        BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        Markup = new ElliottWaveExactMarkup(BarProvider, MarkupDepth);
    }

    public override void Calculate(int index)
    {
        if (!IsLastBar) return;
        if (index == m_LastCalculatedIndex) return;
        m_LastCalculatedIndex = index;

        int startBarIndex = Math.Max(0, index - BarsCount + 1);

        // Find global max and min
        double maxValue = double.MinValue;
        double minValue = double.MaxValue;
        int maxBarIndex = startBarIndex;
        int minBarIndex = startBarIndex;

        for (int i = startBarIndex; i <= index; i++)
        {
            double high = Bars.HighPrices[i];
            double low = Bars.LowPrices[i];

            if (high > maxValue) { maxValue = high; maxBarIndex = i; }
            if (low < minValue) { minValue = low; minBarIndex = i; }
        }

        if (maxBarIndex == minBarIndex) return;

        // Farthest extremum = startPoint; endPoint = last closed bar
        int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
        double startValue = fartherBarIndex == maxBarIndex ? maxValue : minValue;
        BarPoint startPoint = new BarPoint(startValue, fartherBarIndex, BarProvider);

        // endPoint is the last closed bar (current bar index)
        int endBarIndex = index;
        bool isUp = fartherBarIndex == minBarIndex; // if start is min, we go up
        double endValue = isUp
            ? BarProvider.GetHighPrice(endBarIndex)
            : BarProvider.GetLowPrice(endBarIndex);
        BarPoint endPoint = new BarPoint(endValue, endBarIndex, BarProvider);

        // Build inner zigzag
        var optimizer = new DeviationOptimizer(BarProvider, startPoint.BarIndex, endPoint.BarIndex, false);
        double optimalDev = optimizer.FindOptimalDeviation();
        SimpleExtremumFinder innerFinder = new SimpleExtremumFinder(optimalDev, BarProvider, !isUp);
        innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

        List<BarPoint> innerPoints = innerFinder.ToExtremaList()
            .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
            .ToList();

        if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
            innerPoints.Insert(0, startPoint);

        if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
            innerPoints.Add(endPoint);

        // Corridor fixes
        innerPoints = ExtremumFinderBase.EndFixCorridors(innerPoints, BarProvider);
        innerPoints = ExtremumFinderBase.RefineToCorridors(innerPoints, BarProvider);

        // Build active segment (§2.3): if last pivot != endBar, add active point
        BarPoint lastPivot = innerPoints[^1];
        if (lastPivot.BarIndex < endBarIndex)
        {
            bool activeIsUp = lastPivot.Value < endValue;
            double activePrice = activeIsUp
                ? BarProvider.GetHighPrice(endBarIndex)
                : BarProvider.GetLowPrice(endBarIndex);
            innerPoints.Add(new BarPoint(activePrice, endBarIndex, BarProvider));
        }

        // Run predictive parsing
        PredictionResult prediction = Markup.ParsePredictive(innerPoints, endBarIndex);

        if (prediction?.Model == null) return;

        m_CachedPrediction = prediction;
        Chart.RemoveAllObjects();

        ExactParsedNode model = prediction.Model;

        // Draw confirmed waves
        DrawConfirmedWaves(model);

        // Draw projected waves
        DrawProjectedWaves(model, prediction.Projections);

        // Draw cluster zones if enabled
        if (ShowTargetZones && prediction.Clusters?.Count > 0)
            DrawClusterZones(prediction.Clusters);
    }

    private void DrawConfirmedWaves(ExactParsedNode node)
    {
        if (node?.SubWaves == null) return;

        int confirmedCount = node.ActiveFromWaveIndex >= 0
            ? node.ActiveFromWaveIndex
            : node.WaveCount;

        // Draw confirmed sub-waves using the base class method
        // but only up to the confirmed count
        var labels = new List<MarkupLabelItem>();
        NotationItem[] notation = TryGetNotation(node.ModelType, MAIN_NOTATION_LEVEL);
        Color lineColor = GetWaveColor(node.ModelType);

        for (int i = 0; i < confirmedCount && i < node.SubWaves.Length; i++)
        {
            ExactParsedNode sw = node.SubWaves[i];
            if (sw == null) continue;

            string labelText = (notation != null && i < notation.Length)
                ? notation[i].NotationKey
                : ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);

            string name = $"EW_{sw.StartPoint.BarIndex}_{sw.EndPoint.BarIndex}_{labelText}";

            Chart.DrawTrendLine(name + "_l",
                sw.StartPoint.BarIndex, sw.StartPoint.Value,
                sw.EndPoint.BarIndex, sw.EndPoint.Value,
                lineColor, 1, LineStyle.Lines);

            labels.Add(new MarkupLabelItem(
                sw.EndPoint.BarIndex, sw.EndPoint.Value, sw.IsUp,
                name, labelText, MAIN_NOTATION_LEVEL, lineColor));

            // Draw sub-wave structure for confirmed waves
            if (MAIN_NOTATION_LEVEL > 0 && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                DrawMarkupLines(sw, "EW_s_", (byte)(MAIN_NOTATION_LEVEL - 1), labels);
        }

        // Draw active (unconfirmed) waves with dashed lines
        for (int i = confirmedCount; i < node.WaveCount && i < node.SubWaves.Length; i++)
        {
            ExactParsedNode sw = node.SubWaves[i];
            if (sw == null) continue;

            string labelText = (notation != null && i < notation.Length)
                ? notation[i].NotationKey
                : ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);

            string name = $"EW_A_{sw.StartPoint.BarIndex}_{sw.EndPoint.BarIndex}_{labelText}";
            Color halfAlpha = Color.FromArgb(128, lineColor.R, lineColor.G, lineColor.B);

            Chart.DrawTrendLine(name + "_l",
                sw.StartPoint.BarIndex, sw.StartPoint.Value,
                sw.EndPoint.BarIndex, sw.EndPoint.Value,
                halfAlpha, 1, LineStyle.DotsRare);

            labels.Add(new MarkupLabelItem(
                sw.EndPoint.BarIndex, sw.EndPoint.Value, sw.IsUp,
                name, labelText + "?", MAIN_NOTATION_LEVEL, halfAlpha));
        }

        DrawStackedLabels(labels);
    }

    private void DrawProjectedWaves(ExactParsedNode node, List<WaveProjection> projections)
    {
        if (projections == null || projections.Count == 0) return;

        Color lineColor = GetWaveColor(node.ModelType);
        Color projColor = Color.FromArgb(128, lineColor.R, lineColor.G, lineColor.B);

        double lastPrice = node.EndPoint.Value;
        int lastBar = node.EndPoint.BarIndex;

        foreach (var proj in projections)
        {
            string pName = $"PROJ_{lastBar}_{proj.BarIndex}_{proj.WaveName}";

            Chart.DrawTrendLine(pName + "_line",
                lastBar, lastPrice, proj.BarIndex, proj.Price,
                projColor, 1, LineStyle.DotsRare);

            double pyOffset = lastPrice < proj.Price
                ? Symbol.PipSize * 2
                : -Symbol.PipSize * 2;
            Chart.DrawText(pName, $"({proj.WaveName})?",
                proj.BarIndex, proj.Price + pyOffset, projColor);

            lastPrice = proj.Price;
            lastBar = proj.BarIndex;
        }
    }

    private void DrawClusterZones(List<ClusterZone> clusters)
    {
        Color zoneColor = Color.FromArgb(40, 100, 180, 100);

        for (int i = 0; i < clusters.Count; i++)
        {
            var zone = clusters[i];
            string name = $"ZONE_{i}_{zone.BarFrom}";

            Chart.DrawRectangle(name,
                zone.BarFrom, zone.PriceLow,
                zone.BarTo + 5, zone.PriceHigh,
                zoneColor);
        }
    }
}
