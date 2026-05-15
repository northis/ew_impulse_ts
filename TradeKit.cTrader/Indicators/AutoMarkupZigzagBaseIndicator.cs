using cAlgo.API;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Zigzag-based Elliott Wave indicator. Builds a zigzag using
/// <see cref="SimpleExtremumFinder"/> with a configurable deviation percent,
/// then runs the Elliott Wave markup algorithm on each segment independently.
/// </summary>
[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class AutoMarkupZigzagBaseIndicator : ElliottWaveIndicatorBase
{
    private const double INNER_DEVIATION = 0.01;

    [Parameter("Zigzag deviation %", DefaultValue = 0.3, MinValue = 0.01, Group = Helper.TRADE_SETTINGS_NAME)]
    public double DeviationPercent { get; set; }

    [Parameter("Markup depth", DefaultValue = ElliottWaveExactMarkup.MAX_MARKUP_DEPTH, MinValue = 1, MaxValue = 5, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MarkupDepth { get; set; }

    private SimpleExtremumFinder m_ExtremumFinder;
    private readonly Dictionary<int, ExactParsedNode> m_SegmentMarkups = new();

    protected override void Initialize()
    {
        BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        Markup = new ElliottWaveExactMarkup(BarProvider, MarkupDepth);
        m_ExtremumFinder = new SimpleExtremumFinder(DeviationPercent, BarProvider);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

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
            // Previous segment's endpoint may have moved; redraw it.
            BarPoint prevStart = extremaValues[count - 3];
            BarPoint prevEnd = segStart;
            DrawSegmentWithMarkup(prevStart, prevEnd);
        }

        DrawSegmentWithMarkup(segStart, segEnd);
    }

    private void DrawSegmentWithMarkup(BarPoint segStart, BarPoint segEnd)
    {
        // Remove previous objects for this segment
        string segPrefix = $"AZ_{segStart.BarIndex}_";
        RemoveObjectsByPrefix(segPrefix);

        bool isUp = segEnd.Value > segStart.Value;

        // Draw the zigzag line
        Color zigzagColor = Color.FromArgb(200, 180, 180, 180);
        Chart.DrawTrendLine(segPrefix + "line",
            segStart.BarIndex, segStart.Value,
            segEnd.BarIndex, segEnd.Value,
            zigzagColor, 1, LineStyle.Lines);

        // Build inner zigzag for markup
        SimpleExtremumFinder innerFinder = new SimpleExtremumFinder(INNER_DEVIATION, BarProvider, !isUp);
        innerFinder.Calculate(segStart.BarIndex, segEnd.BarIndex);

        List<BarPoint> innerPoints = innerFinder.ToExtremaList()
            .Where(p => p.BarIndex >= segStart.BarIndex && p.BarIndex <= segEnd.BarIndex)
            .ToList();

        if (innerPoints.All(p => p.BarIndex != segStart.BarIndex))
            innerPoints.Insert(0, segStart);

        if (innerPoints.All(p => p.BarIndex != segEnd.BarIndex))
            innerPoints.Add(segEnd);

        innerPoints = ExtremumFinderBase.EndFixCorridors(innerPoints, BarProvider);
        innerPoints = ExtremumFinderBase.RefineToCorridors(innerPoints, BarProvider);

        List<ExactParsedNode> parsed = Markup.Parse(innerPoints);

        ExactParsedNode best = parsed.Count > 0
            ? parsed.OrderByDescending(a => a.Score).First()
            : null;

        // Store/update the markup for this segment
        m_SegmentMarkups[segStart.BarIndex] = best;

        if (best == null)
            return;

        DrawMarkupNode(best, segPrefix, MAIN_NOTATION_LEVEL);
    }

    private void RemoveObjectsByPrefix(string prefix)
    {
        var toRemove = Chart.Objects
            .Where(o => o.Name.StartsWith(prefix))
            .Select(o => o.Name)
            .ToList();
        foreach (string name in toRemove)
            Chart.RemoveObject(name);
    }

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
