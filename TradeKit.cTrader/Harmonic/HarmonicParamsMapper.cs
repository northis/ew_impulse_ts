using System;
using System.Collections.Generic;
using TradeKit.Core.Harmonic;

namespace TradeKit.CTrader.Harmonic
{
    /// <summary>
    /// Maps the cTrader input parameters onto <see cref="HarmonicParams"/>. Shared by the
    /// harmonic indicator and robot so the calculation logic is never duplicated here.
    /// </summary>
    public static class HarmonicParamsMapper
    {
        /// <summary>The cTrader settings group for the harmonic model flags.</summary>
        public const string MODELS_GROUP = "Models";

        /// <summary>The cTrader settings group for the score weights.</summary>
        public const string SCORE_GROUP = "Score";

        /// <summary>
        /// Builds the algorithm settings from the flat cTrader inputs.
        /// </summary>
        public static HarmonicParams Create(HarmonicInputs inputs)
        {
            var patterns = new SortedSet<HarmonicPatternType>();
            if (inputs.UseGartley) patterns.Add(HarmonicPatternType.GARTLEY);
            if (inputs.UseBat) patterns.Add(HarmonicPatternType.BAT);
            if (inputs.UseButterfly) patterns.Add(HarmonicPatternType.BUTTERFLY);
            if (inputs.UseCrab) patterns.Add(HarmonicPatternType.CRAB);
            if (inputs.UseShark) patterns.Add(HarmonicPatternType.SHARK);
            if (inputs.UseCypher) patterns.Add(HarmonicPatternType.CYPHER);

            return new HarmonicParams
            {
                Patterns = patterns,
                BarsDepth = inputs.BarDepthCount,
                MinPivotPeriod = inputs.MinPivotPeriod,
                MaxPivotPeriod = Math.Max(inputs.MinPivotPeriod, inputs.MaxPivotPeriod),
                DConfirmationBars = inputs.DConfirmationBars,
                FibErrorPercent = inputs.FibErrorPercent,
                LegAsymmetryPercent = inputs.LegAsymmetryPercent,
                MinimumScore = inputs.MinimumScore,
                FibErrorWeight = inputs.FibErrorWeight,
                PrzWeight = inputs.PrzWeight,
                DConfluenceWeight = inputs.DConfluenceWeight,
                TakeProfitTarget = inputs.UseSecondTarget
                    ? HarmonicTakeProfitTarget.TAKE_PROFIT_2
                    : HarmonicTakeProfitTarget.TAKE_PROFIT_1,
                StopMode = inputs.StopMode,
                StopPercent = inputs.StopPercent,
                MinimumRiskReward = inputs.MinimumRiskReward,
                MinPatternBars = inputs.MinPatternSizeBars,
                FilterByDivergence = inputs.UseDivergences,
                FilterByTrend = inputs.UseTrendOnly,
                FilterByPriceAction = inputs.UseCandlePatterns,
                FilterByRsi = inputs.UseRsi,
                BreakevenRatio = inputs.BreakEvenRatio > 0 ? inputs.BreakEvenRatio : null
            };
        }
    }

    /// <summary>
    /// A flat carrier of the cTrader harmonic inputs.
    /// </summary>
    public class HarmonicInputs
    {
        /// <summary>Search for Gartley patterns.</summary>
        public bool UseGartley { get; set; } = true;

        /// <summary>Search for Bat patterns.</summary>
        public bool UseBat { get; set; } = true;

        /// <summary>Search for Butterfly patterns.</summary>
        public bool UseButterfly { get; set; } = true;

        /// <summary>Search for Crab patterns.</summary>
        public bool UseCrab { get; set; } = true;

        /// <summary>Search for Shark patterns.</summary>
        public bool UseShark { get; set; } = true;

        /// <summary>Search for Cypher patterns.</summary>
        public bool UseCypher { get; set; } = true;

        /// <summary>How many bars back the search may look.</summary>
        public int BarDepthCount { get; set; } = 500;

        /// <summary>The smallest pivot period.</summary>
        public int MinPivotPeriod { get; set; } = 3;

        /// <summary>The largest pivot period.</summary>
        public int MaxPivotPeriod { get; set; } = 20;

        /// <summary>Trailing bars required to confirm the point D.</summary>
        public int DConfirmationBars { get; set; } = 1;

        /// <summary>The allowed Fibonacci ratio error, in percent.</summary>
        public double FibErrorPercent { get; set; } = 15d;

        /// <summary>The allowed leg duration asymmetry, in percent.</summary>
        public double LegAsymmetryPercent { get; set; } = 250d;

        /// <summary>The minimum total score, from 0 to 1.</summary>
        public double MinimumScore { get; set; }

        /// <summary>The weight of the Fibonacci ratio error.</summary>
        public double FibErrorWeight { get; set; } = 4d;

        /// <summary>The weight of the PRZ level confluence.</summary>
        public double PrzWeight { get; set; } = 2d;

        /// <summary>The weight of the point D / PRZ confluence.</summary>
        public double DConfluenceWeight { get; set; } = 3d;

        /// <summary>Use the second Fibonacci target as the working take profit.</summary>
        public bool UseSecondTarget { get; set; }

        /// <summary>The stop loss mode.</summary>
        public HarmonicStopMode StopMode { get; set; } = HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY;

        /// <summary>The stop loss percent.</summary>
        public double StopPercent { get; set; } = 75d;

        /// <summary>The minimum risk/reward ratio.</summary>
        public double MinimumRiskReward { get; set; }

        /// <summary>The minimum X-to-D duration, in bars.</summary>
        public int MinPatternSizeBars { get; set; }

        /// <summary>Use only the patterns confirmed by a divergence.</summary>
        public bool UseDivergences { get; set; }

        /// <summary>Use only the patterns aligned with the trend.</summary>
        public bool UseTrendOnly { get; set; }

        /// <summary>Use only the patterns confirmed by a candle pattern.</summary>
        public bool UseCandlePatterns { get; set; }

        /// <summary>Use only the patterns with an overbought/oversold RSI.</summary>
        public bool UseRsi { get; set; }

        /// <summary>The breakeven level between 0 (entry) and 1 (take profit). 0 disables it.</summary>
        public double BreakEvenRatio { get; set; }
    }
}
