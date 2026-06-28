using TradeKit.Core.Common;
using TradeKit.Core.Json;
using TradeKit.ReplayViewer.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// camelCase JSON for SSE so the browser client (and §17.1 schema) matches.
var SseJsonOptions = new System.Text.Json.JsonSerializerOptions
{
    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
};

// Path to data/ relative to repo root (adjustable via env var)
string dataDir = Environment.GetEnvironmentVariable("REPLAY_DATA_DIR")
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "data"));
string? loadedCsvPath = null;
ReplayBarsProvider? loadedProvider = null;
ReplayEngine? engine = null;

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

WebApplication app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API ──────────────────────────────────────────────

// List available CSV files
app.MapGet("/api/replay/files", () =>
{
    var files = ReplayEngine.ListCsvFiles(dataDir).ToList();
    return Results.Ok(files);
});

// Get file info (bar count, time range)
app.MapGet("/api/replay/files/{name}", (string name) =>
{
    string path = Path.Combine(dataDir, name);
    if (!File.Exists(path))
        return Results.NotFound(new { error = "File not found" });

    EnsureProvider(path);
    var info = engine!.GetFileInfo(path);
    return Results.Ok(info);
});

// Run replay
app.MapPost("/api/replay/run", (ReplayRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.File))
        return Results.BadRequest(new { error = "File name is required" });

    string path = Path.Combine(dataDir, req.File);
    if (!File.Exists(path))
        return Results.NotFound(new { error = "File not found" });

    try
    {
        EnsureProvider(path);

        ReplayData data;
        if (!string.IsNullOrWhiteSpace(req.FromDate) || !string.IsNullOrWhiteSpace(req.ToDate))
        {
            data = engine!.RunByDate(path, req.FromDate, req.ToDate, req.DeadDepth);
        }
        else
        {
            data = engine!.Run(path, req.StartBar, req.EndBar, req.DeadDepth);
        }
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.ToString(),
            statusCode: 500,
            title: ex.Message);
    }
});

// Run replay — streaming (SSE): each pivot frame is pushed to the client as it is generated
app.MapPost("/api/replay/stream", async (HttpContext ctx) =>
{
    var req = await ctx.Request.ReadFromJsonAsync<ReplayRequest>();
    if (req == null || string.IsNullOrWhiteSpace(req.File))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("{\"error\":\"File name is required\"}");
        return;
    }

    string path = Path.Combine(dataDir, req.File);
    if (!File.Exists(path))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("{\"error\":\"File not found\"}");
        return;
    }

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"] = "keep-alive";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering

    try
    {
        EnsureProvider(path);

        int startBar, endBar;
        if (!string.IsNullOrWhiteSpace(req.FromDate) || !string.IsNullOrWhiteSpace(req.ToDate))
        {
            engine!.InitializeForDates(path, req.FromDate, req.ToDate, deviationPercent: null);
        }
        else
        {
            engine!.Initialize(path, req.StartBar, req.EndBar, deviationPercent: null);
        }

        // Use the range the engine actually resolved (date → bar mapping + clamping),
        // so the chart shows only the selected window, not the whole CSV file.
        startBar = engine.StartBar;
        endBar = engine.EndBar;

        int deadDepth = req.DeadDepth;

        // 1. Send candles + metadata first
        var candles = engine.FullMarkup!.BarsProvider is ReplayBarsProvider rbp
            ? rbp.GetAllCandles().Skip(startBar).Take(endBar - startBar + 1).ToList()
            : new List<CandleBar>();
        var initEvent = new { type = "init", candles, startBar, endBar };
        await WriteSseEvent(ctx, initEvent);

        // 2. Stream frames one by one
        while (engine.HasMoreSteps)
        {
            var frame = engine.StepForward(deadDepth);
            if (frame != null)
            {
                var frameEvent = new { type = "frame", frame };
                await WriteSseEvent(ctx, frameEvent);
            }
        }

        // 3. Final snapshot
        var result = engine.FullMarkup!.ParseTiled();
        var snapshot = EwMarkupTreeExporter.BuildSnapshot(engine.FullMarkup, result, deadDepth);
        var doneEvent = new { type = "done", snapshot };
        await WriteSseEvent(ctx, doneEvent);
    }
    catch (Exception ex)
    {
        var errEvent = new { type = "error", message = ex.Message, detail = ex.ToString() };
        await WriteSseEvent(ctx, errEvent);
    }
});

// Interactive replay — initialise a stepping session for the selected range.
app.MapPost("/api/replay/init", (ReplayRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.File))
        return Results.BadRequest(new { error = "File name is required" });

    string path = Path.Combine(dataDir, req.File);
    if (!File.Exists(path))
        return Results.NotFound(new { error = "File not found" });

    try
    {
        EnsureProvider(path);
        if (!string.IsNullOrWhiteSpace(req.FromDate) || !string.IsNullOrWhiteSpace(req.ToDate))
            engine!.InitializeForDates(path, req.FromDate, req.ToDate, deviationPercent: null);
        else
            engine!.Initialize(path, req.StartBar, req.EndBar, deviationPercent: null);

        return Results.Ok(new
        {
            symbol = engine.Symbol,
            timeframe = engine.Timeframe,
            priceDecimals = engine.PriceDecimals,
            startBar = engine.StartBar,
            endBar = engine.EndBar,
            totalSteps = engine.TotalSteps
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), statusCode: 500, title: ex.Message);
    }
});

// Interactive replay — advance one zigzag segment and return the per-step state.
app.MapPost("/api/replay/step", (StepRequest req) =>
{
    if (engine == null || !engine.IsInitialized)
        return Results.BadRequest(new { error = "Replay not initialised. Call /api/replay/init first." });

    try
    {
        ReplayStepResult? step = engine.Step(req.DeadDepth);
        if (step == null)
            return Results.Ok(new { done = true });

        return Results.Ok(new
        {
            done = false,
            frame = step.Frame,
            snapshot = step.Snapshot,
            newCandles = step.NewCandles
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), statusCode: 500, title: ex.Message);
    }
});

// ── Impulse training game ────────────────────────────
// Scans a file/date-range for entry-impulse setups (same logic as the cTrader bot)
// and returns each setup with its eventual TP/SL outcome for the quiz UI.
var impulseScanEngine = new ImpulseScanEngine();
app.MapPost("/api/impulse/scan", (ImpulseScanRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.File))
        return Results.BadRequest(new { error = "File name is required" });

    string path = Path.Combine(dataDir, req.File);
    if (!File.Exists(path))
        return Results.NotFound(new { error = "File not found" });

    try
    {
        ImpulseScanResult result = impulseScanEngine.Scan(path, req);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), statusCode: 500, title: ex.Message);
    }
});

app.Run();

// ── helpers ──────────────────────────────────────────
void EnsureProvider(string csvPath)
{
    if (loadedCsvPath == csvPath && loadedProvider != null && engine != null)
        return;

    ITimeFrame tf = ResolveTimeFrame(csvPath);
    loadedProvider = new ReplayBarsProvider(tf);
    engine = new ReplayEngine(loadedProvider);
    loadedCsvPath = csvPath;
}

async Task WriteSseEvent(HttpContext ctx, object data)
{
    string json = System.Text.Json.JsonSerializer.Serialize(data, SseJsonOptions);
    await ctx.Response.WriteAsync($"data: {json}\n\n");
    await ctx.Response.Body.FlushAsync();
}

// Extracts the timeframe shortName from a CSV filename like
// "AUDCAD_h1_2019-...csv" → looks up TimeFrameHelper for matching ITimeFrame.
// Falls back to Hour1.
static ITimeFrame ResolveTimeFrame(string csvPath)
{
    string name = Path.GetFileNameWithoutExtension(csvPath);
    // Pattern: SYMBOL_TF_...  e.g. "AUDCAD_h1_2019-..." → "h1"
    var m = System.Text.RegularExpressions.Regex.Match(name, @"_([a-zA-Z]\d{1,2})_");
    if (m.Success)
    {
        string shortName = m.Groups[1].Value;
        var match = TimeFrameHelper.TimeFrames.Values
            .FirstOrDefault(v => string.Equals(
                v.TimeFrame.ShortName, shortName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return match.TimeFrame;
    }
    // Fallback — try "Hour" by name
    return TimeFrameHelper.Hour1;
}

// ── request DTO ──────────────────────────────────────
record ReplayRequest(
    string? File,
    int StartBar = 0,
    int EndBar = -1,
    int DeadDepth = 1,
    string? FromDate = null,
    string? ToDate = null);

record StepRequest(int DeadDepth = 1);
