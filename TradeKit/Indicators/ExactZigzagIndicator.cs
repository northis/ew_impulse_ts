using System;
using cAlgo.API;
using TradeKit.AlgoBase;
using TradeKit.Core;

namespace TradeKit.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
public class ExactZigzagIndicator : Indicator
{
    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_ExtremumFinder = new ExactExtremumFinder(new CTraderBarsProvider(Bars, Symbol));
        m_ExtremumFinder.OnMoveExtremum += OnMoveExtremum;
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnMoveExtremum -= OnMoveExtremum;
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        base.OnDestroy();
    }

    private void OnSetExtremum(object sender, ExtremumFinderBase.BarPointEventArgs e)
    {
        int? barIndex = m_ExtremumFinder.Extremum?.BarIndex;
        if (!barIndex.HasValue)
            return;

        Value[barIndex.Value] = m_ExtremumFinder.Extremum.Value;
    }

    private void OnMoveExtremum(object sender, ExtremumFinderBase.BarPointEventArgs e)
    {
        int? barIndex = m_ExtremumFinder.Extremum?.BarIndex;
        if (!barIndex.HasValue)
            return;

        Value[barIndex.Value] = double.NaN;
    }

    [Output("ExactZigzag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
    public IndicatorDataSeries Value { get; set; }

    private ExactExtremumFinder m_ExtremumFinder;

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(index);
    }
}