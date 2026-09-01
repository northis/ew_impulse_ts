using System;
using System.Globalization;
using System.IO;
using System.Linq;
using cAlgo.API;
using Newtonsoft.Json;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Json;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Diagonal
{
    /// <summary>
    /// Indicator that finds contracting diagonals (<see cref="DiagonalSetupFinder"/>) and
    /// marks the counter-move setup that follows them (see DIAGONAL.md).
    /// </summary>
    /// <seealso cref="Indicator" />
    public class DiagonalFinderBaseIndicator
        : BaseIndicator<DiagonalSetupFinder, ElliottWaveSignalEventArgs>
    {
        private DiagonalSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private Color m_SlColor;
        private Color m_TpColor;
        private int m_MarkupSavedCount;

        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnBreakeven(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            string action = e.MoveStopToEntry
                ? e.CloseHalf ? "breakeven + close half" : "breakeven"
                : "close half";
            Logger.Write(
                $"The fresh 23.6% level is reached, {action}. Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnManualClose(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"The setup is closed early. Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnEnter(object sender, ElliottWaveSignalEventArgs e)
        {
            BarPoint[] wp = e.WavePoints;
            Logger.Write($"Diagonal setup found! {e.Level.OpenTime:s}");
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);

            if (wp.Length < 1)
                return;

            BarPoint current = wp[0];
            foreach (BarPoint wave in wp.Skip(1))
            {
                Chart.DrawTrendLine($"Dg{levelIndex}+{wave.OpenTime}",
                    current.OpenTime, current.Value, wave.OpenTime, wave.Value, Color.MediumPurple);
                current = wave;
            }

            // The wedge itself: the 1-3 and 2-4 trendlines (DIAGONAL.md §4.2).
            if (wp.Length >= 5)
            {
                Chart.DrawTrendLine($"DgU{levelIndex}", wp[1].OpenTime, wp[1].Value,
                    wp[3].OpenTime, wp[3].Value, Color.Goldenrod, LINE_WIDTH, LineStyle.Lines);
                Chart.DrawTrendLine($"DgL{levelIndex}", wp[2].OpenTime, wp[2].Value,
                    wp[4].OpenTime, wp[4].Value, Color.Goldenrod, LINE_WIDTH, LineStyle.Lines);
            }

            double levelValue = e.Level.Value;

            Chart.DrawRectangle($"DgSL{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                    e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"DgTP{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                    e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                .SetFilled();

            SaveDiagonalMarkup(wp, e);
            DrawDiagonalParams(levelIndex, wp, e);
        }

        /// <summary>
        /// Draws a compact block with the parameters that characterise the found diagonal
        /// itself (contractions, retrace/penetration levels, convergence, durations —
        /// DIAGONAL.md §4) next to the end of wave 5: above the extreme for a bullish
        /// diagonal, below it for a bearish one. The trade levels (TP/SL/R:R) are shown
        /// on a separate, differently coloured last line.
        /// </summary>
        private void DrawDiagonalParams(int levelIndex, BarPoint[] wp, ElliottWaveSignalEventArgs e)
        {
            if (!ShowDiagonalParams || wp.Length < 6)
                return;

            bool isUp = wp[5].Value > wp[0].Value;
            double s = isUp ? 1 : -1;
            double V(int i) => s * wp[i].Value;

            double w1 = V(1) - V(0);
            double w2 = V(1) - V(2);
            double w3 = V(3) - V(2);
            double w4 = V(3) - V(4);
            double w5 = V(5) - V(4);
            if (w1 <= 0 || w2 <= 0 || w3 <= 0 || w4 <= 0 || w5 <= 0)
                return;

            double b1 = Math.Max(1, wp[1].BarIndex - wp[0].BarIndex);
            double b2 = Math.Max(1, wp[2].BarIndex - wp[1].BarIndex);
            double b3 = Math.Max(1, wp[3].BarIndex - wp[2].BarIndex);
            double b4 = Math.Max(1, wp[4].BarIndex - wp[3].BarIndex);

            // D-CONVERGE measure (DIAGONAL.md §4.2): how many times narrower the wedge
            // is at point 4 than at point 1, in v-space.
            double slope13 = (V(3) - V(1)) / (wp[3].BarIndex - wp[1].BarIndex);
            double slope24 = (V(4) - V(2)) / (wp[4].BarIndex - wp[2].BarIndex);
            double width1 = V(1) - (V(2) + slope24 * (wp[1].BarIndex - wp[2].BarIndex));
            double width4 = (V(1) + slope13 * (wp[4].BarIndex - wp[1].BarIndex)) - V(4);
            string conv = width4 > 0 && width1 > 0
                ? ((width1 / width4) - 1).ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture)
                : "—";

            string f(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
            string pc(double x) => (100 * x).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            string F(double x) => x.ToString($"F{Symbol.Digits}", CultureInfo.InvariantCulture);

            double rr = Math.Abs(e.TakeProfit.Value - e.Level.Value) /
                        Math.Max(1e-12, Math.Abs(e.StopLoss.Value - e.Level.Value));

            string[] lines =
            {
                $"DIAGONAL {(isUp ? "↑" : "↓")}",
                $"W3/W1 {f(w3 / w1)}   W4/W2 {f(w4 / w2)}   W5/W3 {f(w5 / w3)}",
                $"W2ret {pc(w2 / w1)}   W3pen {pc((w3 - w2) / w1)}   W4/W3 {pc(w4 / w3)}",
                $"W4→W2 {pc((V(1) - V(4)) / w2)}   conv {conv}",
                $"t3/t1 {f(b3 / b1)}   t4/t2 {f(b4 / b2)}",
                $"TP {F(e.TakeProfit.Value)}   SL {F(e.StopLoss.Value)}   R:R {f(rr)}",
            };

            // Readable on both dark and light themes.
            Color statsColor = Color.FromHex("#00BFFF");
            Color levelsColor = Color.Goldenrod;

            int bar = Bars.OpenTimes.GetIndexByTime(wp[5].OpenTime) + 1;
            double step = Math.Max(Symbol.PipSize * 12, Math.Abs(wp[5].Value - wp[0].Value) * 0.06);
            int n = lines.Length;

            for (int i = 0; i < n; i++)
            {
                double price = isUp
                    ? wp[5].Value + step * (n - i)
                    : wp[5].Value - step * (i + 1);
                ChartText txt = Chart.DrawText($"DgP{levelIndex}_{i}", lines[i],
                    bar, price, i == n - 1 ? levelsColor : statsColor);
                txt.HorizontalAlignment = HorizontalAlignment.Left;
                txt.VerticalAlignment = isUp ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            }
        }

        /// <summary>
        /// Saves the found diagonal as a JSON markup file when <see cref="MarkupSavePath"/>
        /// is set. Empty path (the default) — nothing is saved.
        /// </summary>
        private void SaveDiagonalMarkup(BarPoint[] wavePoints, ElliottWaveSignalEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MarkupSavePath))
                return;

            try
            {
                Directory.CreateDirectory(MarkupSavePath);

                JsonDiagonalMarkup markup = JsonDiagonalMarkup.FromSignal(
                    Symbol.Name, m_BarsProvider.TimeFrame.ShortName,
                    wavePoints, e.Level, e.TakeProfit, e.StopLoss);

                if (markup == null)
                    return;

                string fileName = string.Format(CultureInfo.InvariantCulture,
                    "{0}_{1}_Diagonal_{2}_{3:yyyyMMdd-HHmmss}.json",
                    Symbol.Name,
                    m_BarsProvider.TimeFrame.ShortName,
                    ++m_MarkupSavedCount,
                    wavePoints[0].OpenTime);

                string filePath = Path.Combine(MarkupSavePath, fileName);
                System.IO.File.WriteAllText(filePath,
                    JsonConvert.SerializeObject(markup, Formatting.Indented));
                Print($"Diagonal markup saved: {filePath}");
            }
            catch (Exception ex)
            {
                Print($"Failed to save diagonal markup: {ex.Message}");
            }
        }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");

            var cTraderViewManager = new CTraderViewManager(this);
            var barProvidersFactory = new BarProvidersFactory(
                Symbol, MarketData, cTraderViewManager);
            m_BarsProvider = barProvidersFactory.GetBarsProvider(TimeFrame.ToITimeFrame());

            m_SetupFinder = new DiagonalSetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), GetEWParams(),
                TakeProfitRatio, RequireWave5Ratio, RequireWave4Ratio, RequireInitialDiagonal,
                TakeProfitAtRetrace
                    ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                    : DiagonalTakeProfitMode.RISK_RATIO,
                MinConvergence, MaxConvergence, RequireInsideWedge, MaxSpillAreaRatio,
                MinWave3Penetration, MaxWaveDurationRatio, retraceAction: RetraceAction,
                minRiskRewardRatio: MinRiskRewardRatio,
                wave3RetraceRatio: Wave3RetraceRatio,
                minWave4Wave2Level: MinWave4Wave2Level,
                requireWave4Shorter: RequireWave4Shorter,
                requireWave2Shorter: RequireWave2Shorter,
                minWave2Retrace: MinWave2Retrace,
                maxWave5SpillRatio: MaxWave5SpillRatio,
                minWave4Wave2DurationRatio: MinWave4Wave2DurationRatio,
                minWave3Wave1DurationRatio: MinWave3Wave1DurationRatio);
            Subscribe(m_SetupFinder);
            m_SetupFinder.MarkAsInitialized();
        }

        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected EWParams GetEWParams()
        {
            return new EWParams(Period, MinSizePercent, BarsCount);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the minimum size of the diagonal in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = 0.3, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the zigzag period. 0 means auto.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = 0, MinValue = 0, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets the take-profit as a multiple of the risk (DIAGONAL.md §6).
        /// </summary>
        [Parameter("TP ratio (R:R)", DefaultValue = 1.0, MinValue = 0.2, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double TakeProfitRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the target is a 23.6% retracement of the
        /// whole diagonal instead of a fixed R:R (DIAGONAL.md §6.3).
        /// </summary>
        [Parameter("TP at 23.6% of the diagonal", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool TakeProfitAtRetrace { get; set; }

        /// <summary>
        /// Gets or sets what is logged when the recomputed 23.6% retrace level of the diagonal
        /// is reached while the trade is in profit (DIAGONAL.md §6.4).
        /// </summary>
        [Parameter("Action on the fresh 23.6%", DefaultValue = DiagonalRetraceAction.NONE, Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalRetraceAction RetraceAction { get; set; }

        /// <summary>
        /// Gets or sets the minimum R:R of a 23.6%-retrace setup: a worse one waits for wave 5
        /// to improve it instead of being taken or dropped. 0 turns the wait off
        /// (DIAGONAL.md §6.5).
        /// </summary>
        [Parameter("Min R:R (retrace TP)", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinRiskRewardRatio { get; set; }

        /// <summary>
        /// Gets or sets the target as a retrace of |W3| from the extreme of wave 5
        /// (DIAGONAL.md §6.6): 0.382 — TP at the 38.2% level, 0 — off.
        /// </summary>
        [Parameter("TP at % of W3", DefaultValue = 0.0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double Wave3RetraceRatio { get; set; }

        /// <summary>
        /// Gets or sets the level of wave 2's range wave 4 has to reach (D-W4-24, DIAGONAL.md
        /// §4): 0 is the end of wave 1, 1 is the end of wave 2.
        /// </summary>
        [Parameter("Min W4 level in W2", DefaultValue = 0.236, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave4Wave2Level { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 4 must last fewer bars than wave 2
        /// (D-TIME-24, DIAGONAL.md §4).
        /// </summary>
        [Parameter("W4 shorter than W2", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave4Shorter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 2 must last fewer bars than wave 1
        /// (D-TIME-12, DIAGONAL.md §4). Off by default — the rule is optional.
        /// </summary>
        [Parameter("W2 shorter than W1", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave2Shorter { get; set; }

        /// <summary>
        /// Gets or sets the minimum retracement of wave 1 by wave 2 as a share of |W1|
        /// (D-W2-RET, DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W2 retrace of W1", DefaultValue = 0.0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave2Retrace { get; set; }

        /// <summary>
        /// Gets or sets the tolerated spill area over the span of wave 5 (D-INSIDE-5,
        /// DIAGONAL.md §4.3). 0 — off.
        /// </summary>
        [Parameter("Max W5 spill", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave5SpillRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration ratio bars(W4)/bars(W2) (D-TIME-24-MIN,
        /// DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W4/W2 duration", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave4Wave2DurationRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration ratio bars(W3)/bars(W1) (D-TIME-31-MIN,
        /// DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W3/W1 duration", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave3Wave1DurationRatio { get; set; }

        /// <summary>
        /// Gets or sets how hard the trendlines 1-3 and 2-4 must converge: 0 — parallel,
        /// +1 — the wedge is twice as narrow at point 4, −1 — the filter is off
        /// (DIAGONAL.md §4.2).
        /// </summary>
        [Parameter("Min convergence", DefaultValue = 0.0, MinValue = -1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinConvergence { get; set; }

        /// <summary>
        /// Gets or sets the maximum allowed convergence of the trendlines 1-3 and 2-4:
        /// 0 — the cap is off, +1 — the wedge may be at most twice as narrow at point 4
        /// (DIAGONAL.md §4.2).
        /// </summary>
        [Parameter("Max convergence", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxConvergence { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the bars of waves 2-4 must stay inside
        /// the trendlines (DIAGONAL.md §4.3).
        /// </summary>
        [Parameter("Bars inside the wedge", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInsideWedge { get; set; }

        /// <summary>
        /// Gets or sets the tolerated spill area as a share of the wedge area.
        /// </summary>
        [Parameter("Max spill area", DefaultValue = 0.005, MinValue = 0.0001, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxSpillAreaRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum break of wave 1 by wave 3 as a share of |W1| (D-W3-PEN).
        /// </summary>
        [Parameter("Min W3 penetration", DefaultValue = 0.03, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave3Penetration { get; set; }

        /// <summary>
        /// Gets or sets the D-TIME bound on the duration ratio of same-character waves
        /// (W3 vs W1, W4 vs W2).
        /// </summary>
        [Parameter("Max wave duration ratio", DefaultValue = 8.0, MinValue = 1, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWaveDurationRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 5 must be "mature" on the signal:
        /// |W5| ≥ 0.786·|W3| (DIAGONAL.md §6.1).
        /// </summary>
        [Parameter("W5 >= 78.6% of W3", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave5Ratio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the wedge must contract evenly:
        /// |W4| ≥ 0.786·|W2| (DIAGONAL.md §4.1).
        /// </summary>
        [Parameter("W4 >= 78.6% of W2", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave4Ratio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the whole diagonal, up to the signal bar,
        /// must stay inside the preceding counter-move (DIAGONAL.md §5.2).
        /// </summary>
        [Parameter("Initial diagonal", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInitialDiagonal { get; set; }

        /// <summary>
        /// Gets or sets the folder where JSON markup files of the diagonals found on the
        /// available candles are saved. Empty (the default) — nothing is saved.
        /// </summary>
        [Parameter("Save markup path", DefaultValue = "", Group = Helper.DEV_SETTINGS_NAME)]
        public string MarkupSavePath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the parameters of a found diagonal
        /// (wave ratios, retrace levels, convergence, durations) are drawn next to it.
        /// </summary>
        [Parameter("Show diagonal params", DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowDiagonalParams { get; set; }

        #endregion
    }
}
