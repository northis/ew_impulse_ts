// ── Diagonal training game ───────────────────────────────────────────────
// Loads detected contracting-diagonal setups from the backend and quizzes the
// user: show the chart up to the entry (0-1-2-3-4-5 diagonal whose wave 5 just
// broke wave 3), let them decide enter / skip, then reveal the TP/SL outcome
// and score the decision. See DIAGONAL.md.

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
    autoscaleInfoProvider: () => visiblePriceRange
        ? { priceRange: { minValue: visiblePriceRange.min, maxValue: visiblePriceRange.max } }
        : null
});

const RIGHT_PADDING_BARS = 8;
let barTimeMap = new Map();   // absolute barIndex → unix seconds
let priceDecimals = 5;
let overlays = [];            // line series + price lines drawn per setup
let visiblePriceRange = null; // { min, max } of currently shown candles

window.addEventListener('resize', () => {
    chart.applyOptions({});
});

// ── State ──
let scan = null;              // last scan result
let candleByBar = new Map();  // absolute barIndex → candle DTO
let currentFileInfo = null;   // { firstBarTime, lastBarTime, barCount }
let idx = -1;                 // current setup index
let phase = 'idle';           // 'question' | 'answer'
const decided = new Set();
const stats = { total: 0, win: 0, loss: 0, goodSkip: 0, missed: 0 };

// ── DOM ──
const $ = id => document.getElementById(id);

// ── Helpers ──
function fmtPrice(v) { return v == null ? '—' : Number(v).toFixed(priceDecimals); }

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
    let lo = Infinity, hi = -Infinity;
    for (const d of data) { if (d.low < lo) lo = d.low; if (d.high > hi) hi = d.high; }
    if (isFinite(lo) && isFinite(hi)) {
        const pad = (hi - lo) * 0.08 || Math.abs(hi) * 0.001;
        visiblePriceRange = { min: lo - pad, max: hi + pad };
    } else {
        visiblePriceRange = null;
    }
    candleSeries.setData(data);
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
    if (t1 == null || t2 == null || t1 === t2) return;
    const s = chart.addLineSeries({
        color, lineWidth: width, lineStyle: style,
        lastValueVisible: false, priceLineVisible: false,
        autoscaleInfoProvider: () => null
    });
    const pts = t1 < t2
        ? [{ time: t1, value: p1 }, { time: t2, value: p2 }]
        : [{ time: t2, value: p2 }, { time: t1, value: p1 }];
    s.setData(pts);
    overlays.push({ kind: 'line', ref: s });
}

function addPriceLine(price, color, title) {
    const pl = candleSeries.createPriceLine({
        price, color, lineWidth: 1, lineStyle: 2, axisLabelVisible: true, title
    });
    overlays.push({ kind: 'price', ref: pl });
}

// ── Drawing a setup ──
const WAVE_LABELS = ['0', '1', '2', '3', '4', '5'];

function drawSetupBase(s) {
    clearOverlays();
    const wp = s.wavePoints || [];

    // Diagonal zigzag 0-1-2-3-4-5
    for (let i = 0; i + 1 < wp.length; i++)
        addLine(wp[i].bar, wp[i].price, wp[i + 1].bar, wp[i + 1].price, '#b78cf2', 2, 0);

    // Converging trendlines: 1-3 (extended to the wave-5 bar) and 2-4
    if (wp.length >= 6) {
        const P1 = wp[1], P2 = wp[2], P3 = wp[3], P4 = wp[4], P5 = wp[5];
        const s13 = (P3.price - P1.price) / Math.max(1, P3.bar - P1.bar);
        addLine(P1.bar, P1.price, P5.bar, P1.price + s13 * (P5.bar - P1.bar), '#e0b84a', 1, 2);
        const s24 = (P4.price - P2.price) / Math.max(1, P4.bar - P2.bar);
        addLine(P2.bar, P2.price, P5.bar, P2.price + s24 * (P5.bar - P2.bar), '#e0b84a', 1, 2);

        // V(3) — the level whose break triggers the signal
        addPriceLine(P3.price, '#7f8fa6', 'W3');
    }

    // Wave labels as markers
    const markers = [];
    for (let i = 0; i < wp.length; i++) {
        const t = barTimeFor(wp[i].bar);
        if (t == null) continue;
        const isHigh = i > 0
            ? wp[i].price > wp[i - 1].price
            : (wp.length > 1 && wp[0].price > wp[1].price);
        markers.push({
            time: t,
            position: isHigh ? 'aboveBar' : 'belowBar',
            color: '#b78cf2',
            shape: 'circle',
            text: WAVE_LABELS[i] || String(i),
            size: 0
        });
    }
    markers.sort((a, b) => a.time - b.time);
    candleSeries.setMarkers(markers);

    // Levels
    addPriceLine(s.takeProfit, '#6AA84F', 'TP');
    addPriceLine(s.entryPrice, '#d1d5db', 'Вход');
    addPriceLine(s.stopLoss, '#ef5350', 'SL');
}

/** Left edge of the view, extended by the same span currently shown (extra context). */
function viewLeftBar(s) {
    const span = Math.max(0, s.entryBar - s.viewStartBar);
    return s.viewStartBar - span;
}

/** Parses "DIAGONAL_CONTRACTING w5/w3=0.84 rr=1.50" from the finder comment. */
function parseComment(comment) {
    const text = comment || '';
    const model = (text.match(/^(\S+)/) || [])[1];
    const w5 = (text.match(/w5\/w3=([\d.]+)/) || [])[1];
    return {
        model: model ? model.replace('DIAGONAL_', '').toLowerCase() : '—',
        w5: w5 || '—'
    };
}

function renderQuestion(s) {
    phase = 'question';
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.entryBar);

    $('setupCounter').textContent = `${idx + 1} / ${scan.setups.length}`;
    const badge = $('dirBadge');
    // isUp here is the direction of the TRADE (against the diagonal).
    badge.textContent = s.isUp ? '▲ ВВЕРХ' : '▼ ВНИЗ';
    badge.className = 'dir-badge ' + (s.isUp ? 'up' : 'down');

    const rr = Math.abs(s.takeProfit - s.entryPrice) / Math.abs(s.entryPrice - s.stopLoss);
    const info = parseComment(s.comment);
    $('qTp').textContent = fmtPrice(s.takeProfit);
    $('qEntry').textContent = fmtPrice(s.entryPrice);
    $('qSl').textContent = fmtPrice(s.stopLoss);
    $('qRr').textContent = isFinite(rr) ? rr.toFixed(2) : '—';
    $('qModel').textContent = info.model;
    $('qW5').textContent = info.w5;

    $('phaseQuestion').style.display = '';
    $('phaseAnswer').style.display = 'none';

    updateProgress(s.entryBar, s.entryTime);
}

function renderAnswer(s, decision) {
    phase = 'answer';
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.outcomeBar);

    // Outcome marker (replaces the wave-label markers set by drawSetupBase)
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

$('btnFit').addEventListener('click', () => {
    try { chart.timeScale().fitContent(); } catch (_) { /* ignore */ }
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
        period: numVal('pPeriod', 0),
        minSizePercent: numVal('pMinSize', 0.1),
        barsCount: numVal('pBars', 10),
        takeProfitRatio: numVal('pRr', 1.0),
        requireWave5Ratio: $('pW5Ratio').checked,
        requireWave4Ratio: $('pW4Ratio').checked,
        requireWave2Shorter: $('pW2Time').checked,
        requireInitialDiagonal: $('pInitDiagonal').checked,
        takeProfitAtRetrace: $('pTpRetrace').checked,
        minConvergence: parseFloat($('pConverge').value) || 0,
        maxConvergence: parseFloat($('pMaxConverge').value) || 0,
        requireInsideWedge: $('pInside').checked,
        maxSpillAreaRatio: parseFloat($('pSpill').value) || 0.005,
        minWave3Penetration: numVal('pPen', 0.03),
        maxWaveDurationRatio: numVal('pDur', 8.0),
        minWave2Retrace: numVal('pW2Retrace', 0),
        maxWave5SpillRatio: numVal('pW5Spill', 0)
    };

    try {
        const res = await fetch('/api/diagonal/scan', {
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

    barTimeMap = new Map();
    candleByBar = new Map();
    for (const c of scan.candles) {
        barTimeMap.set(c.barIndex, Math.floor(new Date(c.time).getTime() / 1000));
        candleByBar.set(c.barIndex, c);
    }

    idx = -1;
    phase = 'idle';
    decided.clear();
    stats.total = stats.win = stats.loss = stats.goodSkip = stats.missed = 0;
    renderStats();

    const n = scan.setups.length;
    const periodInfo = `период ${scan.usedPeriod} (bps ${(scan.medianBarBps || 0).toFixed(1)})`;
    $('status').textContent =
        `${scan.symbol} ${scan.timeframe}: сетапов — ${n} ` +
        `(найдено диагоналей ${scan.enterCount}, c исходом TP/SL ${scan.resolvedCount}) · ${periodInfo}`;

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
    const rnd = first + Math.random() * (last - first);
    const iso = new Date(rnd).toISOString();
    $('inpFromDate').value = isoToDateValue(iso);
    $('inpToDate').value = isoToDateValue(currentFileInfo.lastBarTime);
    $('status').textContent = `Старт: ${isoToDateValue(iso)} — нажмите «Сканировать»`;
}

$('selFile').addEventListener('change', onFileSelected);
$('btnRandom').addEventListener('click', randomStartDate);

loadFiles();
