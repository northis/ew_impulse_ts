using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.PatternGeneration;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Predictive Elliott Wave indicator (v2 engine — see EW_MARKUP_v2.md).
/// Analyses from the farthest extremum to the last closed bar using the
/// bottom-up interval-DP <see cref="ElliottWaveExactMarkupV2"/> engine,
/// producing projections for incomplete wave models (§13).
/// </summary>
[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class PredictiveElliottWaveIndicator : ElliottWaveIndicatorBase
{
    [Parameter(nameof(BarsCount), DefaultValue = 200, MinValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
    public int BarsCount { get; set; }

    [Parameter("Markup depth", DefaultValue = ElliottWaveExactMarkupV2.MAX_LEVELS_V2,
        MinValue = 1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MarkupDepth { get; set; }

    [Parameter("Show target zones", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
    public bool ShowTargetZones { get; set; }

    private int m_LastCalculatedIndex = -1;
    private IReadOnlyList<ElliottWaveExactMarkupV2.Segment> m_LastSegments;

    protected override void Initialize()
    {
        BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        UseV2 = true;
    }

    public override void Calculate(int index)
    {
        if (!IsLastBar) return;
        if (index == m_LastCalculatedIndex) return;
        m_LastCalculatedIndex = index;

        int startBarIndex = Math.Max(0, index - BarsCount + 1);

        // Find global max and min in the lookback window
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

        // Farthest extremum → analysis start.
        int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
        bool isUp = fartherBarIndex == minBarIndex; // start at min → expect upward

        // End point is the last *closed* bar (index - 1), because the current bar
        // is still forming and may not yet be present in the BarsProvider cache.
        int endBarIndex = index - 1;
        if (endBarIndex <= fartherBarIndex) return;

        // --- v2 engine: it builds its own minimal-period zigzag internally (§4) ---
        MarkupV2 = new ElliottWaveExactMarkupV2(
            BarProvider,
            fartherBarIndex,
            endBarIndex,
            deviationPercent: null,   // auto-optimal via FindOptimalDeviation()
            isUpDirection: !isUp);    // SimpleExtremumFinder uses "first isHigh"

        MarkupSearchResult result = MarkupV2.Parse();

        // Prefer the best projection (incomplete model at the right edge, §13);
        // fall back to the best-scoring complete root.
        TreeNode bestNode = result.BestProjection ?? result.Roots.FirstOrDefault();
        if (bestNode == null) return;

        m_LastSegments = MarkupV2.Segments;

        // Convert the v2 TreeNode tree to v1 ExactParsedNode for rendering.
        ExactParsedNode model = ConvertV2NodeToExactParsedNode(bestNode, m_LastSegments);

        Chart.RemoveAllObjects();

        // Draw confirmed + active waves
        DrawConfirmedWaves(model);

        // Draw projected tail waves from the v2 node's built-in projections (§13)
        if (bestNode.Projections?.Count > 0)
            DrawProjectedWaves(model, bestNode.Projections.ToList());

        // Draw cluster zones if enabled (v2 projected tail may carry clusters)
        if (ShowTargetZones)
        {
            // v2 projections don't carry clusters directly; clusters are computed
            // by the shared CalculateClusterZones helper from v1 when needed.
            var clusters = ElliottWaveExactMarkup.CalculateClusterZones(
                bestNode.Projections?.ToList());
            if (clusters?.Count > 0)
                DrawClusterZones(clusters);
        }
    }

    private void DrawConfirmedWaves(ExactParsedNode node)
    {
        if (node?.SubWaves == null) return;

        int confirmedCount = node.ActiveFromWaveIndex >= 0
            ? node.ActiveFromWaveIndex
            : node.WaveCount;

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

            // Draw sub-wave structure for confirmed waves (v2 style: draw one level down)
            if (MAIN_NOTATION_LEVEL > 0 && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                DrawMarkupLines(sw, "EW_s_", MAIN_NOTATION_LEVEL - 1, labels);
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
