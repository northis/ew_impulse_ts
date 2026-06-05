using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;

namespace TradeKit.Core.Json
{
    /// <summary>One zigzag pivot in the exported tree (§17).</summary>
    public sealed class EwPivotDto
    {
        /// <summary>Gets or sets the bar index of the pivot.</summary>
        [JsonProperty("barIndex")]
        public int BarIndex { get; set; }

        /// <summary>Gets or sets the pivot price.</summary>
        [JsonProperty("price")]
        public double Price { get; set; }

        /// <summary>Gets or sets a value indicating whether the pivot is a swing high.</summary>
        [JsonProperty("isHigh")]
        public bool IsHigh { get; set; }
    }

    /// <summary>One flattened tree node in the exported snapshot (§17).</summary>
    public sealed class EwNodeDto
    {
        /// <summary>Gets or sets the export-local node id (e.g. <c>"n0"</c>).</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>Gets or sets the parent node id (<c>null</c> for a root).</summary>
        [JsonProperty("parentId")]
        public string ParentId { get; set; }

        /// <summary>Gets or sets the Elliott model name.</summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>Gets or sets the wave position (<c>"root"</c> for a root).</summary>
        [JsonProperty("wavePos")]
        public string WavePos { get; set; }

        /// <summary>Gets or sets the notation level (0 = smallest).</summary>
        [JsonProperty("level")]
        public int Level { get; set; }

        /// <summary>Gets or sets the inclusive start pivot index.</summary>
        [JsonProperty("startPivot")]
        public int StartPivot { get; set; }

        /// <summary>Gets or sets the inclusive end pivot index.</summary>
        [JsonProperty("endPivot")]
        public int EndPivot { get; set; }

        /// <summary>Gets or sets a value indicating whether the node travels up.</summary>
        [JsonProperty("isUp")]
        public bool IsUp { get; set; }

        /// <summary>Gets or sets the lifecycle status (COMPLETE/PROJECTED/DEAD/…).</summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>Gets or sets the death reason (<c>null</c> while alive).</summary>
        [JsonProperty("deathReason")]
        public string DeathReason { get; set; }

        /// <summary>Gets or sets the node score (§16).</summary>
        [JsonProperty("score")]
        public double Score { get; set; }

        /// <summary>Gets or sets the ids of the child nodes.</summary>
        [JsonProperty("children")]
        public List<string> Children { get; set; } = new();
    }

    /// <summary>A full markup-tree snapshot (§17, <c>$schema = ew-markup-tree/v2</c>).</summary>
    public sealed class EwTreeSnapshotDto
    {
        /// <summary>Gets or sets the schema identifier.</summary>
        [JsonProperty("$schema")]
        public string Schema { get; set; } = "ew-markup-tree/v2";

        /// <summary>Gets or sets the symbol name.</summary>
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        /// <summary>Gets or sets the timeframe name.</summary>
        [JsonProperty("timeframe")]
        public string Timeframe { get; set; }

        /// <summary>Gets or sets the first bar index of the range.</summary>
        [JsonProperty("rangeStartBar")]
        public int RangeStartBar { get; set; }

        /// <summary>Gets or sets the last bar index of the range.</summary>
        [JsonProperty("rangeEndBar")]
        public int RangeEndBar { get; set; }

        /// <summary>Gets or sets the zigzag pivots that segment the range.</summary>
        [JsonProperty("zigzag")]
        public List<EwPivotDto> Zigzag { get; set; } = new();

        /// <summary>Gets or sets the flat list of tree nodes.</summary>
        [JsonProperty("nodes")]
        public List<EwNodeDto> Nodes { get; set; } = new();
    }

    /// <summary>One delta event in a replay frame (§17.1).</summary>
    public sealed class EwReplayEventDto
    {
        /// <summary>Gets or sets the event type (BORN/DIED/COMPLETED/PROJECTED).</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Gets or sets the stable node id the event refers to.</summary>
        [JsonProperty("nodeId")]
        public string NodeId { get; set; }

        /// <summary>Gets or sets the model name (BORN events).</summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>Gets or sets the wave position (BORN events).</summary>
        [JsonProperty("wavePos")]
        public string WavePos { get; set; }
    }

    /// <summary>One replay frame produced after a closed bar / new pivot (§17.1).</summary>
    public sealed class EwReplayFrameDto
    {
        /// <summary>Gets or sets the bar index of the new pivot.</summary>
        [JsonProperty("barIndex")]
        public int BarIndex { get; set; }

        /// <summary>Gets or sets the close time of the bar.</summary>
        [JsonProperty("closeTime")]
        public DateTime CloseTime { get; set; }

        /// <summary>Gets or sets the pivot added in this frame.</summary>
        [JsonProperty("newPivot")]
        public EwPivotDto NewPivot { get; set; }

        /// <summary>Gets or sets the delta events since the previous frame.</summary>
        [JsonProperty("events")]
        public List<EwReplayEventDto> Events { get; set; } = new();

        /// <summary>Gets or sets the stable ids of all alive nodes in this frame.</summary>
        [JsonProperty("aliveNodeIds")]
        public List<string> AliveNodeIds { get; set; } = new();

        /// <summary>Gets or sets the stable id of the best node (<c>null</c> when none).</summary>
        [JsonProperty("bestNodeId")]
        public string BestNodeId { get; set; }
    }

    /// <summary>A full bar-by-bar replay (§17.1, <c>$schema = ew-markup-tree-replay/v2</c>).</summary>
    public sealed class EwReplayDto
    {
        /// <summary>Gets or sets the schema identifier.</summary>
        [JsonProperty("$schema")]
        public string Schema { get; set; } = "ew-markup-tree-replay/v2";

        /// <summary>Gets or sets the symbol name.</summary>
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        /// <summary>Gets or sets the timeframe name.</summary>
        [JsonProperty("timeframe")]
        public string Timeframe { get; set; }

        /// <summary>Gets or sets the replay frames in chronological order.</summary>
        [JsonProperty("frames")]
        public List<EwReplayFrameDto> Frames { get; set; } = new();
    }

    /// <summary>
    /// Step 7 of EW_MARKUP_v2.md §19 — serializes an <see cref="ElliottWaveExactMarkupV2"/>
    /// markup tree into the §17 JSON snapshot (for visualization/debugging) and the §17.1
    /// bar-by-bar replay. The snapshot flattens the live tree (COMPLETE roots plus the best
    /// PROJECTED continuation); when the search was run with <c>deadDepth &gt; 0</c> the
    /// pruned sibling hypotheses are attached as <c>DEAD</c> nodes up to that depth. The
    /// replay re-parses growing pivot prefixes and diffs consecutive snapshots into delta
    /// frames (§17.1) — a stop-gap until the engine gains the incremental driver of §14.6.
    /// </summary>
    public static class EwMarkupTreeExporter
    {
        /// <summary>
        /// Builds the §17 snapshot DTO for <paramref name="result"/>. Dead branches are
        /// attached only when <paramref name="deadDepth"/> matches the value the
        /// <paramref name="markup"/> was parsed with.
        /// </summary>
        public static EwTreeSnapshotDto BuildSnapshot(
            ElliottWaveExactMarkupV2 markup, MarkupSearchResult result, int deadDepth = 0)
        {
            if (markup == null)
                throw new ArgumentNullException(nameof(markup));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            IReadOnlyList<BarPoint> pivots = markup.Pivots;
            var snapshot = new EwTreeSnapshotDto
            {
                Symbol = markup.BarsProvider?.BarSymbol?.Name ?? "SYNTHETIC",
                Timeframe = markup.BarsProvider?.TimeFrame?.Name ?? string.Empty,
                RangeStartBar = pivots.Count > 0 ? pivots[0].BarIndex : 0,
                RangeEndBar = pivots.Count > 0 ? pivots[^1].BarIndex : 0
            };

            for (int p = 0; p < pivots.Count; p++)
                snapshot.Zigzag.Add(new EwPivotDto
                {
                    BarIndex = pivots[p].BarIndex,
                    Price = pivots[p].Value,
                    IsHigh = markup.PivotIsHigh(p)
                });

            int counter = 0;
            string Walk(TreeNode node, string parentId, int depth)
            {
                string id = "n" + counter++;
                var dto = new EwNodeDto
                {
                    Id = id,
                    ParentId = parentId,
                    Model = node.Model.ToString(),
                    WavePos = parentId == null ? "root" : node.WavePos,
                    Level = node.Level,
                    StartPivot = node.RangeStartSegment,
                    EndPivot = node.RangeEndSegment + 1,
                    IsUp = node.EndPivot != null && node.StartPivot != null &&
                           node.EndPivot.Value > node.StartPivot.Value,
                    Status = node.Status.ToString(),
                    DeathReason = node.DeathReason == AlgoBase.DeathReason.NONE
                        ? null
                        : node.DeathReason.ToString(),
                    Score = node.Score
                };
                snapshot.Nodes.Add(dto);

                foreach (TreeNode child in node.Children)
                    dto.Children.Add(Walk(child, id, depth + 1));

                if (deadDepth > 0 && depth < deadDepth)
                {
                    foreach (ElliottWaveExactMarkupV2.DeadAlternative alt in
                             markup.DeadAlternativesFor(node.Model, node.RangeStartSegment, node.RangeEndSegment))
                    {
                        string deadId = "n" + counter++;
                        snapshot.Nodes.Add(new EwNodeDto
                        {
                            Id = deadId,
                            ParentId = id,
                            Model = alt.Model.ToString(),
                            WavePos = ElliottWaveExactMarkup.GetWaveKey(node.Model, alt.WaveIndex + 1),
                            Level = Math.Max(0, node.Level - 1),
                            StartPivot = alt.StartSeg,
                            EndPivot = alt.EndSeg + 1,
                            IsUp = alt.IsUp,
                            Status = "DEAD",
                            DeathReason = alt.Reason.ToString(),
                            Score = 0
                        });
                        dto.Children.Add(deadId);
                    }
                }

                return id;
            }

            foreach (TreeNode root in result.Roots)
                Walk(root, null, 0);
            if (result.BestProjection != null)
                Walk(result.BestProjection, null, 0);

            return snapshot;
        }

        /// <summary>Builds the §17 snapshot and serializes it to indented JSON.</summary>
        public static string ToTreeJson(
            ElliottWaveExactMarkupV2 markup, MarkupSearchResult result, int deadDepth = 0) =>
            JsonConvert.SerializeObject(BuildSnapshot(markup, result, deadDepth), Formatting.Indented);

        /// <summary>
        /// Builds the §17.1 replay DTO by re-parsing growing pivot prefixes of
        /// <paramref name="markup"/> and diffing consecutive snapshots into delta frames.
        /// </summary>
        public static EwReplayDto BuildReplay(ElliottWaveExactMarkupV2 markup)
        {
            if (markup == null)
                throw new ArgumentNullException(nameof(markup));

            IReadOnlyList<BarPoint> pivots = markup.Pivots;
            var replay = new EwReplayDto
            {
                Symbol = markup.BarsProvider?.BarSymbol?.Name ?? "SYNTHETIC",
                Timeframe = markup.BarsProvider?.TimeFrame?.Name ?? string.Empty
            };

            var prev = new Dictionary<string, string>();
            for (int k = 2; k <= pivots.Count; k++)
            {
                var prefix = pivots.Take(k).ToList();
                var sub = new ElliottWaveExactMarkupV2(markup.BarsProvider, prefix, markup.DeviationPercent);
                MarkupSearchResult r = sub.Parse();

                var cur = new Dictionary<string, string>();
                var meta = new Dictionary<string, (string model, string wavePos)>();
                void Collect(TreeNode node)
                {
                    string sid = StableId(node);
                    cur[sid] = node.Status.ToString();
                    meta[sid] = (node.Model.ToString(), node.WavePos ?? "root");
                    foreach (TreeNode child in node.Children)
                        Collect(child);
                }

                foreach (TreeNode root in r.Roots)
                    Collect(root);
                if (r.BestProjection != null)
                    Collect(r.BestProjection);

                var events = new List<EwReplayEventDto>();
                foreach (KeyValuePair<string, string> kv in cur)
                {
                    if (!prev.TryGetValue(kv.Key, out string prevStatus))
                    {
                        events.Add(new EwReplayEventDto
                        {
                            Type = "BORN",
                            NodeId = kv.Key,
                            Model = meta[kv.Key].model,
                            WavePos = meta[kv.Key].wavePos
                        });
                    }
                    else if (prevStatus != kv.Value)
                    {
                        string type = kv.Value == nameof(NodeStatus.COMPLETE) && prevStatus == nameof(NodeStatus.PROJECTED)
                            ? "COMPLETED"
                            : kv.Value == nameof(NodeStatus.PROJECTED) ? "PROJECTED" : null;
                        if (type != null)
                            events.Add(new EwReplayEventDto { Type = type, NodeId = kv.Key });
                    }
                }

                foreach (string deadId in prev.Keys.Where(id => !cur.ContainsKey(id)))
                    events.Add(new EwReplayEventDto { Type = "DIED", NodeId = deadId });

                events = events
                    .OrderBy(e => e.Type, StringComparer.Ordinal)
                    .ThenBy(e => e.NodeId, StringComparer.Ordinal)
                    .ToList();

                string best = r.Roots.Count > 0
                    ? StableId(r.Roots[0])
                    : r.BestProjection != null ? StableId(r.BestProjection) : null;

                replay.Frames.Add(new EwReplayFrameDto
                {
                    BarIndex = pivots[k - 1].BarIndex,
                    CloseTime = pivots[k - 1].OpenTime,
                    NewPivot = new EwPivotDto
                    {
                        BarIndex = pivots[k - 1].BarIndex,
                        Price = pivots[k - 1].Value,
                        IsHigh = sub.PivotIsHigh(k - 1)
                    },
                    Events = events,
                    AliveNodeIds = cur.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                    BestNodeId = best
                });

                prev = cur;
            }

            return replay;
        }

        /// <summary>Builds the §17.1 replay and serializes it to indented JSON.</summary>
        public static string ToReplayJson(ElliottWaveExactMarkupV2 markup) =>
            JsonConvert.SerializeObject(BuildReplay(markup), Formatting.Indented);

        /// <summary>
        /// Returns a frame-stable id for <paramref name="node"/> derived from its model and
        /// segment range so the same logical node keeps its id across replay frames.
        /// </summary>
        private static string StableId(TreeNode node) =>
            $"{node.Model}|{node.RangeStartSegment}-{node.RangeEndSegment}|{node.WavePos ?? "root"}|L{node.Level}";
    }
}
