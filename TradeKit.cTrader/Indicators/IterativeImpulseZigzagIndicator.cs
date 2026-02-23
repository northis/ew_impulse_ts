using cAlgo.API;
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
    /// Gets or sets the zigzag period.
    /// </summary>
    [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
    public int Period { get; set; }

    private IBarsProvider m_BarProvider;
    private SimpleExtremumFinder m_ExtremumFinder;

    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_ExtremumFinder = new SimpleExtremumFinder(Period, m_BarProvider);
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
        bool isImpulse = IterativeZigzagImpulseClassifier.IsImpulse(segStart, segEnd, m_BarProvider, Period);
        Color color = isImpulse ? Color.LimeGreen : Color.Gray;

        string lineId = $"IIZ_{segStart.BarIndex}";
        Chart.DrawTrendLine(lineId,
            segStart.BarIndex, segStart.Value, segEnd.BarIndex, segEnd.Value,
            color, isImpulse ? 3 : 1);

        string textId = $"T_{segStart.BarIndex}";
        if (isImpulse)
        {
            ChartText text = Chart.DrawText(textId, "Impulse", segEnd.OpenTime, segEnd.Value, color);
            if (segEnd > segStart)
                text.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            Chart.RemoveObject(textId);
        }
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
