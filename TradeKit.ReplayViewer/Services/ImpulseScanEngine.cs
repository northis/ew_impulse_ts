using System.Text.RegularExpressions;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Scans a CSV data file for "entry impulse" trade setups using the very same
/// <see cref="ImpulseSetupFinder"/> that drives the live cTrader bot, then records
/// each setup together with its eventual TP/SL outcome. Used by the interactive
/// "impulse training" page to quiz the user on profitable-vs-losing situations.
/// </summary>
public sealed class ImpulseScanEngine
{
    /// <summary>Reasonable upper bound for a single scan (manual offline tool).</summary>
    private const int RIGHT_PADDING_BARS = 6;

    /// <summary>Runs a scan over the requested file and date range.</summary>
    public ImpulseScanResult Scan(string csvPath, ImpulseScanRequest req)
    {
        // ── 1. Resolve symbol/timeframe and load candles (with correct price digits) ──
        ITimeFrame tf = ResolveTimeFrame(csvPath);

        // Pre-load once to detect the price decimals, then build a symbol that rounds
        // SL/TP exactly like the live bot does for this instrument.
        var probe = new ReplayBarsProvider(tf);
        probe.LoadCandles(csvPath);
        int digits = probe.PriceDecimals;

        string symbolName = ResolveSymbolName(csvPath);
        double pipSize = Math.Pow(10, -digits);
        var symbol = new SymbolBase(symbolName, symbolName, 1, digits, pipSize, pipSize, 100000);

        var provider = new ReplayBarsProvider(tf, symbol);
        provider.LoadCandles(csvPath);

        int n = provider.Count;
        if (n == 0)
            return new ImpulseScanResult { Symbol = symbolName, Timeframe = tf.Name };

        (int startBar, int endBar) = provider.ResolveDateRange(
            req.FromDate ?? string.Empty, req.ToDate ?? string.Empty);
        if (endBar <= 0 || endBar >= n) endBar = n - 1;
        if (startBar < 0) startBar = 0;

        // ── 2. Build the impulse params from the page inputs + sensible defaults ──
        var impulseParams = new ImpulseParams(
            Period: req.Period,
            EnterRatio: req.EnterRatio,
            TakeRatio: req.TakeRatio,
            BreakEvenRatio: req.BreakEvenRatio,
            MaxZigzagPercent: req.MaxZigzagPercent,
            MaxOverlapseLengthPercent: req.MaxOverlapseLengthPercent,
            MaxDistance: req.MaxDistance,
            HeterogeneityMax: req.HeterogeneityMax,
            MinSizePercent: req.MinSizePercent,
            AreaPercent: req.AreaPercent,
            BarsCount: req.BarsCount);

        var tradeView = new ReplayTradeViewManager(provider);
        var finder = new ImpulseSetupFinder(provider, tradeView, impulseParams);

        var setups = new List<ImpulseSetupDto>();
        ImpulseSetupDto? pending = null;
        int enterCount = 0;

        finder.OnEnter += (_, args) =>
        {
            enterCount++;
            bool isUp = args.TakeProfit.Value > args.StopLoss.Value;
            int viewBar = provider.GetIndexByTime(args.StartViewBarTime);
            int impulseStartBar = args.Model.Wave0?.BarIndex ?? args.Level.BarIndex;
            int impulseEndBar = args.Model.Wave5?.BarIndex ?? args.Level.BarIndex;

            pending = new ImpulseSetupDto
            {
                IsUp = isUp,
                IsLimit = args.IsLimit,
                ImpulseStartBar = impulseStartBar,
                ImpulseStartPrice = args.Model.Wave0?.Value ?? args.Level.Value,
                ImpulseEndBar = impulseEndBar,
                ImpulseEndPrice = args.Model.Wave5?.Value ?? args.Level.Value,
                EntryBar = args.Level.BarIndex,
                EntryPrice = args.Level.Value,
                StopLoss = args.StopLoss.Value,
                TakeProfit = args.TakeProfit.Value,
                ViewStartBar = viewBar >= 0 ? viewBar : impulseStartBar,
                EntryTime = args.Level.OpenTime.ToString("o"),
                Comment = args.Comment ?? string.Empty,
                RatioZigzag = args.Stats?.RatioZigzag ?? 0,
                Area = args.Stats?.Area ?? 0,
                Outcome = "NONE"
            };
        };

        void Resolve(string outcome, LevelEventArgs e)
        {
            if (pending == null) return;
            pending.Outcome = outcome;
            pending.OutcomeBar = e.Level.BarIndex;
            pending.OutcomePrice = e.Level.Value;
            setups.Add(pending);
            pending = null;
        }

        finder.OnTakeProfit += (_, e) => Resolve("TP", e);
        finder.OnStopLoss += (_, e) => Resolve("SL", e);
        finder.OnCanceled += (_, e) => Resolve("CANCELED", e);

        finder.MarkAsInitialized();

        // ── 3. Drive the finder bar-by-bar (causal). Warm up from bar 0 so the zigzag
        //       has history; stop once we are past the window and no trade is open. ──
        for (int bar = 0; bar < n; bar++)
        {
            if (bar > endBar && !finder.IsInSetup)
                break;
            finder.CheckBar(provider.GetOpenTime(bar));
        }

        // ── 4. Keep only resolved (TP/SL) setups whose entry is inside the window ──
        var kept = setups
            .Where(s => s.EntryBar >= startBar && s.EntryBar <= endBar)
            .Where(s => s.Outcome is "TP" or "SL")
            .OrderBy(s => s.EntryBar)
            .ToList();

        // Re-id sequentially for the UI.
        for (int i = 0; i < kept.Count; i++)
            kept[i].Id = i;

        // ── 5. Slice candles to only the region the UI actually draws ──
        // The UI shows extra left context equal to the entry-window span, so include
        // those leading bars too (left edge = ViewStartBar - (EntryBar - ViewStartBar)).
        var candles = new List<CandleBar>();
        if (kept.Count > 0)
        {
            int minBar = kept.Min(s =>
            {
                int span = Math.Max(0, s.EntryBar - s.ViewStartBar);
                int left = s.ViewStartBar - span;
                return Math.Min(left, s.ImpulseStartBar);
            });
            int maxBar = kept.Max(s => s.OutcomeBar);
            minBar = Math.Max(0, minBar);
            maxBar = Math.Min(n - 1, maxBar + RIGHT_PADDING_BARS);
            candles = provider.GetAllCandles()
                .Skip(minBar).Take(maxBar - minBar + 1).ToList();
        }

        return new ImpulseScanResult
        {
            Symbol = symbolName,
            Timeframe = tf.Name,
            PriceDecimals = digits,
            StartBar = startBar,
            EndBar = endBar,
            Candles = candles,
            Setups = kept,
            EnterCount = enterCount,
            ResolvedCount = setups.Count,
            BarCount = n
        };
    }

    // ── helpers ──────────────────────────────────────────

    /// <summary>Extracts the symbol name from a CSV filename ("AUDCAD_h1_...csv" → "AUDCAD").</summary>
    private static string ResolveSymbolName(string csvPath)
    {
        string name = Path.GetFileNameWithoutExtension(csvPath);
        int us = name.IndexOf('_');
        return us > 0 ? name[..us] : name;
    }

    /// <summary>Extracts the timeframe from a CSV filename ("AUDCAD_h1_...csv" → Hour1).</summary>
    private static ITimeFrame ResolveTimeFrame(string csvPath)
    {
        string name = Path.GetFileNameWithoutExtension(csvPath);
        Match m = Regex.Match(name, @"_([a-zA-Z]\d{1,2})_");
        if (m.Success)
        {
            string shortName = m.Groups[1].Value;
            TimeFrameInfo? match = TimeFrameHelper.TimeFrames.Values
                .FirstOrDefault(v => string.Equals(
                    v.TimeFrame.ShortName, shortName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.TimeFrame;
        }
        return TimeFrameHelper.Hour1;
    }
}

/// <summary>Parameters for an impulse scan (page inputs + bot defaults).</summary>
public sealed record ImpulseScanRequest(
    string? File,
    string? FromDate = null,
    string? ToDate = null,
    // ── IsSmoothImpulse (uncommented) filters ──
    double MinSizePercent = 0.13,
    double MaxOverlapseLengthPercent = 35,
    double MaxDistance = 35,
    double AreaPercent = 35,
    int BarsCount = 15,
    // ── entry / outcome geometry ──
    double EnterRatio = 0.35,
    double TakeRatio = 1.6,
    // ── advanced (defaulted) ──
    int Period = 20,
    double BreakEvenRatio = 0,
    double MaxZigzagPercent = 20,
    double HeterogeneityMax = 20);

/// <summary>A single detected impulse setup with its resolved TP/SL outcome.</summary>
public sealed class ImpulseSetupDto
{
    public int Id { get; set; }
    public bool IsUp { get; set; }
    public bool IsLimit { get; set; }

    public int ImpulseStartBar { get; set; }
    public double ImpulseStartPrice { get; set; }
    public int ImpulseEndBar { get; set; }
    public double ImpulseEndPrice { get; set; }

    public int EntryBar { get; set; }
    public double EntryPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }

    public int ViewStartBar { get; set; }
    public string EntryTime { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    /// <summary>Ratio-zigzag score of the impulse movement (0..1).</summary>
    public double RatioZigzag { get; set; }

    /// <summary>Envelope-area score of the impulse movement (0..1).</summary>
    public double Area { get; set; }

    /// <summary>"TP", "SL", "CANCELED" or "NONE".</summary>
    public string Outcome { get; set; } = "NONE";
    public int OutcomeBar { get; set; }
    public double OutcomePrice { get; set; }
}

/// <summary>Full result of a scan: metadata, candles and the detected setups.</summary>
public sealed class ImpulseScanResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public int PriceDecimals { get; set; } = 5;
    public int StartBar { get; set; }
    public int EndBar { get; set; }
    public IReadOnlyList<CandleBar> Candles { get; set; } = Array.Empty<CandleBar>();
    public IReadOnlyList<ImpulseSetupDto> Setups { get; set; } = Array.Empty<ImpulseSetupDto>();

    // Temporary diagnostics.
    public int EnterCount { get; set; }
    public int ResolvedCount { get; set; }
    public int BarCount { get; set; }
}
