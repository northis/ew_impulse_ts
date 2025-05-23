using System;
using System.Diagnostics;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Gartley;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Gartley
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    public class GartleyFinderBaseIndicator :
        BaseIndicator<GartleySetupFinder, GartleySignalEventArgs>
    {
        private GartleySetupFinder m_SetupFinder;
        private SupertrendFinder m_SupertrendFinder;
        private IBarsProvider m_BarsProvider;
        private Color m_SlColor;
        private Color m_TpColor;
        private Color m_BearColorFill;
        private Color m_BullColorFill;
        private Color m_BearColorBorder;
        private Color m_BullColorBorder;
        private bool m_CandlesSaved;
        private const int SETUP_WIDTH = 3;
        private const int LINE_WIDTH = 1;
        private const int DIV_LINE_WIDTH = 3;

        /// <summary>
        /// Gets or sets a value indicating whether we should show ratio values on patterns.
        /// </summary>
        [Parameter("Show ratio values", DefaultValue = false, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should show divergences with the patterns.
        /// </summary>
        [Parameter("Show divergences", DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowDivergences { get; set; }

        /// <summary>
        /// Gets or sets the value how deep we should analyze the candles.
        /// </summary>
        [Parameter("Bar depth count", DefaultValue = Helper.GARTLEY_BARS_COUNT, MinValue = 10, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }
        
        /// <summary>
        /// Gets or sets the final accuracy.
        /// </summary>
        [Parameter("Accuracy", DefaultValue = Helper.GARTLEY_ACCURACY, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double Accuracy { get; set; }

        /// <summary>
        /// Gets or sets the ratio used to calculate the take profit level in the Gartley pattern strategy.
        /// </summary>
        [Parameter("Take profit ratio", DefaultValue = Helper.GARTLEY_TP_RATIO, MinValue = 0.1, MaxValue = Helper.GARTLEY_TP2_RATIO, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double TakeProfitRatio { get; set; }

        /// <summary>
        /// Gets or sets the stop-loss ratio, which determines the proportional distance at which a stop-loss is set
        /// relative to the calculated pattern level. This value is configurable within a range and is used to
        /// manage risk during trades.
        /// </summary>
        [Parameter("Stop loss ratio", DefaultValue = Helper.GARTLEY_SL_RATIO, MinValue = 0.1, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.005)]
        public double StopLossRatio { get; set; }
        
        /// <summary>
        /// Gets or sets the pivot (zigzag) period.
        /// </summary>
        [Parameter("Pivot period", DefaultValue = Helper.GARTLEY_MIN_PERIOD, MinValue = 1, MaxValue = 230, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use divergences with the patterns.
        /// </summary>
        [Parameter("Use divergences", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseDivergences { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use candle patterns (Price Action) with the patterns.
        /// </summary>
        [Parameter("Use candle patterns", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseCandlePatterns { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should use only trend patterns.
        /// </summary>
        [Parameter("Trend only patterns", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseTrendOnly { get; set; }

        /// <summary>
        /// Gets or sets the minimum pattern size in bars.
        /// </summary>
        [Parameter(nameof(MaxPatternSizeBars), DefaultValue = 50, MinValue = 5, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MaxPatternSizeBars { get; set; }

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
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            m_BearColorFill = Color.FromHex("#50F08080");
            m_BullColorFill = Color.FromHex("#5090EE90");
            m_BearColorBorder = Color.FromHex("#F0F08080");
            m_BullColorBorder = Color.FromHex("#F090EE90");

            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_SetupFinder = new GartleySetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), Accuracy,
                BarDepthCount, ShowDivergences, UseDivergences, UseTrendOnly,
                UseCandlePatterns, MaxPatternSizeBars, TakeProfitRatio,
                StopLossRatio, null, Period);
            Subscribe(m_SetupFinder);
        }

        public override void Calculate(int index)
        {
            m_SupertrendFinder?.OnCalculate(Bars.OpenTimes[index]);
            if (SaveCandles && !m_CandlesSaved)
            {
                string savedFilePath = m_BarsProvider.SaveCandlesForDateRange(DateRangeToCollect);
                if (!string.IsNullOrEmpty(savedFilePath))
                {
                    m_CandlesSaved = true;
                    Logger.Write($"Candles saved to: {savedFilePath}");
                }
            }
            
            base.Calculate(index);
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, GartleySignalEventArgs e)
        {
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);
            int indexX = Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemX.OpenTime);
            int indexA = Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemA.OpenTime);
            int indexB = Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemB.OpenTime);
            int indexC = Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemC.OpenTime);
            int indexD =  Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemD.OpenTime);
            if (indexX == 0 || indexA == 0 || indexB == 0 || indexC == 0 || indexD == 0)
                return;
            
            string name = $"{levelIndex}{e.GartleyItem.GetHashCode()}";
            double valueX = e.GartleyItem.ItemX.Value;
            double valueA = e.GartleyItem.ItemA.Value;
            double valueB = e.GartleyItem.ItemB.Value;
            double valueC = e.GartleyItem.ItemC.Value;
            double valueD = e.GartleyItem.ItemD.Value;

            bool useE = e.GartleyItem.ItemE != null;

            int indexE = useE
                ? Bars.OpenTimes.GetIndexByTime(e.GartleyItem.ItemE.OpenTime)
                : 0;
            double valueE = useE
                ? e.GartleyItem.ItemE.Value
                : 0;

            bool isBull = valueX < valueA;
            if(useE) isBull = !isBull;
            
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;
            
            ChartTriangle p1 = 
            Chart.DrawTriangle($"P1{name}", indexX, valueX, indexA, valueA, indexB, valueB, colorFill, 0);
            p1.IsFilled = true;
            
            ChartTriangle p2 = 
                Chart.DrawTriangle($"P2{name}", indexB, valueB, indexC, valueC, indexD, valueD, colorFill, 0);
            p2.IsFilled = true;

            if (useE)
            {
                ChartTriangle p3 = 
                    Chart.DrawTriangle($"P3{name}", indexC, valueC, indexD, valueD, indexE, valueE, colorFill, 0);
                p3.IsFilled = true;
            }

            string percent = ShowRatio ? $" ({e.GartleyItem.AccuracyPercent}%)" : string.Empty;
            string header =
                $"{(isBull ? "Bullish" : "Bearish")} {e.GartleyItem.PatternType.Format()}{percent}";

            string xdRatio;
            if (ShowRatio)
            {
                string xdLevel = e.GartleyItem.XtoD > 0
                    ? $" ({e.GartleyItem.XtoD.Ratio()})"
                    : string.Empty;
                xdRatio =
                    $"{Environment.NewLine}{e.GartleyItem.XtoDActual.Ratio()}{xdLevel}";
            }
            else
            {
                xdRatio = string.Empty;
            }

            Chart.DrawTrendLine($"XD{name}", indexX, valueX, indexD, valueD,
                    colorBorder, ShowRatio ? LINE_WIDTH : 0)
                .TextForLine(Chart, $"{header}{xdRatio}", !e.GartleyItem.IsBull, indexX, indexD);

            if (ShowRatio)
            {
                string[] pNames = e.GartleyItem.PatternType.GetPointNames();
                Chart.DrawText($"XText{name}", pNames[0], indexX, valueX, colorBorder)
                    .ChartTextAlign(!e.GartleyItem.IsBull);
                Chart.DrawText($"AText{name}", pNames[1], indexA, valueA, colorBorder)
                    .ChartTextAlign(e.GartleyItem.IsBull);
                Chart.DrawText($"BText{name}", pNames[2], indexB, valueB, colorBorder)
                    .ChartTextAlign(!e.GartleyItem.IsBull);
                Chart.DrawText($"CText{name}", pNames[3], indexC, valueC, colorBorder)
                    .ChartTextAlign(e.GartleyItem.IsBull);
                Chart.DrawText($"DText{name}", pNames[4], indexD, valueD, colorBorder)
                    .ChartTextAlign(!e.GartleyItem.IsBull);

                ChartTrendLine xbLine =
                    Chart.DrawTrendLine($"XB{name}", indexX, valueX, indexB, valueB, colorBorder, LINE_WIDTH);
                string xbLevel = e.GartleyItem.XtoB > 0
                    ? $" ({e.GartleyItem.XtoB.Ratio()})"
                    : string.Empty;
                xbLine.TextForLine(Chart, $"{e.GartleyItem.XtoBActual.Ratio()}{xbLevel}", !true, indexX, indexB);

                ChartTrendLine bdLine = Chart.DrawTrendLine(
                    $"BD{name}", indexB, valueB, indexD, valueD, colorBorder, LINE_WIDTH);

                bdLine.TextForLine(Chart, $"{e.GartleyItem.BtoDActual.Ratio()} ({e.GartleyItem.BtoD.Ratio()})",
                    true, indexB, indexD);
                
                ChartTrendLine acLine =
                    Chart.DrawTrendLine($"AC{name}", indexA, valueA, indexC, valueC, colorBorder, LINE_WIDTH);

                acLine.TextForLine(Chart, $"{e.GartleyItem.AtoCActual.Ratio()} ({e.GartleyItem.AtoC.Ratio()})",
                    isBull, indexA, indexC);

                if (pNames.Length > 5 && useE)
                {
                    Chart.DrawText($"EText{name}", pNames[5], indexE, valueE,
                            colorBorder)
                        .ChartTextAlign(e.GartleyItem.IsBull);

                    ChartTrendLine ceLine =
                        Chart.DrawTrendLine($"CE{name}", indexC, valueC, indexE,
                            valueE, colorBorder, LINE_WIDTH);
                    string ceLevel = e.GartleyItem.CtoE > 0
                        ? $" ({e.GartleyItem.CtoE.Ratio()})"
                        : string.Empty;
                    ceLine.TextForLine(Chart,
                        $"{e.GartleyItem.CtoEActual.Ratio()}{ceLevel}", !true,
                        indexC, indexE);
                }
            }

            if (ShowSetups)
            {
                double levelValue = e.Level.Value;

                Chart.DrawRectangle($"SL{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP1{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP2{name}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                        e.GartleyItem.TakeProfit2, m_TpColor, LINE_WIDTH)
                    .SetFilled();
            }

            BarPoint div = e.DivergenceStart;
            if (ShowDivergences && div is not null)
            {
                Chart.DrawTrendLine($"Div{name}", div.BarIndex, div.Value,
                    useE ? indexE : indexD, useE ? valueE : valueD, colorBorder,
                    DIV_LINE_WIDTH);
            }

            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }
    }
}
