using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Wraps <see cref="DiagonalSetupFinder"/> for the continuation robot. The inner finder
    /// still detects the completed diagonal and computes the counter-trend signal, but this
    /// wrapper <b>does not raise</b> <c>OnEnter</c>: the base robot must not open the
    /// counter-trend trade. The continuation robot reads <see cref="CurrentDiagonalSignal"/>
    /// and manages its own limit order (DIAGONAL.md §9.43).
    /// </summary>
    public class DiagonalContinuationSetupFinder
        : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        private readonly DiagonalSetupFinder m_Inner;
        private bool m_InnerInitialized;

        public DiagonalContinuationSetupFinder(
            IBarsProvider barsProvider,
            ISymbol symbol,
            EWParams ewParams,
            DiagonalParams diagonalParams)
            : base(barsProvider, symbol)
        {
            m_Inner = new DiagonalSetupFinder(
                barsProvider,
                symbol,
                ewParams,
                diagonalParams.TakeProfitRatio,
                diagonalParams.RequireWave5Ratio,
                diagonalParams.RequireWave4Ratio,
                false,
                diagonalParams.TakeProfitMode,
                diagonalParams.MinConvergence,
                diagonalParams.MaxConvergence,
                diagonalParams.RequireInsideWedge,
                diagonalParams.MaxSpillAreaRatio,
                diagonalParams.MinWave3Penetration,
                diagonalParams.MaxWaveDurationRatio,
                retraceAction: diagonalParams.RetraceAction,
                minRiskRewardRatio: diagonalParams.MinRiskRewardRatio,
                wave3RetraceRatio: diagonalParams.Wave3RetraceRatio,
                minWave4Wave2Level: diagonalParams.MinWave4Wave2Level,
                minWave4RetraceW3: diagonalParams.MinWave4RetraceW3,
                maxWave4RetraceW3: diagonalParams.MaxWave4RetraceW3,
                requireWave4Shorter: diagonalParams.RequireWave4Shorter,
                requireWave2Shorter: diagonalParams.RequireWave2Shorter,
                minWave2Retrace: diagonalParams.MinWave2Retrace,
                maxWave2Retrace: diagonalParams.MaxWave2Retrace,
                maxWave5SpillRatio: diagonalParams.MaxWave5SpillRatio,
                minWave4Wave2DurationRatio: diagonalParams.MinWave4Wave2DurationRatio,
                minWave3Wave1DurationRatio: diagonalParams.MinWave3Wave1DurationRatio,
                minWave2Wave1DurationRatio: diagonalParams.MinWave2Wave1DurationRatio,
                requireThickWaveEnds: diagonalParams.RequireThickWaveEnds,
                thinMarketGuard: diagonalParams.ThinMarketGuard,
                takeProfitRetraceRatio: diagonalParams.TakeProfitRetraceRatio,
                stopWave3Ratio: diagonalParams.StopWave3Ratio,
                triggerMode: diagonalParams.TriggerMode,
                wave5SpillMode: diagonalParams.Wave5SpillMode,
                wave5SpillThreshold: diagonalParams.Wave5SpillThreshold,
                minSignalRiskRewardRatio: diagonalParams.MinSignalRiskRewardRatio,
                initialMode: diagonalParams.InitialMode);

            // Suppress the counter-trend signal: only store the diagonal context for the
            // continuation manager. While a continuation setup is active, ignore new ones.
            m_Inner.OnEnter += (_, args) =>
            {
                if (!IsInSetup)
                    CurrentDiagonalSignal = args;
            };
        }

        /// <summary>The last diagonal signal not yet consumed by the continuation robot.</summary>
        public ElliottWaveSignalEventArgs CurrentDiagonalSignal { get; private set; }

        /// <summary>Consumes the pending diagonal signal (returns <c>null</c> when there is none).</summary>
        public ElliottWaveSignalEventArgs ConsumeDiagonalSignal()
        {
            ElliottWaveSignalEventArgs signal = CurrentDiagonalSignal;
            CurrentDiagonalSignal = null;
            return signal;
        }

        /// <summary>A distinct id, so the continuation comments cannot collide with the
        /// counter-trend finder's comments.</summary>
        public override string Id => base.Id + "C";

        protected override void CheckSetup(DateTime openDateTime)
        {
            // During the initial warm-up the inner finder must see the bars but must not
            // emit signals. It is marked initialized only after the wrapper is initialized.
            if (!IsInitialized)
            {
                m_Inner.CheckBar(openDateTime);
                return;
            }

            if (!m_InnerInitialized)
            {
                m_Inner.MarkAsInitialized();
                m_InnerInitialized = true;
            }

            m_Inner.CheckBar(openDateTime);
        }

        public override void CheckTick(SymbolTickEventArgs tick)
        {
            if (m_InnerInitialized)
                m_Inner.CheckTick(tick);
        }
    }
}
