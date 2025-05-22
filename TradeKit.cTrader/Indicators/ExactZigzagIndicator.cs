using System;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
public class ExactZigzagIndicator : Indicator
{
    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        var factory = new BarProvidersFactory(Symbol, MarketData, new CTraderViewManager(this));
        m_ExtremumFinder = new PivotExtremumFinder(Period, m_BarProvider, factory);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        base.OnDestroy();
    }

    /// <summary>
    /// Gets or sets the zigzag period.
    /// </summary>
    [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
    public int Period { get; set; }

    private BarPoint m_CurrentExtremum;
    private IBarsProvider m_BarProvider;

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

        const int colorRatioValue = 240;
        ImpulseResult movementStatistic = MovementStatistic.GetMovementStatistic(
            m_CurrentExtremum, e.EventExtremum, m_BarProvider);

        Color color = Color.FromArgb(colorRatioValue, colorRatioValue, colorRatioValue);

        string id = $"{barIndex}{e.EventExtremum.Value}";
        Chart.DrawTrendLine($"ES{id}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            color, 3);

        string[] statItems = movementStatistic.ToString()
            .Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
        
        string report = string.Join(Environment.NewLine, statItems);
        var text = Chart.DrawText($"T{id}", report,
            e.EventExtremum.OpenTime,
            e.EventExtremum.Value, color);
        if (e.EventExtremum > m_CurrentExtremum)
        {
            text.VerticalAlignment = VerticalAlignment.Top;
        }

        m_CurrentExtremum = e.EventExtremum;
    }

    //[Output("ExactZigzag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
    //public IndicatorDataSeries Value { get; set; }

    private PivotExtremumFinder m_ExtremumFinder;

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}