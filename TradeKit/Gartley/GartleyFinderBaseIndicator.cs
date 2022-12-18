using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    public class GartleyFinderBaseIndicator : Indicator
    {
        private GartleySetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private bool m_IsInitialized;
        private Color m_SlColor;
        private Color m_TpColor;
        private Color m_BearColorFill;
        private Color m_BullColorFill;
        private Color m_BearColorBorder;
        private Color m_BullColorBorder;
        private const int SETUP_WIDTH = 3;
        private const int LINE_WIDTH = 1;

        /// <summary>
        /// Gets or sets the value how deep should we analyze the candles.
        /// </summary>
        [Parameter(nameof(BarDepthCount), DefaultValue = Helper.GARTLEY_BARS_COUNT)]
        public int BarDepthCount { get; set; }

        /// <summary>
        /// Gets or sets the percent of the allowance for the relations calculation.
        /// </summary>
        [Parameter(nameof(BarAllowancePercent), DefaultValue = Helper.GARTLEY_CANDLE_ALLOWANCE_PERCENT, MinValue = 1, MaxValue = 50)]
        public int BarAllowancePercent { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.GARTLEY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseGartley), DefaultValue = true)]
        public bool UseGartley { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.BUTTERFLY"/> pattern.
        /// </summary>
        [Parameter(nameof(UseButterfly), DefaultValue = false)]
        public bool UseButterfly { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.SHARK"/> pattern.
        /// </summary>
        [Parameter(nameof(UseShark), DefaultValue = false)]
        public bool UseShark { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCrab), DefaultValue = false)]
        public bool UseCrab { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseBat), DefaultValue = false)]
        public bool UseBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.ALT_BAT"/> pattern.
        /// </summary>
        [Parameter(nameof(UseAltBat), DefaultValue = false)]
        public bool UseAltBat { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.CYPHER"/> pattern.
        /// </summary>
        [Parameter(nameof(UseCypher), DefaultValue = false)]
        public bool UseCypher { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether We should use <see cref="GartleyPatternType.DEEP_CRAB"/> pattern.
        /// </summary>
        [Parameter(nameof(UseDeepCrab), DefaultValue = false)]
        public bool UseDeepCrab { get; set; }

        private HashSet<GartleyPatternType> GetPatternsType()
        {
            var res = new HashSet<GartleyPatternType>();
            if (UseGartley)
                res.Add(GartleyPatternType.GARTLEY);
            if (UseButterfly)
                res.Add(GartleyPatternType.BUTTERFLY);
            if (UseShark)
                res.Add(GartleyPatternType.SHARK);
            if (UseCrab)
                res.Add(GartleyPatternType.CRAB);
            if (UseBat)
                res.Add(GartleyPatternType.BAT);
            if (UseAltBat)
                res.Add(GartleyPatternType.ALT_BAT);
            if (UseCypher)
                res.Add(GartleyPatternType.CYPHER);
            if (UseDeepCrab)
                res.Add(GartleyPatternType.DEEP_CRAB);

            return res;
        }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            Logger.SetWrite(a => Print(a));
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }

            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            m_BearColorFill = Color.FromHex("#50F08080");
            m_BullColorFill = Color.FromHex("#5090EE90");
            m_BearColorBorder = Color.FromHex("#F0F08080");
            m_BullColorBorder = Color.FromHex("#F090EE90");

            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();
            m_SetupFinder = new GartleySetupFinder(
                m_BarsProvider, Symbol, BarAllowancePercent, BarDepthCount, patternTypes);
            m_SetupFinder.OnEnter += OnEnter;
            m_SetupFinder.OnStopLoss += OnStopLoss;
            m_SetupFinder.OnTakeProfit += OnTakeProfit;
        }

        protected override void OnDestroy()
        {
            m_SetupFinder.OnEnter -= OnEnter;
            m_SetupFinder.OnStopLoss -= OnStopLoss;
            m_SetupFinder.OnTakeProfit -= OnTakeProfit;
            base.OnDestroy();
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        private void OnEnter(object sender, EventArgs.GartleySignalEventArgs e)
        {
            if (!e.Level.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            int indexX = e.GartleyItem.ItemX.Index.GetValueOrDefault();
            int indexA = e.GartleyItem.ItemA.Index.GetValueOrDefault();
            int indexB = e.GartleyItem.ItemB.Index.GetValueOrDefault();
            int indexC = e.GartleyItem.ItemC.Index.GetValueOrDefault();
            int indexD = e.GartleyItem.ItemD.Index.GetValueOrDefault();
            if (indexX == 0 || indexA == 0 || indexB == 0 || indexC == 0 || indexD == 0)
                return;

            string name = $"{levelIndex}{e.GartleyItem.PatternType}";
            double valueX = e.GartleyItem.ItemX.Price;
            double valueA = e.GartleyItem.ItemA.Price;
            double valueB = e.GartleyItem.ItemB.Price;
            double valueC = e.GartleyItem.ItemC.Price;
            double valueD = e.GartleyItem.ItemD.Price;

            bool isBull = valueX < valueA;
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;
            
            ChartTriangle p1 = 
            Chart.DrawTriangle($"P1{name}", indexX, valueX, indexA, valueA, indexB, valueB, colorFill, 0);
            p1.IsFilled = true;

            ChartTriangle p2 = 
            Chart.DrawTriangle($"P2{name}", indexB, valueB, indexC, valueC, indexD, valueD, colorFill, 0);
            p2.IsFilled = true;

            Chart.DrawTrendLine($"XD{name}", indexX, valueX, indexD, valueD, colorBorder, LINE_WIDTH)
                .TextForLine(Chart, $"{e.GartleyItem.PatternType}{Environment.NewLine}{e.GartleyItem.XtoDActual.Ratio()} ({e.GartleyItem.XtoD.Ratio()})",
                    !isBull, indexX, indexD);

            Chart.DrawText($"XText{name}", "X", indexX, valueX, colorBorder)
                .ChartTextAlign(!isBull);
            Chart.DrawText($"AText{name}", "A", indexA, valueA, colorBorder)
                .ChartTextAlign(isBull);
            Chart.DrawText($"BText{name}", "B", indexB, valueB, colorBorder)
                .ChartTextAlign(!isBull);
            Chart.DrawText($"CText{name}", "C", indexC, valueC, colorBorder)
                .ChartTextAlign(isBull);
            Chart.DrawText($"DText{name}", "D", indexD, valueD, colorBorder)
                .ChartTextAlign(!isBull);

            string xbLevel = e.GartleyItem.XtoB > 0
                ? $" ({e.GartleyItem.XtoB.Ratio()})"
                : string.Empty;

            Chart.DrawTrendLine($"XB{name}", indexX, valueX, indexB, valueB, colorBorder, LINE_WIDTH)
                .TextForLine(Chart, $"{e.GartleyItem.XtoBActual.Ratio()}{xbLevel}", !true, indexX, indexB);

            Chart.DrawTrendLine($"BD{name}", indexB, valueB, indexD, valueD, colorBorder, LINE_WIDTH)
                .TextForLine(Chart, $"{e.GartleyItem.BtoDActual.Ratio()} ({e.GartleyItem.BtoD.Ratio()})",
                    true, indexB, indexD);

            Chart.DrawTrendLine($"AC{name}", indexA, valueA, indexC, valueC, colorBorder, LINE_WIDTH)
                .TextForLine(Chart, $"{e.GartleyItem.AtoCActual.Ratio()} ({e.GartleyItem.AtoC.Ratio()})",
                    isBull, indexA, indexC);
            
            //double closeD = m_BarsProvider.GetClosePrice(indexD);
            
            ChartRectangle slRect =
                Chart.DrawRectangle($"SL{name}", indexD, valueD, indexD + SETUP_WIDTH,
                    e.GartleyItem.StopLoss, m_SlColor, 1);
            slRect.IsFilled = true;
            ChartRectangle tpRect =
                Chart.DrawRectangle($"TP{name}", indexD, valueD, indexD + SETUP_WIDTH,
                    e.GartleyItem.TakeProfit1, m_TpColor, 1);
            tpRect.IsFilled = true;

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            m_SetupFinder.CheckBar(index);
            if (IsLastBar && !m_IsInitialized)
            {
                m_IsInitialized = true;
                Logger.Write($"History ok, index {index}");
            }
        }
    }
}
