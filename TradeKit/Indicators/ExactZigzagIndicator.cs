using cAlgo.API;
using System;
using TradeKit.AlgoBase;
using TradeKit.Core;
using static Plotly.NET.StyleParam;

namespace TradeKit.Indicators;

//[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
public class ExactZigzagIndicator : Indicator
{
    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_ExtremumFinder = new ExactExtremumFinder(new CTraderBarsProvider(Bars, Symbol),
            new BarProvidersFactory(Symbol, MarketData));
        m_ExtremumFinder.OnMoveExtremum += OnMoveExtremum;
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnMoveExtremum -= OnMoveExtremum;
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        base.OnDestroy();
    }

    private BarPoint m_CurrentExtremum;
    private ChartTrendLine m_ChartTrendLine;

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

        m_ChartTrendLine = Chart.DrawTrendLine($"ES{barIndex}{e.EventExtremum.Value}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            Color.Purple, 1);
        m_CurrentExtremum = e.EventExtremum;
    }

    private void OnMoveExtremum(object sender, ExtremumFinderBase.BarPointEventArgs e)
    {
        int? barIndex = m_ExtremumFinder.Extremum?.BarIndex;
        if (!barIndex.HasValue)
            return;

        m_ChartTrendLine = Chart.DrawTrendLine($"EM{barIndex}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value, Color.LightBlue);
        m_CurrentExtremum = e.EventExtremum;
        //Value[barIndex.Value] = double.NaN;
    }

    //[Output("ExactZigzag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
    //public IndicatorDataSeries Value { get; set; }

    private ExactExtremumFinder m_ExtremumFinder;

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(index);
    }
}