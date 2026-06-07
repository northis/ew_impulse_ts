using TradeKit.Core.Common;
using TradeKit.ReplayViewer.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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

app.Run();

// ── helpers ──────────────────────────────────────────
void EnsureProvider(string csvPath)
{
    if (loadedCsvPath == csvPath && loadedProvider != null && engine != null)
        return;

    // TimeFrame / Symbol — use synthetic defaults
    var tf = new TimeFrameBase("H1", "H1");
    loadedProvider = new ReplayBarsProvider(tf);
    engine = new ReplayEngine(loadedProvider);
    loadedCsvPath = csvPath;
}

// ── request DTO ──────────────────────────────────────
record ReplayRequest(
    string? File,
    int StartBar = 0,
    int EndBar = -1,
    int DeadDepth = 1,
    string? FromDate = null,
    string? ToDate = null);
