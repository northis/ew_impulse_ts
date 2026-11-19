using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// How the end of the diagonal is anchored for a continuation trade.
    /// </summary>
    public enum DiagonalContinuationVariant
    {
        /// <summary>The signal's P5 is the end; the limit is fixed there.</summary>
        SIGNAL_ANCHOR,

        /// <summary>The end is confirmed by a retrace of the running height; the limit is
        /// then placed at <c>end - kD</c> and re-anchored on a new extreme before the fill.</summary>
        CONFIRMED_END,

        /// <summary>The limit is re-priced every bar at <c>k</c> below the running extreme;
        /// the deep correction itself is the trigger. This is the primary variant.</summary>
        DYNAMIC_REPRICE,

        /// <summary>The end is confirmed by the first retrace of <c>c</c> of the running
        /// height; the entry is the confirmation bar's close (or the next open), the stop is
        /// P0, and the target is measured from the correction extreme C. This is the
        /// "enter at the first correction" variant (V4).</summary>
        CORRECTION_END
    }

    /// <summary>
    /// What the take profit is measured from.
    /// </summary>
    public enum DiagonalContinuationTargetKind
    {
        /// <summary><c>P0 + f*D</c> — the classic measured move / extension from the start.</summary>
        DIAGONAL_START,

        /// <summary><c>end + g*D</c> — a measured move from the diagonal end.</summary>
        DIAGONAL_END,

        /// <summary><c>entry + f*D</c> — a measured move from the actual entry.</summary>
        ENTRY,

        /// <summary><c>C + f*D</c> — the classic projection from the correction extreme
        /// (the V4 anchor).</summary>
        CORRECTION_END
    }

    /// <summary>
    /// Parameters of a diagonal-continuation trade (DIAGONAL.md §9.43).
    /// </summary>
    public sealed record DiagonalContinuationParams(
        DiagonalContinuationVariant Variant = DiagonalContinuationVariant.DYNAMIC_REPRICE,
        double EnterRatio = 0.5d,
        DiagonalContinuationTargetKind TargetKind = DiagonalContinuationTargetKind.DIAGONAL_START,
        double TargetRatio = 1.0d,
        double ConfirmRatio = 0.10d,
        double StopBuffer = 0d,
        bool Ladder = false,
        double LadderTp2Ratio = 1.618d,
        int MaxHoldBars = 500,
        int MaxWaitBars = 300,
        double CostRate = 0.0002d,
        bool EntryAtNextOpen = false);

    /// <summary>
    /// The diagonal context needed by the simulator: the signal bar and the 0-1-2-3-4-5
    /// points as known at the signal close.
    /// </summary>
    public sealed record DiagonalContinuationSetup(
        bool IsUp,
        int SignalBar,
        DateTime SignalTime,
        int P0Bar,
        double P0,
        int P5Bar,
        double P5);

    /// <summary>
    /// The result of one continuation attempt. <see cref="Outcome"/> is <c>TP</c>, <c>SL</c>,
    /// <c>TIME</c>, <c>CENSOR</c> or <c>NOFILL</c>.
    /// </summary>
    public sealed record DiagonalContinuationTrade(
        bool IsUp,
        int SignalBar,
        DateTime SignalTime,
        int P0Bar,
        double P0,
        int EndBar,
        double EndPrice,
        double D,
        int EntryBar,
        DateTime EntryTime,
        double Entry,
        double Stop,
        double Target1,
        double Target2,
        double Risk,
        double PlannedRr,
        double EntryDepth,
        int BarsToFill,
        int BarsToEnd,
        string Outcome,
        int ExitBar,
        DateTime ExitTime,
        double ExitPrice,
        double GrossR,
        double CostR,
        double NetR,
        bool Resolved,
        bool Profitable,
        double MfeR,
        double MaeR,
        int BarsHeld,
        int Reanchors,
        int Confirmations,
        string NoFillReason,
        int DiagonalBrokenBar = -1);

    /// <summary>
    /// Causal simulator of the "diagonal as the entry impulse" trade (DIAGONAL.md §9.43).
    /// It is shared by the research harness and the ReplayViewer tab so the chart and the
    /// report cannot drift apart.
    /// </summary>
    public static class DiagonalContinuationSimulator
    {
        public static DiagonalContinuationTrade Simulate(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline = null)
        {
            return parameters.Variant switch
            {
                DiagonalContinuationVariant.SIGNAL_ANCHOR =>
                    SimulateSignalAnchor(provider, setup, parameters, wave5Timeline),
                DiagonalContinuationVariant.CONFIRMED_END =>
                    SimulateConfirmedEnd(provider, setup, parameters, wave5Timeline),
                DiagonalContinuationVariant.CORRECTION_END =>
                    SimulateCorrectionEnd(provider, setup, parameters, wave5Timeline),
                _ => SimulateDynamic(provider, setup, parameters, wave5Timeline)
            };
        }

        /// <summary>
        /// When the caller supplies the live wave-5 timeline, the diagonal's own rules decide
        /// where wave 5 ends: a missing bar means the diagonal is broken (D-W5-CAP, V(4)
        /// break or the wave-5 duration cap). Without the timeline the simulator keeps its
        /// own running extreme.
        /// </summary>
        private static bool TryGetValidWave5(
            IReadOnlyDictionary<int, DiagonalWave5State> timeline,
            int bar,
            out DiagonalWave5State state)
        {
            state = null;
            return timeline == null || timeline.TryGetValue(bar, out state);
        }

        private static DiagonalContinuationTrade SimulateSignalAnchor(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline)
        {
            bool isUp = setup.IsUp;
            int sgn = isUp ? 1 : -1;
            double p0 = setup.P0;
            double end = setup.P5;
            double d = Math.Abs(end - p0);
            if (d <= 1e-12)
                return NoFill(setup, parameters, "degenerateDiagonal");

            int signalBar = setup.SignalBar;
            int lastBar = Math.Min(provider.Count - 1, signalBar + parameters.MaxWaitBars);
            double entryLevel = end - sgn * parameters.EnterRatio * d;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                if (!TryGetValidWave5(wave5Timeline, i, out _))
                    return NoFill(setup, parameters, "diagonalBroken", diagonalBrokenBar: i);

                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;
                if (!filled)
                    continue;

                double open = provider.GetOpenPrice(i);
                double entry = isUp ? Math.Min(open, entryLevel) : Math.Max(open, entryLevel);
                return Walk(provider, setup, parameters, d, end, setup.P5Bar, i, entry, 0, 0);
            }

            return NoFill(setup, parameters, "noFill");
        }

        private static DiagonalContinuationTrade SimulateConfirmedEnd(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline)
        {
            bool isUp = setup.IsUp;
            int sgn = isUp ? 1 : -1;
            double p0 = setup.P0;
            double extreme = setup.P5;
            int extremeBar = setup.P5Bar;
            int signalBar = setup.SignalBar;
            int lastBar = Math.Min(provider.Count - 1, signalBar + parameters.MaxWaitBars);

            bool confirmed = false;
            double end = extreme;
            int endBar = extremeBar;
            int confirmations = 0;
            int reanchors = 0;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                if (!TryGetValidWave5(wave5Timeline, i, out DiagonalWave5State state))
                    return NoFill(setup, parameters, "diagonalBroken", diagonalBrokenBar: i);

                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool confirmedThisBar = false;

                bool newExtreme = false;
                if (wave5Timeline != null)
                {
                    if (isUp ? state.W5 > extreme : state.W5 < extreme)
                    {
                        extreme = state.W5;
                        extremeBar = state.W5Bar;
                        newExtreme = true;
                    }
                }
                else if (isUp && high > extreme)
                {
                    extreme = high;
                    extremeBar = i;
                    newExtreme = true;
                }
                else if (!isUp && low < extreme)
                {
                    extreme = low;
                    extremeBar = i;
                    newExtreme = true;
                }

                if (newExtreme && confirmed)
                {
                    confirmed = false;
                    reanchors++;
                }

                double height = Math.Abs(extreme - p0);
                if (height <= 1e-12)
                    continue;

                if (!confirmed)
                {
                    double retrace = isUp
                        ? (extreme - low) / height
                        : (high - extreme) / height;
                    if (retrace >= parameters.ConfirmRatio)
                    {
                        confirmed = true;
                        confirmedThisBar = true;
                        end = extreme;
                        endBar = extremeBar;
                        confirmations++;
                    }
                }

                if (!confirmed || confirmedThisBar)
                    continue;

                double d = Math.Abs(end - p0);
                if (d <= 1e-12)
                    continue;

                double entryLevel = end - sgn * parameters.EnterRatio * d;
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;
                if (!filled)
                    continue;

                double open = provider.GetOpenPrice(i);
                double entry = isUp ? Math.Min(open, entryLevel) : Math.Max(open, entryLevel);
                return Walk(provider, setup, parameters, d, end, endBar, i, entry,
                    confirmations, reanchors);
            }

            return NoFill(setup, parameters,
                confirmed ? "noFill" : "noEndConfirmation");
        }

        /// <summary>
        /// V4: wait for the first correction after the diagonal, confirm the end when the
        /// price retraces <c>c</c> of the running height, and enter at the confirmation
        /// close (or the next open). The stop is P0; the target is measured from the
        /// correction extreme C (<c>C + f*D</c>), the classic projection.
        /// </summary>
        private static DiagonalContinuationTrade SimulateCorrectionEnd(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline)
        {
            bool isUp = setup.IsUp;
            double p0 = setup.P0;
            double extreme = setup.P5;
            int extremeBar = setup.P5Bar;
            int signalBar = setup.SignalBar;
            int lastBar = Math.Min(provider.Count - 1, signalBar + parameters.MaxWaitBars);

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                if (!TryGetValidWave5(wave5Timeline, i, out DiagonalWave5State state))
                    return NoFill(setup, parameters, "diagonalBroken", diagonalBrokenBar: i);

                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);

                bool newExtreme = false;
                if (wave5Timeline != null)
                {
                    if (isUp ? state.W5 > extreme : state.W5 < extreme)
                    {
                        extreme = state.W5;
                        extremeBar = state.W5Bar;
                        newExtreme = true;
                    }
                }
                else if (isUp && high > extreme)
                {
                    extreme = high;
                    extremeBar = i;
                    newExtreme = true;
                }
                else if (!isUp && low < extreme)
                {
                    extreme = low;
                    extremeBar = i;
                    newExtreme = true;
                }

                // A new extreme means the diagonal is still extending; wait for the next
                // retrace. The confirmation is not evaluated on the new-extreme bar.
                if (newExtreme)
                    continue;

                double height = Math.Abs(extreme - p0);
                if (height <= 1e-12)
                    continue;

                double retrace = isUp
                    ? (extreme - low) / height
                    : (high - extreme) / height;
                if (retrace < parameters.ConfirmRatio)
                    continue;

                int entryBar = parameters.EntryAtNextOpen ? i + 1 : i;
                if (entryBar >= provider.Count)
                    return NoFill(setup, parameters, "noEntryBar");

                double entry = parameters.EntryAtNextOpen
                    ? provider.GetOpenPrice(entryBar)
                    : provider.GetClosePrice(i);

                // The correction extreme C is the counter-move's extreme on the
                // confirmation bar; the classic target is C + f*D.
                double correctionExtreme = isUp ? low : high;
                return Walk(
                    provider, setup, parameters, height, extreme, extremeBar,
                    entryBar, entry, confirmations: 1, reanchors: 0,
                    correctionExtreme: correctionExtreme);
            }

            return NoFill(setup, parameters, "noCorrectionConfirmation");
        }

        private static DiagonalContinuationTrade SimulateDynamic(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline)
        {
            bool isUp = setup.IsUp;
            int sgn = isUp ? 1 : -1;
            double p0 = setup.P0;
            double extreme = setup.P5;
            int extremeBar = setup.P5Bar;
            int signalBar = setup.SignalBar;
            int lastBar = Math.Min(provider.Count - 1, signalBar + parameters.MaxWaitBars);
            int reanchors = 0;

            for (int i = signalBar + 1; i <= lastBar; i++)
            {
                if (!TryGetValidWave5(wave5Timeline, i, out DiagonalWave5State state))
                    return NoFill(setup, parameters, "diagonalBroken", diagonalBrokenBar: i);

                // The level for this bar uses the end known at the previous close.
                double height = Math.Abs(extreme - p0);
                if (height <= 1e-12)
                    continue;

                double entryLevel = extreme - sgn * parameters.EnterRatio * height;
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                bool filled = isUp ? low <= entryLevel : high >= entryLevel;

                if (filled)
                {
                    double open = provider.GetOpenPrice(i);
                    double entry = isUp ? Math.Min(open, entryLevel) : Math.Max(open, entryLevel);
                    return Walk(provider, setup, parameters, height, extreme, extremeBar,
                        i, entry, 0, reanchors);
                }

                bool newExtreme = false;
                if (wave5Timeline != null)
                {
                    if (state.W5 != extreme || state.W5Bar != extremeBar)
                    {
                        extreme = state.W5;
                        extremeBar = state.W5Bar;
                        newExtreme = true;
                    }
                }
                else if (isUp && high > extreme)
                {
                    extreme = high;
                    extremeBar = i;
                    newExtreme = true;
                }
                else if (!isUp && low < extreme)
                {
                    extreme = low;
                    extremeBar = i;
                    newExtreme = true;
                }

                if (newExtreme)
                    reanchors++;
            }

            return NoFill(setup, parameters, "noFill");
        }

        private static DiagonalContinuationTrade Walk(
            IBarsProvider provider,
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            double d,
            double end,
            int endBar,
            int entryBar,
            double entry,
            int confirmations,
            int reanchors,
            double? correctionExtreme = null)
        {
            bool isUp = setup.IsUp;
            int sgn = isUp ? 1 : -1;
            double p0 = setup.P0;
            double stop = p0 - sgn * parameters.StopBuffer * d;
            double originalStop = stop;
            double risk = Math.Abs(entry - stop);
            if (risk <= 1e-12)
                return NoFill(setup, parameters, "degenerateStop");

            double target1 = parameters.TargetKind switch
            {
                DiagonalContinuationTargetKind.DIAGONAL_START =>
                    p0 + sgn * parameters.TargetRatio * d,
                DiagonalContinuationTargetKind.DIAGONAL_END =>
                    end + sgn * parameters.TargetRatio * d,
                DiagonalContinuationTargetKind.CORRECTION_END =>
                    (correctionExtreme ?? end) + sgn * parameters.TargetRatio * d,
                _ => entry + sgn * parameters.TargetRatio * d
            };

            if (sgn * (target1 - entry) <= 0)
                return NoFill(setup, parameters, "targetBehindEntry");

            double target2 = parameters.Ladder
                ? p0 + sgn * parameters.LadderTp2Ratio * d
                : target1;
            if (parameters.Ladder && sgn * (target2 - target1) <= 0)
                return NoFill(setup, parameters, "degenerateLadder");

            int lastBar = Math.Min(provider.Count - 1, entryBar + parameters.MaxHoldBars);
            double mfeR = 0;
            double maeR = 0;
            double exitPrice = 0;
            int exitBar = -1;
            string outcome = "CENSOR";
            double grossR = 0;
            bool tp1Hit = false;

            double high = provider.GetHighPrice(entryBar);
            double low = provider.GetLowPrice(entryBar);
            TrackExcursion(isUp, entry, high, low, risk, ref mfeR, ref maeR);

            bool stopHit = isUp ? low <= stop : high >= stop;
            if (stopHit)
            {
                outcome = "SL";
                exitBar = entryBar;
                exitPrice = stop;
                grossR = -1;
            }
            else
            {
                for (int i = entryBar + 1; i <= lastBar; i++)
                {
                    high = provider.GetHighPrice(i);
                    low = provider.GetLowPrice(i);
                    TrackExcursion(isUp, entry, high, low, risk, ref mfeR, ref maeR);

                    bool stopNow = isUp ? low <= stop : high >= stop;
                    bool tp1Now = isUp ? high >= target1 : low <= target1;
                    bool tp2Now = parameters.Ladder &&
                                  (isUp ? high >= target2 : low <= target2);

                    if (parameters.Ladder && tp1Now && !tp1Hit)
                    {
                        tp1Hit = true;
                        stop = entry;
                    }

                    if (stopNow)
                    {
                        outcome = "SL";
                        exitBar = i;
                        exitPrice = stop;
                        grossR = tp1Hit
                            ? 0.5 * (sgn * (target1 - entry) / risk) +
                              0.5 * (sgn * (stop - entry) / risk)
                            : -1;
                        break;
                    }

                    if (parameters.Ladder && tp1Hit && tp2Now)
                    {
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target2;
                        grossR = 0.5 * (sgn * (target1 - entry) / risk) +
                                0.5 * (sgn * (target2 - entry) / risk);
                        break;
                    }

                    if (!parameters.Ladder && tp1Now)
                    {
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target1;
                        grossR = sgn * (target1 - entry) / risk;
                        break;
                    }
                }
            }

            if (outcome is not ("TP" or "SL"))
            {
                int markBar = Math.Min(provider.Count - 1, entryBar + parameters.MaxHoldBars);
                exitBar = markBar;
                exitPrice = provider.GetClosePrice(markBar);
                outcome = markBar < entryBar + parameters.MaxHoldBars ? "CENSOR" : "TIME";
                double markR = sgn * (exitPrice - entry) / risk;
                grossR = tp1Hit
                    ? 0.5 * (sgn * (target1 - entry) / risk) + 0.5 * markR
                    : markR;
            }

            double costR = entry * parameters.CostRate / risk;
            double netR = grossR - costR;
            bool resolved = outcome is "TP" or "SL";

            return new DiagonalContinuationTrade(
                setup.IsUp,
                setup.SignalBar,
                setup.SignalTime,
                setup.P0Bar,
                p0,
                endBar,
                end,
                d,
                entryBar,
                provider.GetOpenTime(entryBar),
                entry,
                originalStop,
                target1,
                target2,
                risk,
                Math.Abs(target1 - entry) / risk,
                sgn * (end - entry) / d,
                entryBar - setup.SignalBar,
                endBar - setup.SignalBar,
                outcome,
                exitBar,
                exitBar >= 0 ? provider.GetOpenTime(exitBar) : default,
                exitPrice,
                grossR,
                costR,
                netR,
                resolved,
                resolved && netR > 0,
                mfeR,
                maeR,
                exitBar >= 0 ? exitBar - entryBar : 0,
                reanchors,
                confirmations,
                string.Empty);
        }

        private static void TrackExcursion(
            bool isUp,
            double entry,
            double high,
            double low,
            double risk,
            ref double mfeR,
            ref double maeR)
        {
            double favorable = isUp ? high - entry : entry - low;
            double adverse = isUp ? entry - low : high - entry;
            mfeR = Math.Max(mfeR, favorable / risk);
            maeR = Math.Max(maeR, adverse / risk);
        }

        private static DiagonalContinuationTrade NoFill(
            DiagonalContinuationSetup setup,
            DiagonalContinuationParams parameters,
            string reason,
            int diagonalBrokenBar = -1)
        {
            double d = Math.Abs(setup.P5 - setup.P0);
            return new DiagonalContinuationTrade(
                setup.IsUp,
                setup.SignalBar,
                setup.SignalTime,
                setup.P0Bar,
                setup.P0,
                setup.P5Bar,
                setup.P5,
                d,
                -1,
                default,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "NOFILL",
                -1,
                default,
                0,
                0,
                0,
                0,
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                reason,
                diagonalBrokenBar);
        }
    }
}
