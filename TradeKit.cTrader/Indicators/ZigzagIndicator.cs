using System;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Indicators;
using TradeKit.Core.ML;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Simple zigzag indicator based on pivot points without determining which came first on a candle - the high or the low.
/// Displays the predicted Elliott wave pattern type for each zigzag segment.
/// </summary>
//[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
public class ZigzagIndicator : Indicator
{
    /// <summary>
    /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
    /// </summary>
    protected override void Initialize()
    {
        m_BarProvider = new CTraderBarsProvider(Bars, Symbol.ToISymbol());
        m_BarProvidersFactory = new BarProvidersFactory(Symbol, MarketData, new CTraderViewManager(this));
        m_Classifier = new OnnxModelClassifier();
        m_ExtremumFinder = new SimplePivotExtremumFinder(Period, m_BarProvider);
        m_ExtremumFinder.OnSetExtremum += OnSetExtremum;
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        m_ExtremumFinder.OnSetExtremum -= OnSetExtremum;
        m_Classifier?.Dispose();
        base.OnDestroy();
    }

    /// <summary>
    /// Gets or sets the zigzag period.
    /// </summary>
    [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
    public int Period { get; set; }

    private BarPoint m_CurrentExtremum;
    private IBarsProvider m_BarProvider;
    private IBarProvidersFactory m_BarProvidersFactory;
    private OnnxModelClassifier m_Classifier;
    private SimplePivotExtremumFinder m_ExtremumFinder;

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
        ElliottModelType? prediction = PredictModelType(m_CurrentExtremum, e.EventExtremum);

        Color color = Color.FromArgb(colorRatioValue, colorRatioValue, colorRatioValue);

        string id = $"{barIndex}{e.EventExtremum.Value}";
        Chart.DrawTrendLine($"ES{id}",
            m_CurrentExtremum.BarIndex, m_CurrentExtremum.Value, e.EventExtremum.BarIndex, e.EventExtremum.Value,
            color, 3);

        if (prediction.HasValue)
        {
            string report = prediction.Value.Format();
            var text = Chart.DrawText($"T{id}", report,
                e.EventExtremum.OpenTime,
                e.EventExtremum.Value, color);
            if (e.EventExtremum > m_CurrentExtremum)
            {
                text.VerticalAlignment = VerticalAlignment.Top;
            }
        }

        m_CurrentExtremum = e.EventExtremum;
    }

    /// <summary>
    /// Predicts the Elliott wave model type for the segment between two extrema.
    /// Falls back to a lower timeframe if the current one has insufficient bars.
    /// </summary>
    /// <param name="start">The start extremum.</param>
    /// <param name="end">The end extremum.</param>
    /// <returns>The predicted model type or null if prediction is not possible.</returns>
    private ElliottModelType? PredictModelType(BarPoint start, BarPoint end)
    {
        ElliottModelType? result = m_Classifier.Predict(start, end, m_BarProvider);
        if (result.HasValue)
            return result;

        ITimeFrame currentTf = m_BarProvider.TimeFrame;
        TimeFrameInfo tfInfo = TimeFrameHelper.GetTimeFrameInfo(currentTf);
        ITimeFrame prevTf = tfInfo.PrevTimeFrame;

        if (prevTf.Name == currentTf.Name)
            return null;

        IBarsProvider lowerProvider = m_BarProvidersFactory.GetBarsProvider(prevTf);
        lowerProvider.LoadBars(start.OpenTime);

        int lowerStartIdx = lowerProvider.GetIndexByTime(start.OpenTime);
        int lowerEndIdx = lowerProvider.GetIndexByTime(end.OpenTime);
        if (lowerStartIdx < 0 || lowerEndIdx < 0)
            return null;

        BarPoint lowerStart = new BarPoint(start.Value, start.OpenTime, prevTf, lowerStartIdx);
        BarPoint lowerEnd = new BarPoint(end.Value, end.OpenTime, prevTf, lowerEndIdx);

        return m_Classifier.Predict(lowerStart, lowerEnd, lowerProvider);
    }

    /// <inheritdoc />
    public override void Calculate(int index)
    {
        m_ExtremumFinder.Calculate(Bars.OpenTimes[index]);
    }
}
