using System;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Zigzag indicator that evaluates whether each segment is an impulse 
/// using the IterativeZigzagImpulseClassifier.
/// </summary>
[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeImpulseZigzagIndicator : Indicator
{
    /// <summary>
    /// Gets or sets the zigzag deviation percent threshold.
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
            // The previous segment's endpoint may have moved via MoveExtremum after it was first
            // drawn; redraw it now that segStart is confirmed as its final endpoint.
            DrawSegment(extremaValues[count - 3], segStart);
        }

        DrawSegment(segStart, segEnd);
    }

    private void DrawSegment(BarPoint segStart, BarPoint segEnd)
    {
        //bool isImpulse = IterativeZigzagImpulseClassifier.IsImpulse(segStart, segEnd, m_BarProvider, DeviationPercent);
        ImpulseResult stat = MovementStatistic.GetMovementStatistic(segStart, segEnd, m_BarProvider);
        int area = stat.Area.ToPercent();
        int h = stat.HeterogeneityMax.ToPercent();
        MovementStatistic.GetDeviationScore(segStart, segEnd, m_BarProvider, out double maxDev, out double avgDev);
        int maxDistance = maxDev.ToPercent();
        int avgDistance = avgDev.ToPercent();

        double rz = MovementStatistic.GetRatioZigZag(segStart, segEnd, m_BarProvider);
        int rzValue = rz.ToPercent();

        //if (area > 35)
        //{
        //    return;
        //}


        int monocolor = Convert.ToInt32(255 - rz * 200);
        var alfa = 200;
        Color color = Color.FromArgb(alfa, monocolor, monocolor, monocolor);

        string lineId = $"IIZ_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value, segEnd.BarIndex, segEnd.Value,
            color, 2);

        string textId = $"T_{segStart.BarIndex}";
        ChartText text = Chart.DrawText(textId, $"{rzValue}/{area}/{maxDistance}/{avgDistance}/{h}", segEnd.OpenTime,
            segEnd.Value, color);
        if (segEnd > segStart)
            text.VerticalAlignment = VerticalAlignment.Top;

        //if (isImpulse)
        //{
        //}
        //else
        //{
        //    Chart.RemoveObject(textId);
        //}
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
