using System;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
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
        m_ExtremumFinder = new ExtremumFinder(2, m_BarProvider, factory);
        m_ElliottWavePatternFinder = new ElliottWavePatternFinder(TimeFrame.ToITimeFrame(), factory);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        base.OnDestroy();
    }

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

        const int baseColorValue = 10;
        const int maxColorValue = 255;
        const int colorRatioValue = 240;
        //Debugger.Launch();
        if (!m_ElliottWavePatternFinder.IsSmoothImpulse(
                m_CurrentExtremum, e.EventExtremum, out double smoothDegree))
        {
            m_CurrentExtremum = e.EventExtremum;
            return;
        }

        int resultColorAdd = baseColorValue + Convert.ToInt32(smoothDegree * colorRatioValue);
        if (resultColorAdd > maxColorValue) resultColorAdd = maxColorValue;

        Color color = Color.FromArgb(resultColorAdd, resultColorAdd, resultColorAdd);

        string id = $"{barIndex}{e.EventExtremum.Value}";
        Chart.DrawTrendLine($"ES{id}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            color, 3);
        Chart.DrawText($"T{id}", Convert.ToInt32(smoothDegree * 100).ToString(), e.EventExtremum.OpenTime,
            e.EventExtremum.Value, color);
        m_CurrentExtremum = e.EventExtremum;
    }

    //[Output("ExactZigzag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
    //public IndicatorDataSeries Value { get; set; }

    private ExtremumFinder m_ExtremumFinder;
    private ElliottWavePatternFinder m_ElliottWavePatternFinder;

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(index);
    }
}