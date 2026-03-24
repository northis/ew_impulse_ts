using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeElliottWaveExactIndicator : Indicator
{
    [Parameter(nameof(DeviationPercent), DefaultValue = 0.1, MinValue = 0.01, Group = Helper.TRADE_SETTINGS_NAME)]
    public double DeviationPercent { get; set; }

    [Parameter(nameof(MaxDepth), DefaultValue = 2, MinValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MaxDepth { get; set; }

    private IBarsProvider m_BarProvider;
    private SimpleExtremumFinder m_ExtremumFinder;
    private ElliottWaveExactMarkup m_Markup;

    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_ExtremumFinder = new SimpleExtremumFinder(DeviationPercent, m_BarProvider);
        m_Markup = new ElliottWaveExactMarkup();
    }

    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);

        if (!IsLastBar)
        {
            return;
        }

        List<BarPoint> allPoints = m_ExtremumFinder.Extrema.Values.ToList();
        if (allPoints.Count < 2)
        {
            return;
        }

        int startIndex = System.Math.Max(0, allPoints.Count - MaxDepth - 1);
        List<BarPoint> mainPoints = allPoints.Skip(startIndex).ToList();

        for (int i = 0; i < mainPoints.Count - 1; i++)
        {
            BarPoint start = mainPoints[i];
            BarPoint end = mainPoints[i + 1];

            bool isUp = end.Value > start.Value;
            SimpleExtremumFinder innerFinder = new SimpleExtremumFinder(0.01, m_BarProvider, !isUp);
            innerFinder.Calculate(start.BarIndex, end.BarIndex);
                
            List<BarPoint> innerPoints = innerFinder.ToExtremaList()
                .Where(p => p.BarIndex >= start.BarIndex && p.BarIndex <= end.BarIndex)
                .ToList();
                
            if (innerPoints.All(p => p.BarIndex != start.BarIndex))
            {
                innerPoints.Insert(0, start);
            }

            if (innerPoints.All(p => p.BarIndex != end.BarIndex))
            {
                innerPoints.Add(end);
            }

            List<ExactParsedNode> parsed = m_Markup.Parse(innerPoints);
            ExactParsedNode best = parsed.FirstOrDefault();

            if (best == null)
            {
                continue;
            }

            List<MarkupResult> flat = best.ToMarkupResult().Flatten().ToList();
            foreach (MarkupResult res in flat)
            {
                if (string.IsNullOrEmpty(res.NodeName))
                {
                    continue;
                }

                string name = $"EW_{res.Start.BarIndex}_{res.End.BarIndex}_{res.Level}_{res.NodeName}";
                double yOffset = res.IsUp ? Symbol.PipSize * 10 : -Symbol.PipSize * 10;
                        
                Chart.DrawText(name, res.NodeName, res.End.BarIndex, res.End.Value + yOffset, Color.Yellow);
                Chart.DrawTrendLine(name + "_line", res.Start.BarIndex, res.Start.Value, res.End.BarIndex, res.End.Value, Color.Yellow, 1, LineStyle.Lines);
            }

            var projections = m_Markup.GetProjections(best);
            if (projections.Count <= 0)
            {
                continue;
            }

            int lastIndex = best.EndPoint.BarIndex;
            double lastValue = best.EndPoint.Value;
            foreach (var proj in projections)
            {
                string pName = $"PROJ_{lastIndex}_{proj.BarIndex}_{proj.Name}";
                            
                Chart.DrawTrendLine(pName + "_line", lastIndex, lastValue, proj.BarIndex, proj.Value, Color.Gray, 1, LineStyle.LinesDots);
                            
                double pyOffset = lastValue < proj.Value ? Symbol.PipSize * 10 : -Symbol.PipSize * 10;
                Chart.DrawText(pName, $"({proj.Name})", proj.BarIndex, proj.Value + pyOffset, Color.Gray);

                lastIndex = proj.BarIndex;
                lastValue = proj.Value;
            }
        }
    }
}
