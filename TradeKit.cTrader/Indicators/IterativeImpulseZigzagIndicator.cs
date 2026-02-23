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

    private BarPoint m_CurrentExtremum;
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
        int? barIndex = m_ExtremumFinder.Extremum?.BarIndex;
        if (!barIndex.HasValue)
            return;

        if (m_CurrentExtremum == null)
        {
            m_CurrentExtremum = e.EventExtremum;
            return;
        }

        bool isImpulse = IterativeZigzagImpulseClassifier.IsImpulse(m_CurrentExtremum, e.EventExtremum, m_BarProvider, Period);
        
        Color color = isImpulse ? Color.LimeGreen : Color.Gray;

        string id = $"{barIndex}{e.EventExtremum.Value}";
        Chart.DrawTrendLine($"IIZ_{id}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            color, isImpulse ? 3 : 1);

        if (isImpulse)
        {
            ChartText text = Chart.DrawText($"T_{id}", "Impulse",
                e.EventExtremum.OpenTime,
                e.EventExtremum.Value, color);
            
            if (e.EventExtremum > m_CurrentExtremum)
            {
                text.VerticalAlignment = VerticalAlignment.Top;
            }
        }

        m_CurrentExtremum = e.EventExtremum;
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
