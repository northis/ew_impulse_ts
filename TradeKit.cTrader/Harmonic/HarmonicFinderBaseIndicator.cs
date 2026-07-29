using System;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Harmonic;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Harmonic
{
    /// <summary>
    /// Indicator that finds harmonic XABCD setups and draws the figure, the PRZ and the
    /// entry/TP/SL levels.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class HarmonicFinderBaseIndicator :
        BaseIndicator<HarmonicSetupFinder, HarmonicSignalEventArgs>
    {
        private HarmonicSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private Color m_SlColor;
        private Color m_TpColor;
        private Color m_TpLineColor;
        private Color m_PrzColor;
        private Color m_BearColorFill;
        private Color m_BullColorFill;
        private Color m_BearColorBorder;
        private Color m_BullColorBorder;

        #region Input parameters

        /// <summary>
        /// Gets or sets a value indicating whether the ratio values should be shown.
        /// </summary>
        [Parameter("Show ratio values", DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Potential Reversal Zone should be drawn.
        /// </summary>
        [Parameter("Show PRZ", DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowPrz { get; set; }

        /// <summary>Gets or sets a value indicating whether Gartley patterns are searched.</summary>
        [Parameter("Gartley", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseGartley { get; set; }

        /// <summary>Gets or sets a value indicating whether Bat patterns are searched.</summary>
        [Parameter("Bat", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseBat { get; set; }

        /// <summary>Gets or sets a value indicating whether Butterfly patterns are searched.</summary>
        [Parameter("Butterfly", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseButterfly { get; set; }

        /// <summary>Gets or sets a value indicating whether Crab patterns are searched.</summary>
        [Parameter("Crab", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseCrab { get; set; }

        /// <summary>Gets or sets a value indicating whether Shark patterns are searched.</summary>
        [Parameter("Shark", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseShark { get; set; }

        /// <summary>Gets or sets a value indicating whether Cypher patterns are searched.</summary>
        [Parameter("Cypher", DefaultValue = true, Group = HarmonicParamsMapper.MODELS_GROUP)]
        public bool UseCypher { get; set; }

        /// <summary>Gets or sets how many bars back the search may look.</summary>
        [Parameter("Bar depth count", DefaultValue = 500, MinValue = 50, MaxValue = 5000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarDepthCount { get; set; }

        /// <summary>Gets or sets the smallest pivot period.</summary>
        [Parameter("Min pivot period", DefaultValue = 3, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MinPivotPeriod { get; set; }

        /// <summary>Gets or sets the largest pivot period.</summary>
        [Parameter("Max pivot period", DefaultValue = 20, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MaxPivotPeriod { get; set; }

        /// <summary>Gets or sets the trailing bars required to confirm the point D.</summary>
        [Parameter("Point D confirmation bars", DefaultValue = 1, MinValue = 0, MaxValue = 20, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public int DConfirmationBars { get; set; }

        /// <summary>Gets or sets the allowed Fibonacci ratio error, in percent.</summary>
        [Parameter("Fib error %", DefaultValue = 15, MinValue = 0, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double FibErrorPercent { get; set; }

        /// <summary>Gets or sets the allowed leg duration asymmetry, in percent.</summary>
        [Parameter("Leg asymmetry %", DefaultValue = 250, MinValue = 0, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME, Step = 10)]
        public double LegAsymmetryPercent { get; set; }

        /// <summary>Gets or sets the minimum total score, from 0 to 1.</summary>
        [Parameter("Min score", DefaultValue = 0.9, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double MinimumScore { get; set; }

        /// <summary>Gets or sets the minimum risk/reward ratio.</summary>
        [Parameter("Min risk/reward", DefaultValue = 0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.1)]
        public double MinimumRiskReward { get; set; }

        /// <summary>Gets or sets the minimum stop distance, in average true ranges.</summary>
        [Parameter("Min stop, ATR", DefaultValue = HarmonicParamsMapper.DEFAULT_MIN_STOP_ATR, MinValue = 0, MaxValue = 20, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.5)]
        public double MinimumStopAtr { get; set; }

        /// <summary>Gets or sets the minimum X-to-D duration, in bars.</summary>
        [Parameter("Min pattern size, bars", DefaultValue = 0, MinValue = 0, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public int MinPatternSizeBars { get; set; }

        /// <summary>Gets or sets a value indicating whether the second target is the working TP.</summary>
        [Parameter("Use target 2", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseSecondTarget { get; set; }

        /// <summary>Gets or sets what the first target is measured against.</summary>
        [Parameter("TP1 basis", DefaultValue = HarmonicTargetMode.MODEL_DEFAULT, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetMode TakeProfit1Mode { get; set; }

        /// <summary>Gets or sets the ratio of the first target.</summary>
        [Parameter("TP1 ratio", DefaultValue = HarmonicFib.F618, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double TakeProfit1Ratio { get; set; }

        /// <summary>Gets or sets what the second target is measured against.</summary>
        [Parameter("TP2 basis", DefaultValue = HarmonicTargetMode.MODEL_DEFAULT, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetMode TakeProfit2Mode { get; set; }

        /// <summary>Gets or sets the ratio of the second target.</summary>
        [Parameter("TP2 ratio", DefaultValue = HarmonicFib.F1272, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME, Step = 0.01)]
        public double TakeProfit2Ratio { get; set; }

        /// <summary>Gets or sets the price the relative targets are projected from.</summary>
        [Parameter("TP anchor", DefaultValue = HarmonicTargetAnchor.POINT_D, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicTargetAnchor TargetAnchor { get; set; }

        /// <summary>Gets or sets the stop loss mode.</summary>
        [Parameter("Stop mode", DefaultValue = HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY, Group = Helper.TRADE_SETTINGS_NAME)]
        public HarmonicStopMode StopMode { get; set; }

        /// <summary>Gets or sets the stop loss percent.</summary>
        [Parameter("Stop %", DefaultValue = 75, MinValue = 0, MaxValue = 500, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double StopPercent { get; set; }

        /// <summary>Gets or sets the weight of the Fibonacci ratio error.</summary>
        [Parameter("Fib error weight", DefaultValue = 4, MinValue = 0, MaxValue = 10, Group = HarmonicParamsMapper.SCORE_GROUP, Step = 0.1)]
        public double FibErrorWeight { get; set; }

        /// <summary>Gets or sets the weight of the PRZ level confluence.</summary>
        [Parameter("PRZ weight", DefaultValue = 2, MinValue = 0, MaxValue = 10, Group = HarmonicParamsMapper.SCORE_GROUP, Step = 0.1)]
        public double PrzWeight { get; set; }

        /// <summary>Gets or sets the weight of the point D / PRZ confluence.</summary>
        [Parameter("Point D confluence weight", DefaultValue = 3, MinValue = 0, MaxValue = 10, Group = HarmonicParamsMapper.SCORE_GROUP, Step = 0.1)]
        public double DConfluenceWeight { get; set; }

        #endregion

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            m_TpLineColor = Color.FromHex("#A000C000");
            m_PrzColor = Color.FromHex("#40FFFF00");
            m_BearColorFill = Color.FromHex("#50F08080");
            m_BullColorFill = Color.FromHex("#5090EE90");
            m_BearColorBorder = Color.FromHex("#F0F08080");
            m_BullColorBorder = Color.FromHex("#F090EE90");

            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_SetupFinder = new HarmonicSetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), HarmonicParamsMapper.Create(GetInputs()));
            Subscribe(m_SetupFinder);
        }

        private HarmonicInputs GetInputs()
        {
            return new HarmonicInputs
            {
                UseGartley = UseGartley,
                UseBat = UseBat,
                UseButterfly = UseButterfly,
                UseCrab = UseCrab,
                UseShark = UseShark,
                UseCypher = UseCypher,
                BarDepthCount = BarDepthCount,
                MinPivotPeriod = MinPivotPeriod,
                MaxPivotPeriod = MaxPivotPeriod,
                DConfirmationBars = DConfirmationBars,
                FibErrorPercent = FibErrorPercent,
                LegAsymmetryPercent = LegAsymmetryPercent,
                MinimumScore = MinimumScore,
                FibErrorWeight = FibErrorWeight,
                PrzWeight = PrzWeight,
                DConfluenceWeight = DConfluenceWeight,
                UseSecondTarget = UseSecondTarget,
                TakeProfit1Mode = TakeProfit1Mode,
                TakeProfit1Ratio = TakeProfit1Ratio,
                TakeProfit2Mode = TakeProfit2Mode,
                TakeProfit2Ratio = TakeProfit2Ratio,
                TargetAnchor = TargetAnchor,
                StopMode = StopMode,
                StopPercent = StopPercent,
                MinimumRiskReward = MinimumRiskReward,
                MinimumStopAtr = MinimumStopAtr,
                MinPatternSizeBars = MinPatternSizeBars
            };
        }

        /// <summary>
        /// Called when stop loss occurs.
        /// </summary>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[e.Level.BarIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take profit occurs.
        /// </summary>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[e.Level.BarIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called on a new signal.
        /// </summary>
        protected override void OnEnter(object sender, HarmonicSignalEventArgs e)
        {
            HarmonicItem item = e.HarmonicItem;

            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);
            int indexX = Bars.OpenTimes.GetIndexByTime(item.ItemX.OpenTime);
            int indexA = Bars.OpenTimes.GetIndexByTime(item.ItemA.OpenTime);
            int indexB = Bars.OpenTimes.GetIndexByTime(item.ItemB.OpenTime);
            int indexC = Bars.OpenTimes.GetIndexByTime(item.ItemC.OpenTime);
            int indexD = Bars.OpenTimes.GetIndexByTime(item.ItemD.OpenTime);
            if (indexX == 0 || indexA == 0 || indexB == 0 || indexC == 0 || indexD == 0)
                return;

            bool isBull = item.IsBull;
            string name = $"{levelIndex}{item.GetHashCode()}";
            Color colorFill = isBull ? m_BullColorFill : m_BearColorFill;
            Color colorBorder = isBull ? m_BullColorBorder : m_BearColorBorder;

            double valueX = item.ItemX.Value;
            double valueA = item.ItemA.Value;
            double valueB = item.ItemB.Value;
            double valueC = item.ItemC.Value;
            double valueD = item.ItemD.Value;

            ChartTriangle xab = Chart.DrawTriangle(
                $"P1{name}", indexX, valueX, indexA, valueA, indexB, valueB, colorFill, 0);
            xab.IsFilled = true;

            ChartTriangle bcd = Chart.DrawTriangle(
                $"P2{name}", indexB, valueB, indexC, valueC, indexD, valueD, colorFill, 0);
            bcd.IsFilled = true;

            string header = $"{(isBull ? "Bullish" : "Bearish")} {item.PatternType} " +
                            $"({item.Score.Total * 100:F1})";
            Chart.DrawTrendLine($"XD{name}", indexX, valueX, indexD, valueD,
                    colorBorder, ShowRatio ? LINE_WIDTH : 0)
                .TextForLine(Chart, header, !isBull, indexX, indexD);

            if (ShowRatio)
            {
                Chart.DrawText($"XText{name}", "X", indexX, valueX, colorBorder).ChartTextAlign(!isBull);
                Chart.DrawText($"AText{name}", "A", indexA, valueA, colorBorder).ChartTextAlign(isBull);
                Chart.DrawText($"BText{name}", "B", indexB, valueB, colorBorder).ChartTextAlign(!isBull);
                Chart.DrawText($"CText{name}", "C", indexC, valueC, colorBorder).ChartTextAlign(isBull);
                Chart.DrawText($"DText{name}", "D", indexD, valueD, colorBorder).ChartTextAlign(!isBull);

                Chart.DrawTrendLine($"XB{name}", indexX, valueX, indexB, valueB, colorBorder, LINE_WIDTH)
                    .TextForLine(Chart, item.Score.AbToXaRatio.Ratio(), false, indexX, indexB);

                Chart.DrawTrendLine($"AC{name}", indexA, valueA, indexC, valueC, colorBorder, LINE_WIDTH)
                    .TextForLine(Chart, item.Score.BcToAbRatio.Ratio(), isBull, indexA, indexC);

                if (item.Score.CdToBcRatio.HasValue)
                {
                    Chart.DrawTrendLine($"BD{name}", indexB, valueB, indexD, valueD, colorBorder, LINE_WIDTH)
                        .TextForLine(Chart, item.Score.CdToBcRatio.Value.Ratio(), true, indexB, indexD);
                }
            }

            if (ShowPrz)
            {
                Chart.DrawRectangle($"PRZ{name}", indexC, item.Prz.Lower,
                        Math.Max(indexD, levelIndex) + SETUP_WIDTH, item.Prz.Upper, m_PrzColor, LINE_WIDTH)
                    .SetFilled();
            }

            if (ShowSetups)
            {
                double entry = e.Level.Value;
                int setupEnd = levelIndex + SETUP_WIDTH;

                // The risk and the working reward of the setup.
                Chart.DrawRectangle($"SL{name}", levelIndex, entry,
                        setupEnd, e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                    .SetFilled();
                Chart.DrawRectangle($"TP{name}", levelIndex, entry,
                        setupEnd, e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                    .SetFilled();

                // Both Fibonacci targets, so the one that is not traded stays visible.
                Chart.DrawTrendLine($"TP1{name}", levelIndex, item.TakeProfit1,
                    setupEnd, item.TakeProfit1, m_TpLineColor, LINE_WIDTH, LineStyle.DotsRare);
                Chart.DrawTrendLine($"TP2{name}", levelIndex, item.TakeProfit2,
                    setupEnd, item.TakeProfit2, m_TpLineColor, LINE_WIDTH, LineStyle.DotsRare);

                Chart.DrawTrendLine($"E{name}", levelIndex, entry, setupEnd, entry,
                        colorBorder, LINE_WIDTH)
                    .TextForLine(Chart,
                        $"{entry.ToString($"F{Symbol.Digits}")}  R:R {e.RiskReward:F2}",
                        isBull, levelIndex, setupEnd);
            }

            string priceFormatted = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"New harmonic setup! {item.PatternType} price:{priceFormatted} " +
                         $"R:R {e.RiskReward:F2} ({Bars[levelIndex].OpenTime:s})");
        }
    }
}
