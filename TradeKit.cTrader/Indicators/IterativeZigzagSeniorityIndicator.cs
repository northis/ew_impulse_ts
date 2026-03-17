using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Zigzag indicator that shows sub-extrema within each confirmed segment.
/// Iteratively decreases zigzag deviation from the segment size down to the minimum,
/// assigning a seniority rank to each inner extremum via <see cref="MovementStatistic.GetSubExtremumRanks"/>.
/// Rank 1 = appeared earliest (coarsest deviation) = highest seniority.
/// A step increments only when the inner extrema set actually changes.
/// </summary>
[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeZigzagSeniorityIndicator : Indicator
{
    /// <summary>
    /// Gets or sets the zigzag deviation percent threshold (starting deviation for iterative analysis).
    /// </summary>
    [Parameter(nameof(DeviationPercent), DefaultValue = 0.3, MinValue = 0.01, Group = Helper.TRADE_SETTINGS_NAME)]
    public double DeviationPercent { get; set; }

    private IBarsProvider m_BarProvider;
    private SimpleExtremumFinder m_ExtremumFinder;

    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_ExtremumFinder = new SimpleExtremumFinder(DeviationPercent, m_BarProvider);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
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
        string lineId = $"IIZ_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value,
            segEnd.BarIndex, segEnd.Value,
            Color.FromArgb(100, 180, 180, 180), 1);
    }

    /// <summary>
    /// Draws a confirmed (both endpoints final) segment with its trend line and inner rank labels.
    /// </summary>
    /// <param name="segStart">The segment start point.</param>
    /// <param name="segEnd">The segment end point.</param>
    private void DrawConfirmedSegment(BarPoint segStart, BarPoint segEnd)
    {
        string lineId = $"IIZ_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value,
            segEnd.BarIndex, segEnd.Value,
            Color.FromArgb(220, 230, 230, 230), 2);

        DrawRankLabels(segStart, segEnd);
    }

    /// <summary>
    /// Computes iterative seniority ranks for inner extrema of the segment and draws rank labels.
    /// </summary>
    /// <param name="segStart">The segment start point.</param>
    /// <param name="segEnd">The segment end point.</param>
    private void DrawRankLabels(BarPoint segStart, BarPoint segEnd)
    {
        Dictionary<int, (BarPoint Point, int Rank)> ranks = MovementStatistic.GetSubExtremumRanks(segStart, segEnd, m_BarProvider);

        foreach (KeyValuePair<int, (BarPoint Point, int Rank)> entry in ranks)
        {
            BarPoint point = entry.Value.Point;
            int rank = entry.Value.Rank;

            string id = $"SR_{segStart.BarIndex}_{point.BarIndex}";

            double segLength = segEnd.BarIndex - segStart.BarIndex;
            double progress = segLength > 0
                ? (double)(point.BarIndex - segStart.BarIndex) / segLength
                : 0.5;
            double interpolated = segStart.Value + progress * (segEnd.Value - segStart.Value);
            bool isAbove = point.Value > interpolated;

            Color rankColor = GetRankColor(rank);
            ChartText text = Chart.DrawText(id, rank.ToString(), point.OpenTime, point.Value, rankColor);
            text.VerticalAlignment = isAbove ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            text.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    /// <summary>
    /// Returns a color for the given rank: rank 1 is brightest, higher ranks are progressively dimmer.
    /// </summary>
    /// <param name="rank">The seniority rank (1 = highest).</param>
    /// <returns>The color to use for this rank.</returns>
    private static Color GetRankColor(int rank)
    {
        int brightness = Math.Max(60, 255 - (rank - 1) * 45);
        return Color.FromArgb(230, brightness, brightness, 0);
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
