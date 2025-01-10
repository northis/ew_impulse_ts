using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
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
        m_ExtremumFinder = new ExtremumFinder(2,
            m_BarProvider, new BarProvidersFactory(Symbol, MarketData, new CTraderViewManager(this)));
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

        const int baseColorValue = 50;
        const int maxColorValue = 255;
        //Debugger.Launch();

        //List<double> devs = new List<double>();
        //double fullZigzagLength = Math.Abs(m_CurrentExtremum.Value - e.EventExtremum.Value);

        //bool isUp = e.EventExtremum > m_CurrentExtremum;
        //double dx = fullZigzagLength / (e.EventExtremum.BarIndex - m_CurrentExtremum.BarIndex);
        //for (int i = m_CurrentExtremum.BarIndex; i <= e.EventExtremum.BarIndex; i++)
        //{
        //    int count = i - m_CurrentExtremum.BarIndex;
        //    Candle candle = Candle.FromIndex(m_BarProvider, i);
        //    double midPoint = candle.L + candle.Length / 2;

        //    double currDx = count * dx;
        //    var part = Math.Abs((isUp
        //        ? m_CurrentExtremum.Value + currDx
        //        : m_CurrentExtremum.Value - currDx) - midPoint) / fullZigzagLength;
        //    devs.Add(part);
        //}

        //double sqrtDev = Math.Sqrt(devs.Select(a => a * a).Average());
        //if (sqrtDev > 1)
        //    sqrtDev = 1;

        //int resultColorAdd = baseColorValue + Convert.ToInt32(sqrtDev * 500);
        //if (resultColorAdd > maxColorValue) resultColorAdd = maxColorValue;
        int resultColorAdd = 200;

        Color color = Color.FromArgb(resultColorAdd, resultColorAdd, resultColorAdd);

        string id = $"{barIndex}{e.EventExtremum.Value}";
        Chart.DrawTrendLine($"ES{id}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            color, 5);
        //Chart.DrawText($"T{id}", Convert.ToInt32(sqrtDev * 100).ToString(), e.EventExtremum.OpenTime,
        //    e.EventExtremum.Value, color);
        m_CurrentExtremum = e.EventExtremum;
    }

    //[Output("ExactZigzag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
    //public IndicatorDataSeries Value { get; set; }

    private ExtremumFinder m_ExtremumFinder;

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(index);
    }
}