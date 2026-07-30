namespace TradeKit.Core.Harmonic;

/// <summary>
/// Search and setup settings of <see cref="HarmonicSetupFinder"/>.
/// <para>
/// The search defaults reproduce the reference Pine indicator, but the trading ones do not:
/// <see cref="TargetAnchor"/>, <see cref="TakeProfit1Override"/>, <see cref="StopMode"/>,
/// <see cref="StopPercent"/> and <see cref="MinimumStopAtr"/> carry the only combination the
/// archive sweep found profitable after costs - a full pattern height target with a stop just
/// beyond the point D, traded only when that stop is wide enough to survive the noise. The
/// combination was picked on the first half of every file and holds on the second one. Reset
/// those five to compare a run with Pine.
/// </para>
/// </summary>
public class HarmonicParams
{
    /// <summary>
    /// How many bars back the search may look. 500 matches <c>max_bars_back</c> of the Pine indicator.
    /// </summary>
    public int BarsDepth { get; set; } = 500;

    /// <summary>
    /// The smallest pivot period used to detect the X/A/B/C points.
    /// </summary>
    public int MinPivotPeriod { get; set; } = 3;

    /// <summary>
    /// The largest pivot period used to detect the X/A/B/C points.
    /// </summary>
    public int MaxPivotPeriod { get; set; } = 20;

    /// <summary>
    /// The number of trailing bars required to confirm the point D.
    /// <para>
    /// 0 means no waiting: the point D is taken on the bar that made the extremum and the setup
    /// enters at the close of that same bar. The point is then only known to be the lowest low
    /// (or the highest high) since the point C and over <see cref="MinBarsBeforePivot"/>
    /// preceding bars - a later bar may still go deeper, so an immediate entry trades a better
    /// fill for more false positives.
    /// </para>
    /// </summary>
    public int DConfirmationBars { get; set; } = 1;

    /// <summary>
    /// The number of bars before the point D that must not break it.
    /// </summary>
    public int MinBarsBeforePivot { get; set; } = 3;

    /// <summary>
    /// The allowed Fibonacci ratio error, in percent.
    /// </summary>
    public double FibErrorPercent { get; set; } = 15d;

    /// <summary>
    /// The allowed leg duration asymmetry, in percent.
    /// </summary>
    public double LegAsymmetryPercent { get; set; } = 250d;

    /// <summary>
    /// Validate the leg duration asymmetry when the point D is confirmed.
    /// <para>
    /// The reference Pine indicator effectively performs no symmetry check at all: it passes the
    /// still empty D bar index into the test, so every comparison degrades to <c>na</c> and the
    /// test always succeeds. Set this to <c>false</c> to reproduce that behaviour when comparing
    /// against a Pine reference export.
    /// </para>
    /// </summary>
    public bool CheckLegSymmetry { get; set; } = true;

    /// <summary>
    /// Drop an XABC candidate whose incomplete score reaches this value once the price enters
    /// its Potential Reversal Zone, before the point D is confirmed.
    /// <para>
    /// This reproduces the after-C entry of the reference Pine indicator, which consumes such a
    /// candidate and completes it at the entry price instead of waiting for a real pivot D. The
    /// first TradeKit version has no after-C entry, so the filter is disabled by default and is
    /// only switched on when comparing against a Pine reference export.
    /// </para>
    /// </summary>
    public double? AfterCEntryScore { get; set; }

    /// <summary>
    /// The minimum total score a pattern must reach to produce a setup, from 0 to 1.
    /// </summary>
    public double MinimumScore { get; set; }

    /// <summary>
    /// The weight of the average Fibonacci ratio error in the total score.
    /// </summary>
    public double FibErrorWeight { get; set; } = 4d;

    /// <summary>
    /// The weight of the PRZ level confluence in the total score.
    /// </summary>
    public double PrzWeight { get; set; } = 2d;

    /// <summary>
    /// The weight of the point D / PRZ confluence in the total score.
    /// </summary>
    public double DConfluenceWeight { get; set; } = 3d;

    /// <summary>
    /// The models to search for.
    /// </summary>
    public ISet<HarmonicPatternType> Patterns { get; set; } =
        new SortedSet<HarmonicPatternType>(HarmonicPatternDefinition.All.Select(a => a.PatternType));

    /// <summary>
    /// Search for bullish patterns.
    /// </summary>
    public bool UseBullish { get; set; } = true;

    /// <summary>
    /// Search for bearish patterns.
    /// </summary>
    public bool UseBearish { get; set; } = true;

    /// <summary>
    /// Per-model overrides of the first Fibonacci target.
    /// </summary>
    public IDictionary<HarmonicPatternType, HarmonicTarget> TakeProfit1Overrides { get; } =
        new Dictionary<HarmonicPatternType, HarmonicTarget>();

    /// <summary>
    /// Per-model overrides of the second Fibonacci target.
    /// </summary>
    public IDictionary<HarmonicPatternType, HarmonicTarget> TakeProfit2Overrides { get; } =
        new Dictionary<HarmonicPatternType, HarmonicTarget>();

    /// <summary>
    /// A single first target used by every model, or <c>null</c> to keep the model defaults.
    /// <para>
    /// The default is the full height of the pattern projected from the entry. The archive
    /// sweep is unambiguous about the distance: the closer targets win more often but lose
    /// the little they win to the cost of a round trip, and the whole far end of the grid is
    /// better than the whole near end, out of sample as well.
    /// <see cref="TakeProfit1Overrides"/> still wins for the models listed there.
    /// </para>
    /// </summary>
    public HarmonicTarget TakeProfit1Override { get; set; } =
        new(HarmonicTargetBasis.PATTERN_HEIGHT, 1d);

    /// <summary>
    /// A single second target used by every model, or <c>null</c> to keep the model defaults.
    /// </summary>
    public HarmonicTarget TakeProfit2Override { get; set; }

    /// <summary>
    /// Which of the two Fibonacci targets becomes the working take profit of the setup.
    /// </summary>
    public HarmonicTakeProfitTarget TakeProfitTarget { get; set; } = HarmonicTakeProfitTarget.TAKE_PROFIT_1;

    /// <summary>
    /// The price the relative targets are projected from.
    /// <para>
    /// <see cref="HarmonicTargetAnchor.POINT_D"/> reproduces the reference Pine indicator;
    /// <see cref="HarmonicTargetAnchor.ENTRY"/> projects the very same ratio from the real
    /// entry price, so the declared distance is the one actually traded. The entry happens a
    /// few bars after the point D and is always the worse price of the two, so anchoring at
    /// the point D quietly shortens every target.
    /// </para>
    /// </summary>
    public HarmonicTargetAnchor TargetAnchor { get; set; } = HarmonicTargetAnchor.ENTRY;

    /// <summary>
    /// The stop loss mode.
    /// </summary>
    public HarmonicStopMode StopMode { get; set; } = HarmonicStopMode.PATTERN_PERCENT_BEYOND_D;

    /// <summary>
    /// The stop loss percent used by <see cref="StopMode"/>.
    /// <para>
    /// The default places the stop 5% of the pattern height beyond the point D. The archive
    /// sweep keeps the whole 1-5% band positive out of sample and degrades from 7.5% on: a
    /// wider stop buys a slightly better win rate for a much worse reward/risk.
    /// </para>
    /// </summary>
    public double StopPercent { get; set; } = 5d;

    /// <summary>
    /// The minimum risk/reward ratio of a setup. 0 disables the filter.
    /// </summary>
    public double MinimumRiskReward { get; set; }

    /// <summary>
    /// The minimum stop loss distance, in average true ranges of the entry bar.
    /// 0 disables the filter.
    /// <para>
    /// A stop placed inside the noise of its own market is taken out by that noise alone, and
    /// the fixed cost of a round trip eats a bigger share of a shorter stop. On the archive
    /// the whole grid of targets and stops is negative without this filter and turns positive
    /// around four average true ranges; the threshold was chosen on the first half of every
    /// file and holds on the second one, so the filter is on by default. Being measured in
    /// ATRs, it depends neither on the instrument nor on the broker.
    /// </para>
    /// </summary>
    public double MinimumStopAtr { get; set; } = 4d;

    /// <summary>
    /// The period of the average true range used by <see cref="MinimumStopAtr"/>.
    /// </summary>
    public int StopAtrPeriod { get; set; } = 14;

    /// <summary>
    /// The minimum X-to-D duration of a pattern, in bars.
    /// </summary>
    public int MinPatternBars { get; set; }

    /// <summary>
    /// The maximum X-to-D duration of a pattern, in bars.
    /// </summary>
    public int MaxPatternBars { get; set; } = int.MaxValue;

    /// <summary>
    /// Use only the patterns positioned against an overbought/oversold RSI reading.
    /// </summary>
    public bool FilterByRsi { get; set; }

    /// <summary>
    /// The RSI period used by <see cref="FilterByRsi"/>.
    /// </summary>
    public int RsiPeriod { get; set; } = 14;

    /// <summary>
    /// Use only the patterns confirmed by an oscillator divergence.
    /// </summary>
    public bool FilterByDivergence { get; set; }

    /// <summary>
    /// Use only the patterns aligned with the trend.
    /// </summary>
    public bool FilterByTrend { get; set; }

    /// <summary>
    /// Use only the patterns confirmed by a price action candle pattern.
    /// </summary>
    public bool FilterByPriceAction { get; set; }

    /// <summary>
    /// A value between 0 (entry) and 1 (take profit) that defines the breakeven level,
    /// or <c>null</c> when the breakeven should not be used.
    /// </summary>
    public double? BreakevenRatio { get; set; }

    /// <summary>
    /// Gets the first Fibonacci target of the model.
    /// </summary>
    /// <param name="patternType">The model.</param>
    public HarmonicTarget GetTakeProfit1(HarmonicPatternType patternType)
    {
        if (TakeProfit1Overrides.TryGetValue(patternType, out HarmonicTarget target))
            return target;

        return TakeProfit1Override ?? HarmonicTarget.DefaultTakeProfit1[patternType];
    }

    /// <summary>
    /// Gets the second Fibonacci target of the model.
    /// </summary>
    /// <param name="patternType">The model.</param>
    public HarmonicTarget GetTakeProfit2(HarmonicPatternType patternType)
    {
        if (TakeProfit2Overrides.TryGetValue(patternType, out HarmonicTarget target))
            return target;

        return TakeProfit2Override ?? HarmonicTarget.DefaultTakeProfit2[patternType];
    }
}
