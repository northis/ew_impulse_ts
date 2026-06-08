using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Json;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Orchestrates a bar-by-bar Elliott Wave v2 markup replay over a CSV data file.
/// Builds a zigzag and runs <see cref="ElliottWaveExactMarkupV2.Parse"/> on
/// growing prefixes, collecting delta frames (§17.1) plus the full snapshot (§17).
/// </summary>
public sealed class ReplayEngine
{
    private readonly ReplayBarsProvider m_BarsProvider;

    // ── Incremental replay state (§14.6) ──
    private IReadOnlyList<BarPoint>? m_Pivots;
    private ElliottWaveExactMarkupV2? m_FullMarkup;
    private Dictionary<string, string>? m_PrevState;
    private int m_CurrentStep;
    private bool m_Initialized;
    private int m_StartBar;
    private int m_EndBar;
    private int m_LastRevealedBar;
    private IReadOnlyList<CandleBar>? m_AllCandles;

    public ReplayEngine(ReplayBarsProvider barsProvider)
    {
        m_BarsProvider = barsProvider;
    }

    // ── Public state (read by Program.cs for snapshot export) ──

    /// <summary>The fully-built markup for the complete range (for final snapshot).</summary>
    public ElliottWaveExactMarkupV2? FullMarkup => m_FullMarkup;

    /// <summary>Whether the engine has been initialised with a CSV.</summary>
    public bool IsInitialized => m_Initialized;

    /// <summary>Resolved first bar index of the active range (after date/clamp resolution).</summary>
    public int StartBar => m_StartBar;

    /// <summary>Resolved last bar index of the active range (after date/clamp resolution).</summary>
    public int EndBar => m_EndBar;

    /// <summary>Number of replay steps (pivot frames) available for the active range.</summary>
    public int TotalSteps => m_Initialized ? Math.Max(0, m_Pivots!.Count - 1) : 0;

    /// <summary>Price decimal places detected in the loaded CSV (drives chart precision).</summary>
    public int PriceDecimals => m_BarsProvider.PriceDecimals;

    /// <summary>Symbol name of the loaded data.</summary>
    public string Symbol => m_BarsProvider.BarSymbol.Name;

    /// <summary>Timeframe name of the loaded data.</summary>
    public string Timeframe => m_BarsProvider.TimeFrame.Name;

    /// <summary>Whether there are more pivot-frames to process.</summary>
    public bool HasMoreSteps => m_Initialized && m_CurrentStep <= m_Pivots!.Count;

    /// <summary>Describes a CSV file available for replay.</summary>
    public static IEnumerable<CsvFileInfo> ListCsvFiles(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            yield break;

        foreach (string f in Directory.EnumerateFiles(dataDir, "*.csv"))
        {
            var fi = new FileInfo(f);
            var info = new CsvFileInfo
            {
                Name = fi.Name,
                Path = f,
                SizeBytes = fi.Length
            };

            // Read first and last data lines to extract date range (cheap — no full load)
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                // Check if there's a header
                string? header = reader.ReadLine();
                bool hasHeader = header != null &&
                    (header.StartsWith("Time") || header.Contains($"Open{Helper.CSV_SEPARATOR}High{Helper.CSV_SEPARATOR}Low{Helper.CSV_SEPARATOR}Close"));

                string? firstDataLine = hasHeader ? reader.ReadLine() : header;
                string? lastDataLine = null;
                int lineCount = firstDataLine != null ? 1 : 0;

                // Read to end to find last line (CSVs are ~hundreds of KB — fine)
                while (reader.ReadLine() is { } line)
                {
                    lastDataLine = line;
                    lineCount++;
                }

                if (firstDataLine != null)
                {
                    info.BarCount = lineCount;
                    string[] parts = firstDataLine.Split(Helper.CSV_SEPARATOR);
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0],
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime firstDt))
                        info.FirstBarTime = DateTime.SpecifyKind(firstDt, DateTimeKind.Utc).ToString("o");
                }

                if (lastDataLine != null)
                {
                    string[] parts = lastDataLine.Split(Helper.CSV_SEPARATOR);
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0],
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime lastDt))
                        info.LastBarTime = DateTime.SpecifyKind(lastDt, DateTimeKind.Utc).ToString("o");
                }
            }
            catch { /* can't read — leave times null */ }

            yield return info;
        }
    }

    // ══════════════════════════════════════════════════════
    //  Incremental bar-by-bar replay driver (§14.6 / §17.1)
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Initialises the engine for step-by-step replay.  Loads candles, builds the
    /// full-range zigzag, and positions the cursor at the second pivot (first frame).
    /// </summary>
    public void Initialize(
        string csvPath, int startBar, int endBar, double? deviationPercent = null)
    {
        m_BarsProvider.LoadCandles(csvPath);

        int n = m_BarsProvider.Count;
        if (endBar <= 0 || endBar >= n)
            endBar = n - 1;
        if (startBar < 0)
            startBar = 0;
        if (startBar >= endBar)
            startBar = Math.Max(0, endBar - 1);
        if (endBar <= startBar)
            throw new ArgumentException(
                $"Range too narrow: startBar={startBar}, endBar={endBar}, total bars={n}. " +
                "Try widening the date range.");

        m_FullMarkup = new ElliottWaveExactMarkupV2(
            m_BarsProvider, startBar, endBar, deviationPercent, isUpDirection: false);

        m_StartBar = startBar;
        m_EndBar = endBar;
        m_LastRevealedBar = startBar - 1;
        m_AllCandles = m_BarsProvider.GetAllCandles();
        m_Pivots = m_FullMarkup.Pivots;
        m_PrevState = new Dictionary<string, string>();
        m_CurrentStep = 2; // need at least 2 pivots for the first frame
        m_Initialized = true;
    }

    /// <summary>
    /// Initialises the engine from date strings (used by the SSE endpoint).</summary>
    public void InitializeForDates(
        string csvPath, string? fromDate, string? toDate, double? deviationPercent = null)
    {
        m_BarsProvider.LoadCandles(csvPath);
        (int start, int end) = m_BarsProvider.ResolveDateRange(fromDate ?? "", toDate ?? "");
        Initialize(csvPath, start, end, deviationPercent);
    }

    /// <summary>
    /// Advances the replay by one pivot, re-parsing the growing prefix, and returns
    /// the delta frame (<c>null</c> when all pivots have been consumed).
    /// </summary>
    public EwReplayFrameDto? StepForward(int deadDepth = 1) => StepCore(deadDepth)?.Frame;

    /// <summary>
    /// Advances the replay by exactly one zigzag segment (one pivot) and returns the
    /// delta frame, the full tree snapshot at this step, and the candles that make up
    /// the newly added segment. Returns <c>null</c> once the range is exhausted.
    /// </summary>
    public ReplayStepResult? Step(int deadDepth = 1)
    {
        StepCoreResult? core = StepCore(deadDepth);
        if (core == null)
            return null;

        StepCoreResult c = core.Value;
        EwTreeSnapshotDto snapshot = EwMarkupTreeExporter.BuildSnapshot(c.Sub, c.Result, deadDepth);

        // Reveal all bars composing the new segment: (lastRevealed .. newPivotBar].
        int newPivotBar = c.Frame.NewPivot?.BarIndex ?? m_LastRevealedBar;
        var newCandles = new List<CandleBar>();
        if (m_AllCandles != null)
        {
            for (int b = m_LastRevealedBar + 1; b <= newPivotBar && b < m_AllCandles.Count; b++)
                newCandles.Add(m_AllCandles[b]);
        }
        m_LastRevealedBar = Math.Max(m_LastRevealedBar, newPivotBar);

        return new ReplayStepResult
        {
            Frame = c.Frame,
            Snapshot = snapshot,
            NewCandles = newCandles
        };
    }

    /// <summary>
    /// Core single-step parse: re-parses the growing pivot prefix and builds the delta
    /// frame, returning the markup and search result so callers can also export a snapshot.
    /// </summary>
    private StepCoreResult? StepCore(int deadDepth)
    {
        if (!m_Initialized || m_CurrentStep > m_Pivots!.Count)
            return null;

        int k = m_CurrentStep++;
        var prefix = m_Pivots!.Take(k).ToList();
        var sub = new ElliottWaveExactMarkupV2(
            m_FullMarkup!.BarsProvider, prefix, m_FullMarkup.DeviationPercent);
        MarkupSearchResult r = sub.ParseTiled();

        // ── Collect current tree ──
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

        // ── Compute delta events ──
        var events = new List<EwReplayEventDto>();
        foreach (var kv in cur)
        {
            if (!m_PrevState!.TryGetValue(kv.Key, out string? prevStatus))
            {
                events.Add(new EwReplayEventDto
                {
                    Type = "BORN", NodeId = kv.Key,
                    Model = meta[kv.Key].model, WavePos = meta[kv.Key].wavePos
                });
            }
            else if (prevStatus != kv.Value)
            {
                string? type = kv.Value == nameof(NodeStatus.COMPLETE) && prevStatus == nameof(NodeStatus.PROJECTED)
                    ? "COMPLETED"
                    : kv.Value == nameof(NodeStatus.PROJECTED) ? "PROJECTED" : null;
                if (type != null)
                    events.Add(new EwReplayEventDto { Type = type, NodeId = kv.Key });
            }
        }
        foreach (string deadId in m_PrevState!.Keys.Where(id => !cur.ContainsKey(id)))
            events.Add(new EwReplayEventDto { Type = "DIED", NodeId = deadId });

        events = events
            .OrderBy(e => e.Type, StringComparer.Ordinal)
            .ThenBy(e => e.NodeId, StringComparer.Ordinal)
            .ToList();

        string? best = r.Roots.Count > 0
            ? StableId(r.Roots[0])
            : r.BestProjection != null ? StableId(r.BestProjection) : null;

        var frame = new EwReplayFrameDto
        {
            BarIndex = m_Pivots[k - 1].BarIndex,
            CloseTime = m_Pivots[k - 1].OpenTime,
            NewPivot = new EwPivotDto
            {
                BarIndex = m_Pivots[k - 1].BarIndex,
                Price = m_Pivots[k - 1].Value,
                IsHigh = ComputePivotIsHigh(sub, k - 1)
            },
            Events = events,
            AliveNodeIds = cur.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            BestNodeId = best
        };

        m_PrevState = cur;
        return new StepCoreResult { Frame = frame, Sub = sub, Result = r };
    }

    /// <summary>Runs the full bar-by-bar replay over <c>[startBar..endBar]</c>.</summary>
    public ReplayData Run(
        string csvPath, int startBar, int endBar, int deadDepth = 1,
        double? deviationPercent = null)
    {
        Initialize(csvPath, startBar, endBar, deviationPercent);

        // Candles for the web chart
        int firstBar = startBar;
        int lastBar = endBar < m_BarsProvider.Count ? endBar : m_BarsProvider.Count - 1;
        var candles = m_BarsProvider.GetAllCandles()
            .Skip(firstBar).Take(lastBar - firstBar + 1).ToList();

        // Build replay frames by stepping through every pivot
        var replay = new EwReplayDto
        {
            Symbol = m_BarsProvider.BarSymbol.Name,
            Timeframe = m_BarsProvider.TimeFrame.Name
        };

        while (HasMoreSteps)
        {
            EwReplayFrameDto? frame = StepForward(deadDepth);
            if (frame != null)
                replay.Frames.Add(frame);
        }

        // Final snapshot
        MarkupSearchResult result = m_FullMarkup!.ParseTiled();
        EwTreeSnapshotDto snapshot = EwMarkupTreeExporter.BuildSnapshot(
            m_FullMarkup, result, deadDepth);

        return new ReplayData
        {
            Candles = candles,
            Replay = replay,
            Snapshot = snapshot,
            StartBar = firstBar,
            EndBar = lastBar
        };
    }

    /// <summary>Runs the replay for a date range (ISO-format strings).</summary>
    public ReplayData RunByDate(
        string csvPath, string? fromDate, string? toDate,
        int deadDepth = 1, double? deviationPercent = null)
    {
        m_BarsProvider.LoadCandles(csvPath);
        (int start, int end) = m_BarsProvider.ResolveDateRange(fromDate ?? "", toDate ?? "");
        return Run(csvPath, start, end, deadDepth, deviationPercent);
    }

    /// <summary>Returns basic info about a CSV file.</summary>
    public CsvFileInfo GetFileInfo(string csvPath)
    {
        var fi = new FileInfo(csvPath);
        var info = new CsvFileInfo
        {
            Name = fi.Name,
            Path = csvPath,
            SizeBytes = fi.Length
        };

        if (fi.Exists)
        {
            // Quick peek: load first bar and last bar
            m_BarsProvider.LoadCandles(csvPath);
            int n = m_BarsProvider.Count;
            if (n > 0)
            {
                info.BarCount = n;
                info.FirstBarTime = m_BarsProvider.GetOpenTime(0).ToString("o");
                info.LastBarTime = m_BarsProvider.GetOpenTime(n - 1).ToString("o");
            }
        }

        return info;
    }

    // ── private helpers ──────────────────────────────────

    /// <summary>
    /// Frame-stable id: same logical node keeps its id across replay steps.
    /// Mirrors <c>EwMarkupTreeExporter.StableId</c>.
    /// </summary>
    private static string StableId(TreeNode node) =>
        $"{node.Model}|{node.RangeStartSegment}-{node.RangeEndSegment}|{node.WavePos ?? "root"}|L{node.Level}";

    /// <summary>
    /// Determines whether <c>pivots[index]</c> is a swing high from segment directions.
    /// </summary>
    private static bool ComputePivotIsHigh(ElliottWaveExactMarkupV2 markup, int index)
    {
        bool firstSegUp = markup.Segments[0].IsUp;
        return firstSegUp ? index % 2 == 1 : index % 2 == 0;
    }
}

/// <summary>Internal result of a single parse step (frame + markup for snapshot export).</summary>
internal readonly struct StepCoreResult
{
    public EwReplayFrameDto Frame { get; init; }
    public ElliottWaveExactMarkupV2 Sub { get; init; }
    public MarkupSearchResult Result { get; init; }
}

/// <summary>Result of one on-demand replay step (§17.1) for the interactive viewer.</summary>
public sealed class ReplayStepResult
{
    /// <summary>The delta frame for this step (new pivot, events, alive ids, best id).</summary>
    public EwReplayFrameDto Frame { get; set; } = null!;

    /// <summary>The full tree snapshot at this step (nodes + zigzag so far).</summary>
    public EwTreeSnapshotDto Snapshot { get; set; } = null!;

    /// <summary>The candles composing the newly added segment (to append to the chart).</summary>
    public IReadOnlyList<CandleBar> NewCandles { get; set; } = Array.Empty<CandleBar>();
}

// ---- DTOs ----

public sealed class CsvFileInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int BarCount { get; set; }
    public string? FirstBarTime { get; set; }
    public string? LastBarTime { get; set; }
}

public sealed class ReplayData
{
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public EwReplayDto Replay { get; set; } = null!;
    public EwTreeSnapshotDto Snapshot { get; set; } = null!;
    public int StartBar { get; set; }
    public int EndBar { get; set; }
}
