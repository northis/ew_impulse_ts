using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;
using TradeKit.Core.PatternGeneration;
using TradeKit.Core.ElliottWave;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Zigzag indicator that shows Elliott Wave sub-structures within each confirmed segment.
/// Uses inner extrema rankings to map to the best fitting Elliott Wave model recursively.
/// </summary>
//[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeElliottWaveIndicator : Indicator
{
    /// <summary>
    /// Gets or sets the zigzag deviation percent threshold (starting deviation for iterative analysis).
    /// </summary>
    [Parameter(nameof(DeviationPercent), DefaultValue = 0.3, MinValue = 0.01, Group = Helper.TRADE_SETTINGS_NAME)]
    public double DeviationPercent { get; set; }

    /// <summary>
    /// Gets or sets the maximum depth for Elliott Wave sub-structure analysis.
    /// </summary>
    [Parameter(nameof(MaxDepth), DefaultValue = 3, MinValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MaxDepth { get; set; }

    private IBarsProvider m_BarProvider;
    private SimpleExtremumFinder m_ExtremumFinder;
    private ElliottWaveMarkup m_Markup;

    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_ExtremumFinder = new SimpleExtremumFinder(DeviationPercent, m_BarProvider);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
        m_Markup = new ElliottWaveMarkup();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        base.OnDestroy();
    }

    private void OnSetExtremum(object sender, ExtremumFinderBase.BarPointEventArgs e)
    {
        var extremaValues = m_ExtremumFinder.Extrema.Values;
        int count = extremaValues.Count;

        if (count < 2)
            return;

        BarPoint segStart = extremaValues[count - 2];
        BarPoint segEnd = e.EventExtremum;

        if (count >= 3)
        {
            BarPoint prevStart = extremaValues[count - 3];
            DrawConfirmedSegment(prevStart, segStart);
        }

        DrawCurrentSegment(segStart, segEnd);
    }

    /// <summary>
    /// Draws the current (not yet confirmed) segment as a dimmed line without rank labels.
    /// </summary>
    /// <param name="segStart">The segment start point.</param>
    /// <param name="segEnd">The segment end point.</param>
    private void DrawCurrentSegment(BarPoint segStart, BarPoint segEnd)
    {
        string lineId = $"IEW_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value,
            segEnd.BarIndex, segEnd.Value,
            Color.FromArgb(100, 180, 180, 180), 1);
    }

    /// <summary>
    /// Draws a confirmed (both endpoints final) segment with its trend line and inner EW labels.
    /// </summary>
    /// <param name="segStart">The segment start point.</param>
    /// <param name="segEnd">The segment end point.</param>
    private void DrawConfirmedSegment(BarPoint segStart, BarPoint segEnd)
    {
        string lineId = $"IEW_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value,
            segEnd.BarIndex, segEnd.Value,
            Color.FromArgb(220, 230, 230, 230), 2);

        DrawElliottWaves(segStart, segEnd);
    }

    /// <summary>
    /// Computes iterative seniority ranks for inner extrema and maps EW structure.
    /// </summary>
    /// <param name="segStart">The segment start point.</param>
    /// <param name="segEnd">The segment end point.</param>
    private void DrawElliottWaves(BarPoint segStart, BarPoint segEnd)
    {
        Dictionary<int, (BarPoint Point, int Rank)> ranks = MovementStatistic.GetSubExtremumRanks(segStart, segEnd, m_BarProvider);
        
        List<MarkupResult> results = m_Markup.ParseSegment(segStart, segEnd, ranks, MaxDepth);
        if (results.Count == 0) return;

        MarkupResult result = results.FirstOrDefault();
        var allNodes = result.Flatten().ToList();

        foreach (var node in allNodes)
        {
            if (string.IsNullOrEmpty(node.NodeName)) continue;

            NotationItem notation = NotationHelper.GetNotation(node.ModelType, node.Level).FirstOrDefault(n => n.Key == node.NodeName);
            if (notation == null) continue;

            string id = $"EW_{segStart.BarIndex}_{node.End.BarIndex}_{node.Level}";

            double segLength = node.End.BarIndex - node.Start.BarIndex;
            double progress = segLength > 0
                ? (double)(node.End.BarIndex - node.Start.BarIndex) / segLength
                : 0.5;
            double interpolated = node.Start.Value + progress * (node.End.Value - node.Start.Value);
            bool isAbove = node.End.Value > interpolated;

            Color nodeColor = GetRankColor(node.Level);
            ChartText text = Chart.DrawText(id, notation.NotationKey, node.End.OpenTime, node.End.Value, nodeColor);
            text.VerticalAlignment = isAbove ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            text.HorizontalAlignment = HorizontalAlignment.Center;
        }

        // Draw sub-wave boundaries
        foreach (var node in allNodes)
        {
            if (node.Boundaries.Count > 0)
            {
                var points = new List<BarPoint> { node.Start };
                points.AddRange(node.Boundaries);
                points.Add(node.End);

                for (int i = 0; i < points.Count - 1; i++)
                {
                    string lineId = $"EWL_{segStart.BarIndex}_{points[i].BarIndex}_{points[i+1].BarIndex}_{node.Level}";
                    Color lineColor = GetRankColor(node.Level);
                    Chart.DrawTrendLine(lineId,
                        points[i].BarIndex, points[i].Value,
                        points[i+1].BarIndex, points[i+1].Value,
                        Color.FromArgb(150, lineColor.R, lineColor.G, lineColor.B), 1);
                }
            }
        }
    }

    /// <summary>
    /// Returns a color for the given level: level 1 is brightest, higher levels are progressively dimmer.
    /// </summary>
    /// <param name="level">The seniority level (0 = highest).</param>
    /// <returns>The color to use for this level.</returns>
    private static Color GetRankColor(int level)
    {
        int brightness = Math.Max(60, 255 - level * 45);
        
        switch (level % 3)
        {
            case 0: return Color.FromArgb(230, brightness, brightness, 0); // Yellow-ish
            case 1: return Color.FromArgb(230, 0, brightness, brightness); // Cyan-ish
            case 2: return Color.FromArgb(230, brightness, 0, brightness); // Magenta-ish
            default: return Color.FromArgb(230, brightness, brightness, brightness);
        }
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
