using System;
using System.Collections.Generic;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Step 7 of EW_MARKUP_v2.md §19 — the engine-side support for tree export (§17).
    /// During a search started with <c>deadDepth &gt; 0</c> every wave hypothesis that
    /// dies on a hard rule is recorded as a <see cref="DeadCapture"/> keyed by the parent
    /// assembly it belonged to. The JSON exporter (<c>EwMarkupTreeExporter</c>) then asks
    /// <see cref="DeadAlternativesFor"/> for the dead siblings hanging off each exported
    /// live node so the debug tree shows why competing branches were pruned. Capturing is
    /// off by default, so production parses keep zero overhead.
    /// </summary>
    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>Hard ceiling on captured dead hypotheses to bound debug memory (§17).</summary>
        private const int MAX_DEAD_CAPTURES = 20_000;

        /// <summary>Maximum dead siblings kept per parent assembly when exporting (§17).</summary>
        private const int MAX_DEAD_PER_PARENT = 12;

        /// <summary>Requested dead-branch export depth (0 disables capturing).</summary>
        private int m_DeadDepth;

        /// <summary>Flat log of dead hypotheses captured during the last search (or null).</summary>
        private List<DeadCapture> m_DeadCaptures;

        /// <summary>Lazily-built lookup from a parent assembly to its dead alternatives.</summary>
        private Dictionary<(ElliottModelType, int, int), List<DeadAlternative>> m_DeadLookup;

        /// <summary>
        /// Gets the maximum tree depth (root = 0) at which dead branches were captured
        /// during the last <see cref="Parse(int)"/> call.
        /// </summary>
        internal int DeadDepth => m_DeadDepth;

        /// <summary>
        /// A dead wave hypothesis recorded at the moment it failed a hard rule, together
        /// with the parent assembly (model + segment range) it belonged to.
        /// </summary>
        private readonly record struct DeadCapture(
            ElliottModelType ParentModel,
            int ParentStart,
            int ParentEnd,
            int WaveIndex,
            ElliottModelType Model,
            int StartSeg,
            int EndSeg,
            DeathReason Reason);

        /// <summary>
        /// A dead sibling exposed to the exporter: the model that was tried for a given
        /// wave position of a live parent node, the segment range it spanned and why it died.
        /// </summary>
        internal sealed record DeadAlternative(
            int WaveIndex,
            ElliottModelType Model,
            int StartSeg,
            int EndSeg,
            bool IsUp,
            DeathReason Reason);

        /// <summary>
        /// Returns the dead wave hypotheses that were tried (and pruned) for the live
        /// parent assembly <paramref name="parentModel"/> spanning
        /// <c>[parentStart..parentEnd]</c>. Empty when no captures match.
        /// </summary>
        internal IReadOnlyList<DeadAlternative> DeadAlternativesFor(
            ElliottModelType parentModel, int parentStart, int parentEnd)
        {
            EnsureDeadLookup();
            return m_DeadLookup != null &&
                   m_DeadLookup.TryGetValue((parentModel, parentStart, parentEnd), out List<DeadAlternative> list)
                ? list
                : Array.Empty<DeadAlternative>();
        }

        /// <summary>
        /// Returns whether the zigzag pivot at <paramref name="pivotIndex"/> is a swing high,
        /// inferred from the direction of the adjacent segment(s).
        /// </summary>
        internal bool PivotIsHigh(int pivotIndex)
        {
            if (pivotIndex > 0)
                return Pivots[pivotIndex].Value > Pivots[pivotIndex - 1].Value;
            // First pivot: it is a high when the first segment travels down.
            return Pivots.Count > 1 && Pivots[0].Value > Pivots[1].Value;
        }

        /// <summary>
        /// Builds <see cref="m_DeadLookup"/> from the flat capture log, deduplicating
        /// identical hypotheses and capping the number kept per parent assembly.
        /// </summary>
        private void EnsureDeadLookup()
        {
            if (m_DeadLookup != null || m_DeadCaptures == null)
                return;

            m_DeadLookup = new Dictionary<(ElliottModelType, int, int), List<DeadAlternative>>();
            var seen = new HashSet<(ElliottModelType, int, int, int, ElliottModelType, int, int)>();

            foreach (DeadCapture d in m_DeadCaptures)
            {
                var dedupKey = (d.ParentModel, d.ParentStart, d.ParentEnd, d.WaveIndex, d.Model, d.StartSeg, d.EndSeg);
                if (!seen.Add(dedupKey))
                    continue;

                var parentKey = (d.ParentModel, d.ParentStart, d.ParentEnd);
                if (!m_DeadLookup.TryGetValue(parentKey, out List<DeadAlternative> list))
                {
                    list = new List<DeadAlternative>();
                    m_DeadLookup[parentKey] = list;
                }

                if (list.Count >= MAX_DEAD_PER_PARENT)
                    continue;

                bool isUp = Pivots[d.EndSeg + 1].Value > Pivots[d.StartSeg].Value;
                list.Add(new DeadAlternative(d.WaveIndex, d.Model, d.StartSeg, d.EndSeg, isUp, d.Reason));
            }
        }
    }
}
