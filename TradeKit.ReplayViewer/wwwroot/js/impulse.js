// ── Impulse training game ────────────────────────────────────────────────
// Loads detected entry-impulse setups from the backend and quizzes the user:
// show the chart up to the entry (impulse + correction), let them decide
// enter / skip, then reveal the TP/SL outcome and score the decision.

// ── Chart setup ──
const chart = LightweightCharts.createChart(
    document.getElementById('chartContainer'), {
    layout: { background: { color: '#1a1d23' }, textColor: '#8892a0' },
    grid: { vertLines: { color: '#272b33' }, horzLines: { color: '#272b33' } },
    crosshair: { mode: LightweightCharts.CrosshairMode.Normal },
    rightPriceScale: { borderColor: '#333840', autoScale: true },
    timeScale: { borderColor: '#333840', timeVisible: true, secondsVisible: false }
});

const candleSeries = chart.addCandlestickSeries({
    upColor: '#26a69a', downColor: '#ef5350',
    borderUpColor: '#26a69a', borderDownColor: '#ef5350',
    wickUpColor: '#26a69a', wickDownColor: '#ef5350',
    // Keep the visible candles vertically in view regardless of overlays/fib lines.
    autoscaleInfoProvider: () => visiblePriceRange
        ? { priceRange: { minValue: visiblePriceRange.min, maxValue: visiblePriceRange.max } }
        : null
});

const RIGHT_PADDING_BARS = 8;
let barTimeMap = new Map();   // absolute barIndex → unix seconds
let priceDecimals = 5;
let overlays = [];            // line series + price lines drawn per setup
let visiblePriceRange = null; // { min, max } of currently shown candles (drives vertical autoscale)

window.addEventListener('resize', () => {
    chart.applyOptions({}); // lightweight-charts auto-resizes its container
});

// ── State ──
let scan = null;              // last scan result
let candleByBar = new Map();  // absolute barIndex → candle DTO
let currentFileInfo = null;   // { firstBarTime, lastBarTime, barCount } of selected filelet idx = -1;                 // current setup index
let phase = 'idle';          // 'question' | 'answer'
let currentDecision = null;   // last decision in answer phase ('enter' | 'skip')
const decided = new Set();    // setup ids already scored
const stats = { total: 0, win: 0, loss: 0, goodSkip: 0, missed: 0 };

// ── DOM ──
const $ = id => document.getElementById(id);

// ── Helpers ──
function fmtPrice(v) { return v == null ? '—' : Number(v).toFixed(priceDecimals); }
function fmtPct(v) { return v == null ? '—' : (Number(v) * 100).toFixed(1) + '%'; }

function barTimeFor(barIndex) {
    const t = barTimeMap.get(barIndex);
    return t === undefined ? null : t;
}

function setPricePrecision(d) {
    const dd = Math.max(0, Math.min(8, d | 0));
    candleSeries.applyOptions({
        priceFormat: { type: 'price', precision: dd, minMove: Math.pow(10, -dd) }
    });
}

function clearOverlays() {
    for (const o of overlays) {
        if (o.kind === 'line') chart.removeSeries(o.ref);
        else candleSeries.removePriceLine(o.ref);
    }
    overlays = [];
    candleSeries.setMarkers([]);
}

/** Push candles for absolute bar range [fromBar..toBar] into the chart. */
function showCandles(fromBar, toBar) {
    const data = [];
    for (let b = fromBar; b <= toBar; b++) {
        const c = candleByBar.get(b);
        if (!c) continue;
        const t = barTimeFor(b);
        if (t == null) continue;
        data.push({ time: t, open: c.open, high: c.high, low: c.low, close: c.close });
    }
    data.sort((a, b) => a.time - b.time);
    // Compute the vertical range of the visible candles so the price scale
    // always scrolls to fit them (with a small margin).
    let lo = Infinity, hi = -Infinity;
    for (const d of data) { if (d.low < lo) lo = d.low; if (d.high > hi) hi = d.high; }
    if (isFinite(lo) && isFinite(hi)) {
        const pad = (hi - lo) * 0.08 || Math.abs(hi) * 0.001;
        visiblePriceRange = { min: lo - pad, max: hi + pad };
    } else {
        visiblePriceRange = null;
    }
    candleSeries.setData(data);
    // Re-enable vertical auto-scaling: dragging/zooming the price axis turns it
    // off, which otherwise leaves later charts off-screen (empty space).
    try { candleSeries.priceScale().applyOptions({ autoScale: true }); } catch (_) { /* ignore */ }
    if (data.length > 0) {
        try {
            chart.timeScale().setVisibleLogicalRange(
                { from: 0, to: data.length - 1 + RIGHT_PADDING_BARS });
        } catch (_) { chart.timeScale().fitContent(); }
    }
}

function addLine(bar1, p1, bar2, p2, color, width, style) {
    const t1 = barTimeFor(bar1), t2 = barTimeFor(bar2);
    if (t1 == null || t2 == null) return;
    const s = chart.addLineSeries({
        color, lineWidth: width, lineStyle: style,
        lastValueVisible: false, priceLineVisible: false,
        autoscaleInfoProvider: () => null   // don't let overlay lines push the price scale
    });
    s.setData([{ time: t1, value: p1 }, { time: t2, value: p2 }]);
    overlays.push({ kind: 'line', ref: s });
}

function addPriceLine(price, color, title) {
    const pl = candleSeries.createPriceLine({
        price, color, lineWidth: 1, lineStyle: 2, axisLabelVisible: true, title
    });
    overlays.push({ kind: 'price', ref: pl });
}

// ── Drawing a setup ──
function drawSetupBase(s) {
    clearOverlays();
    const dir = s.isUp ? '#26a69a' : '#ef5350';
    // Impulse leg (Wave0 → Wave5)
    addLine(s.impulseStartBar, s.impulseStartPrice, s.impulseEndBar, s.impulseEndPrice, dir, 2, 0);
    // Correction leg (Wave5 → entry)
    addLine(s.impulseEndBar, s.impulseEndPrice, s.entryBar, s.entryPrice, '#9aa4b2', 1, 2);
    // Levels
    addPriceLine(s.takeProfit, '#6AA84F', 'TP');
    addPriceLine(s.entryPrice, '#d1d5db', 'Вход');
    addPriceLine(s.stopLoss, '#ef5350', 'SL');
    // Optional Fibonacci levels (retracement + extension of the impulse leg)
    if ($('chkFib').checked) drawFib(s);
}

/** Fibonacci retracement + extension levels measured on the impulse leg. */
const FIB_RETRACEMENT = [0.236, 0.382, 0.5, 0.618, 0.786];
const FIB_EXTENSION = [1.272, 1.618, 2.618];
function drawFib(s) {
    const a = s.impulseStartPrice;     // Wave0
    const b = s.impulseEndPrice;       // Wave5
    const diff = b - a;
    if (!isFinite(diff) || diff === 0) return;
    for (const r of FIB_RETRACEMENT)   // retrace from the impulse end back toward its start
        addPriceLine(b - diff * r, '#c98a3a', `R ${r}`);
    for (const r of FIB_EXTENSION)     // project beyond the impulse end
        addPriceLine(a + diff * r, '#7b6cd9', `E ${r}`);
}

/** Left edge of the view, extended by the same span currently shown (extra context). */
function viewLeftBar(s) {
    const span = Math.max(0, s.entryBar - s.viewStartBar);
    return s.viewStartBar - span;
}

function renderQuestion(s) {
    phase = 'question';
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.entryBar);

    $('setupCounter').textContent = `${idx + 1} / ${scan.setups.length}`;
    const badge = $('dirBadge');
    badge.textContent = s.isUp ? '▲ ВВЕРХ' : '▼ ВНИЗ';
    badge.className = 'dir-badge ' + (s.isUp ? 'up' : 'down');

    const rr = Math.abs(s.takeProfit - s.entryPrice) / Math.abs(s.entryPrice - s.stopLoss);
    $('qTp').textContent = fmtPrice(s.takeProfit);
    $('qEntry').textContent = fmtPrice(s.entryPrice);
    $('qSl').textContent = fmtPrice(s.stopLoss);
    $('qRr').textContent = isFinite(rr) ? rr.toFixed(2) : '—';
    $('qRz').textContent = fmtPct(s.ratioZigzag);
    $('qArea').textContent = fmtPct(s.area);
    $('qCorr').textContent = fmtPct(s.correctionRatio);
    $('qOverlap').textContent = fmtPct(s.overlapDegree);

    $('phaseQuestion').style.display = '';
    $('phaseAnswer').style.display = 'none';

    updateProgress(s.entryBar, s.entryTime);
}

function renderAnswer(s, decision) {
    phase = 'answer';
    currentDecision = decision;
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.outcomeBar);

    // Outcome marker
    const t = barTimeFor(s.outcomeBar);
    if (t != null) {
        const tp = s.outcome === 'TP';
        candleSeries.setMarkers([{
            time: t,
            position: tp ? 'aboveBar' : 'belowBar',
            color: tp ? '#34c77b' : '#ef6b69',
            shape: tp ? 'arrowDown' : 'arrowUp',
            text: tp ? 'TP' : 'SL'
        }]);
    }

    const correct = (decision === 'enter' && s.outcome === 'TP') ||
                    (decision === 'skip' && s.outcome === 'SL');
    const verdict = $('verdict');
    verdict.textContent = correct ? '✔ Верно' : '✘ Ошибка';
    verdict.className = 'verdict ' + (correct ? 'correct' : 'wrong');

    $('aDecision').textContent = decision === 'enter' ? 'Войти' : 'Пропустить';
    const oc = $('aOutcome');
    oc.textContent = s.outcome === 'TP' ? 'Тейк-профит' : 'Стоп-лосс';
    oc.className = s.outcome === 'TP' ? 'tp' : 'sl';

    $('phaseQuestion').style.display = 'none';
    $('phaseAnswer').style.display = '';

    if (!decided.has(s.id)) {
        decided.add(s.id);
        stats.total++;
        if (decision === 'enter') (s.outcome === 'TP' ? stats.win++ : stats.loss++);
        else (s.outcome === 'SL' ? stats.goodSkip++ : stats.missed++);
        renderStats();
    }

    updateProgress(s.outcomeBar, null);
}

function renderStats() {
    $('stTotal').textContent = stats.total;
    // Accuracy counts entries only (Войти): win / (win + loss). Skips are ignored.
    const entries = stats.win + stats.loss;
    $('stAcc').textContent = entries > 0
        ? `${Math.round(stats.win / entries * 100)}% (${stats.win}/${entries})` : '—';
    $('stWin').textContent = stats.win;
    $('stLoss').textContent = stats.loss;
    $('stGoodSkip').textContent = stats.goodSkip;
    $('stMissed').textContent = stats.missed;
}

function updateProgress(bar, isoTime) {
    if (!scan) return;
    const span = Math.max(1, scan.endBar - scan.startBar);
    const pct = Math.max(0, Math.min(100, (bar - scan.startBar) / span * 100));
    $('progress').value = pct;
    $('stepCounter').textContent = `${idx + 1} / ${scan.setups.length}`;
    if (isoTime) $('barDate').textContent = formatDateTime(isoTime);
}

function goTo(i) {
    if (!scan || i < 0 || i >= scan.setups.length) return;
    idx = i;
    renderQuestion(scan.setups[idx]);
}

// ── Decision handlers ──
$('btnEnter').addEventListener('click', () => {
    if (phase !== 'question') return;
    renderAnswer(scan.setups[idx], 'enter');
});
$('btnSkip').addEventListener('click', () => {
    if (phase !== 'question') return;
    renderAnswer(scan.setups[idx], 'skip');
});
$('btnNext').addEventListener('click', () => {
    if (idx + 1 < scan.setups.length) goTo(idx + 1);
    else $('status').textContent = 'Все сетапы пройдены';
});

// Auto-format the chart (fit all visible data) on demand.
$('btnFit').addEventListener('click', () => {
    try { chart.timeScale().fitContent(); } catch (_) { /* ignore */ }
});

// Re-draw the current setup when the Fibonacci toggle changes.
$('chkFib').addEventListener('change', () => {
    if (!scan || idx < 0) return;
    if (phase === 'answer') renderAnswer(scan.setups[idx], currentDecision);
    else renderQuestion(scan.setups[idx]);
});

// ── Scan ──
$('btnScan').addEventListener('click', runScan);

async function runScan() {
    const file = $('selFile').value;
    if (!file || file.startsWith('--')) { $('status').textContent = 'Выберите файл'; return; }

    $('btnScan').disabled = true;
    $('status').textContent = 'Сканирование...';

    const body = {
        file,
        fromDate: dateInputToIso($('inpFromDate').value),
        toDate: dateInputToIso($('inpToDate').value),
        minSizePercent: numVal('pMinSize', 0.13),
        maxOverlapseLengthPercent: numVal('pOverlap', 35),
        maxDistance: numVal('pDistance', 35),
        areaPercent: numVal('pArea', 35),
        barsCount: numVal('pBars', 15),
        maxCorrectionRatioPercent: numVal('pCorr', 50),
        enterRatio: numVal('pEnter', 0.35),
        takeRatio: numVal('pTake', 1.6),
        period: numVal('pPeriod', 20),
        breakEvenRatio: numVal('pBreakEven', 0),
        maxZigzagPercent: numVal('pZigzag', 20),
        heterogeneityMax: numVal('pHetero', 20)
    };

    try {
        const res = await fetch('/api/impulse/scan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.title || err.error || res.statusText);
        }
        scan = await res.json();
        onScanLoaded();
    } catch (e) {
        $('status').textContent = 'Ошибка: ' + e.message;
    } finally {
        $('btnScan').disabled = false;
    }
}

function onScanLoaded() {
    priceDecimals = scan.priceDecimals || 5;
    setPricePrecision(priceDecimals);

    // Build bar → time and bar → candle maps
    barTimeMap = new Map();
    candleByBar = new Map();
    for (const c of scan.candles) {
        barTimeMap.set(c.barIndex, Math.floor(new Date(c.time).getTime() / 1000));
        candleByBar.set(c.barIndex, c);
    }

    // Reset game / stats for the new scan
    idx = -1;
    phase = 'idle';
    decided.clear();
    stats.total = stats.win = stats.loss = stats.goodSkip = stats.missed = 0;
    renderStats();

    const n = scan.setups.length;
    $('status').textContent =
        `${scan.symbol} ${scan.timeframe}: сетапов — ${n} ` +
        `(найдено импульсов ${scan.enterCount}, c исходом TP/SL ${scan.resolvedCount})`;

    if (n === 0) {
        $('gameEmpty').style.display = '';
        $('gameEmpty').innerHTML = '<em>Сетапы не найдены. Смягчите параметры или расширьте диапазон.</em>';
        $('gameCard').style.display = 'none';
        candleSeries.setData([]);
        clearOverlays();
        $('stepCounter').textContent = '0 / 0';
        $('progress').value = 0;
        return;
    }

    $('gameEmpty').style.display = 'none';
    $('gameCard').style.display = '';
    goTo(0);
}

function numVal(id, dflt) {
    const v = parseFloat($(id).value);
    return isFinite(v) ? v : dflt;
}

// ── Date helpers (dd.mm.yyyy, day-first guaranteed) ──
function dateInputToIso(s) {
    s = (s || '').trim();
    if (!s) return null;
    let m = s.match(/^(\d{1,2})[.\/-](\d{1,2})[.\/-](\d{4})$/);
    if (m) {
        const [, d, mo, y] = m;
        return `${y}-${mo.padStart(2, '0')}-${d.padStart(2, '0')}T00:00:00Z`;
    }
    m = s.match(/^(\d{4})-(\d{1,2})-(\d{1,2})/);
    if (m) {
        const [, y, mo, d] = m;
        return `${y}-${mo.padStart(2, '0')}-${d.padStart(2, '0')}T00:00:00Z`;
    }
    return null;
}

/** ISO instant → "yyyy-mm-dd" value for a native <input type="date">. */
function isoToDateValue(iso) {
    const m = iso ? String(iso).match(/^(\d{4})-(\d{2})-(\d{2})/) : null;
    return m ? `${m[1]}-${m[2]}-${m[3]}` : '';
}

function formatDateTime(iso) {
    const dt = new Date(iso);
    if (isNaN(dt)) return '—';
    const p = n => String(n).padStart(2, '0');
    return `${p(dt.getUTCDate())}.${p(dt.getUTCMonth() + 1)}.${dt.getUTCFullYear()} ` +
           `${p(dt.getUTCHours())}:${p(dt.getUTCMinutes())}`;
}

// ── File list ──
async function loadFiles() {
    try {
        const res = await fetch('/api/replay/files');
        const files = await res.json();
        const sel = $('selFile');
        sel.innerHTML = '';
        if (!files.length) {
            sel.innerHTML = '<option>-- нет CSV-файлов --</option>';
            return;
        }
        for (const f of files) {
            const opt = document.createElement('option');
            opt.value = f.name;
            opt.textContent = f.name;
            sel.appendChild(opt);
        }
        onFileSelected();
    } catch (e) {
        $('status').textContent = 'Не удалось загрузить список файлов';
    }
}

async function onFileSelected() {
    const name = $('selFile').value;
    if (!name || name.startsWith('--')) return;
    try {
        const res = await fetch('/api/replay/files/' + encodeURIComponent(name));
        const info = await res.json();
        currentFileInfo = info;
        if (info.firstBarTime && info.lastBarTime) {
            const f = formatDateTime(info.firstBarTime).slice(0, 10);
            const t = formatDateTime(info.lastBarTime).slice(0, 10);
            $('rangeInfo').textContent = `${f} … ${t} (${info.barCount} баров)`;
            // Auto-fill the date pickers with the file's full range (like the markup page).
            $('inpFromDate').value = isoToDateValue(info.firstBarTime);
            $('inpToDate').value = isoToDateValue(info.lastBarTime);
            $('inpFromDate').min = $('inpToDate').min = isoToDateValue(info.firstBarTime);
            $('inpFromDate').max = $('inpToDate').max = isoToDateValue(info.lastBarTime);
        } else {
            $('rangeInfo').textContent = '';
        }
    } catch (_) { $('rangeInfo').textContent = ''; }
}

/** Jump the "from" date to a random day within the file's full range. */
function randomStartDate() {
    if (!currentFileInfo || !currentFileInfo.firstBarTime || !currentFileInfo.lastBarTime) return;
    const first = Date.parse(currentFileInfo.firstBarTime);
    const last = Date.parse(currentFileInfo.lastBarTime);
    if (isNaN(first) || isNaN(last) || last <= first) return;
    // Pick a random day in [first, last); keep "to" at the file end.
    const rnd = first + Math.random() * (last - first);
    const iso = new Date(rnd).toISOString();
    $('inpFromDate').value = isoToDateValue(iso);
    $('inpToDate').value = isoToDateValue(currentFileInfo.lastBarTime);
    $('status').textContent = `Старт: ${isoToDateValue(iso)} — нажмите «Сканировать»`;
}

$('selFile').addEventListener('change', onFileSelected);
$('btnRandom').addEventListener('click', randomStartDate);

loadFiles();
