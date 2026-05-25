using cAlgo.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class IterativeElliottWaveExactIndicator : ElliottWaveIndicatorBase
{
    [Parameter(nameof(BarsCount), DefaultValue = 100, MinValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
    public int BarsCount { get; set; }

    [Parameter("Markup depth", DefaultValue = ElliottWaveExactMarkup.MAX_MARKUP_DEPTH, MinValue = 1, MaxValue = 5, Group = Helper.TRADE_SETTINGS_NAME)]
    public int MarkupDepth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether candle information should be saved to file.
    /// </summary>
    [Parameter("Save candles", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveCandles { get; set; }

    /// <summary>
    /// Gets or sets the date range for saving candles to a .csv file.
    /// </summary>
    [Parameter("Dates to save", DefaultValue = Helper.DATE_COLLECTION_PATTERN, Group = Helper.DEV_SETTINGS_NAME)]
    public string DateRangeToCollect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the candles of the currently identified markup
    /// range should be saved to a .csv file whenever a best result is found.
    /// </summary>
    [Parameter("Save markup candles", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveMarkupCandles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether PNG charts for all markup variants should be
    /// saved once, immediately after the first successful parse.
    /// </summary>
    [Parameter("Save markup charts", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
    public bool SaveMarkupCharts { get; set; }

    /// <summary>
    /// Gets or sets the path to a JSON markup file to display instead of auto-markup.
    /// If set and the file can be parsed, the indicator shows the loaded markup.
    /// </summary>
    [Parameter("Markup file path", DefaultValue = "", Group = Helper.DEV_SETTINGS_NAME)]
    public string MarkupFilePath { get; set; }

    private bool m_CandlesSaved;
    private bool m_MarkupCandlesSaved;
    private bool m_MarkupChartsSaved;
    private ExactParsedNode m_CachedBest;
    private bool m_FileMarkupDrawn;

    protected override void Initialize()
    {
        BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        Markup = new ElliottWaveExactMarkup(BarProvider, MarkupDepth);
    }

    public override void Calculate(int index)
    {
        if (SaveCandles && !m_CandlesSaved)
        {
            string savedFilePath = BarProvider.SaveCandlesForDateRange(DateRangeToCollect);
            if (!string.IsNullOrEmpty(savedFilePath))
            {
                m_CandlesSaved = true;
                Print($"Candles saved to: {savedFilePath}");
            }
        }

        if (!IsLastBar)
            return;

        // If a markup file path is specified, try to load and display it.
        if (!string.IsNullOrWhiteSpace(MarkupFilePath) && !m_FileMarkupDrawn)
        {
            ExactParsedNode fileNode = TryLoadMarkupFromFile(MarkupFilePath);
            if (fileNode != null)
            {
                m_FileMarkupDrawn = true;
                m_CachedBest = fileNode;
                Chart.RemoveAllObjects();
                DrawMarkupNode(fileNode, "EW_", MAIN_NOTATION_LEVEL);
                return;
            }

            Print($"Failed to load markup from: {MarkupFilePath}, falling back to auto-markup");
        }

        if (!string.IsNullOrWhiteSpace(MarkupFilePath) && m_FileMarkupDrawn)
            return;

        // Skip full recalculation while the price stays within the already-marked range.
        if (m_CachedBest != null && index <= m_CachedBest.EndPoint.BarIndex)
            return;

        int startBarIndex = Math.Max(0, index - BarsCount + 1);

        double maxValue = double.MinValue;
        double minValue = double.MaxValue;
        int maxBarIndex = startBarIndex;
        int minBarIndex = startBarIndex;

        for (int i = startBarIndex; i <= index; i++)
        {
            double high = Bars.HighPrices[i];
            double low = Bars.LowPrices[i];

            if (high > maxValue)
            {
                maxValue = high;
                maxBarIndex = i;
            }

            if (low < minValue)
            {
                minValue = low;
                minBarIndex = i;
            }
        }

        if (maxBarIndex == minBarIndex)
            return;

        // farther = older (smaller bar index), closer = newer (larger bar index)
        int fartherBarIndex = Math.Min(maxBarIndex, minBarIndex);
        int closerBarIndex = Math.Max(maxBarIndex, minBarIndex);

        double startValue = fartherBarIndex == maxBarIndex ? maxValue : minValue;
        double endValue = closerBarIndex == maxBarIndex ? maxValue : minValue;

        BarPoint startPoint = new BarPoint(startValue, fartherBarIndex, BarProvider);
        BarPoint endPoint = new BarPoint(endValue, closerBarIndex, BarProvider);

        bool isUp = endPoint.Value > startPoint.Value;


        var optimizer = new DeviationOptimizer(BarProvider, startPoint.BarIndex, endPoint.BarIndex, false);
        double optimalDev = optimizer.FindOptimalDeviation();
        SimpleExtremumFinder innerFinder = new SimpleExtremumFinder(optimalDev, BarProvider, !isUp);
        innerFinder.Calculate(startPoint.BarIndex, endPoint.BarIndex);

        List<BarPoint> innerPoints = innerFinder.ToExtremaList()
            .Where(p => p.BarIndex >= startPoint.BarIndex && p.BarIndex <= endPoint.BarIndex)
            .ToList();

        if (innerPoints.All(p => p.BarIndex != startPoint.BarIndex))
            innerPoints.Insert(0, startPoint);

        if (innerPoints.All(p => p.BarIndex != endPoint.BarIndex))
            innerPoints.Add(endPoint);

        // Ensure each zigzag segment's start price is the actual OHLC corridor extremum
        // so that no candle inside a segment breaches the start-price boundary.
        innerPoints = ExtremumFinderBase.EndFixCorridors(innerPoints, BarProvider);
        innerPoints = ExtremumFinderBase.RefineToCorridors(innerPoints, BarProvider);

        List<ExactParsedNode> parsed = Markup.Parse(innerPoints);

        if (SaveMarkupCharts && !m_MarkupChartsSaved && parsed.Count > 0)
        {
            m_MarkupChartsSaved = true;
            IBarsProvider provider = BarProvider;
            byte level = MAIN_NOTATION_LEVEL;

            ThreadPool.QueueUserWorkItem(markups =>
            {
                ExactParsedNode[] markupArray = (ExactParsedNode[])markups;
                if (markupArray != null)
                {
                    List<string> savedPaths = markupArray.SaveMarkupResults(provider, level);
                    foreach (string path in savedPaths)
                        Print($"Markup saved: {path}");
                }
            }, parsed.ToArray());
        }

        // Sort by Score only: ParseInternal already boosts scores via the depth-coverage
        // bonus (fraction of identified sub-waves), so Score captures both Fibonacci fit
        // and sub-wave depth quality.  A secondary GetDepth() sort was previously used but
        // it systematically demoted corrective patterns (triangles, flats) whose sub-waves
        // are harder to identify, even when their Fibonacci structure was excellent.
        ExactParsedNode best = parsed.Count > 0
            ? parsed
                .OrderByDescending(a => a.Score)
                .First()
            : null;

        if (SaveMarkupCandles && !m_MarkupCandlesSaved)
        {
            DateTime markupStart =
                BarProvider.GetOpenTime(best == null ? startPoint.BarIndex : best.StartPoint.BarIndex);
            DateTime markupEnd = BarProvider.GetOpenTime(best == null ? endPoint.BarIndex : best.EndPoint.BarIndex);
            if (markupStart != default && markupEnd != default)
            {
                string markupFileName = string.Format(
                    Helper.CANDLE_FILE_NAME_FORMAT,
                    BarProvider.BarSymbol.Name,
                    BarProvider.TimeFrame.ShortName,
                    markupStart.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"),
                    markupEnd.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"));
                string markupFilePath = Path.Combine(Helper.DirectoryToSaveResults, markupFileName);
                BarProvider.SaveCandles(markupStart, markupEnd, markupFilePath);
                m_MarkupCandlesSaved = true;
                Print($"Markup candles saved to: {markupFilePath}");
            }
        }

        if (best == null)
            return;

        m_CachedBest = best;

        Chart.RemoveAllObjects();

        // Draw best result with model-type colours and stacked wave labels
        DrawMarkupNode(best, "EW_", MAIN_NOTATION_LEVEL);

        // Use new prediction system for projections
        var prediction = Markup.ParsePredictive(innerPoints, index);
        if (prediction?.Projections == null || prediction.Projections.Count <= 0)
            return;

        int lastIndex = best.EndPoint.BarIndex;
        double lastValue = best.EndPoint.Value;
        foreach (var proj in prediction.Projections)
        {
            string pName = $"PROJ_{lastIndex}_{proj.BarIndex}_{proj.WaveName}";

            Chart.DrawTrendLine(pName + "_line", lastIndex, lastValue, proj.BarIndex, proj.Price, Color.Gray, 1, LineStyle.LinesDots);

            double pyOffset = lastValue < proj.Price ? Symbol.PipSize * 2 : -Symbol.PipSize * 2;
            Chart.DrawText(pName, $"({proj.WaveName})?", proj.BarIndex, proj.Price + pyOffset, Color.Gray);

            lastIndex = proj.BarIndex;
            lastValue = proj.Price;
        }
    }
}
