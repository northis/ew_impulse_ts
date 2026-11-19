// ── Diagonal → reversal impulse (A-B-C) training game ─────────────────
// Loads detected setups from the backend: a completed 0-1-2-3-4-5 diagonal,
// an impulse A AGAINST the diagonal, a correction B of EnterRatio of A, and
// the trade of the second impulse C in the direction of A. The stop is behind
// the start of A (= the diagonal end); the targets are 1.0 (end of A) and 1.272.

// ── Chart setup ──
const chart = LightweightCharts.createChart(
    document.getElementById('chartContainer'), {
    autoSize: true,
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
let baseMarkers = [];         // wave/A-B-C markers of the current setup

window.addEventListener('resize', () => {
    chart.applyOptions({});
});

// ── State ──
let scan = null;              // last scan result
let scanTimer = null;         // elapsed-time ticker for long scans
let candleByBar = new Map();  // absolute barIndex → candle DTO
let currentFileInfo = null;   // { firstBarTime, lastBarTime, barCount }
let allSetups = [];           // every setup, including no-fills
let idx = -1;                 // index into allSetups
let phase = 'idle';           // 'question' | 'answer' | 'nofill'
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
    const tradeUp = !s.isUpDiagonal;

    // Diagonal zigzag 0-1-2-3-4-5 (context only)
    for (let i = 0; i + 1 < wp.length; i++)
        addLine(wp[i].bar, wp[i].price, wp[i + 1].bar, wp[i + 1].price, '#b78cf2', 2, 0);

    // Converging trendlines 1-3 and 2-4
    if (wp.length >= 6) {
        const P1 = wp[1], P2 = wp[2], P3 = wp[3], P4 = wp[4], P5 = wp[5];
        const s13 = (P3.price - P1.price) / Math.max(1, P3.bar - P1.bar);
        addLine(P1.bar, P1.price, P5.bar, P1.price + s13 * (P5.bar - P1.bar), '#e0b84a', 1, 2);
        const s24 = (P4.price - P2.price) / Math.max(1, P4.bar - P2.bar);
        addLine(P2.bar, P2.price, P5.bar, P2.price + s24 * (P5.bar - P2.bar), '#e0b84a', 1, 2);
    }

    // Impulse A against the diagonal, from the diagonal end to the A extreme.
    if (s.aStartBar >= 0 && s.aEndBar >= 0)
        addLine(s.aStartBar, s.aStartPrice, s.aEndBar, s.aEndPrice, '#4aa3e0', 3, 0);

    // Correction B: from the A extreme to the correction extreme C.
    if (s.aEndBar >= 0 && s.bEndBar >= 0) {
        addLine(s.aEndBar, s.aEndPrice, s.bEndBar, s.bEndPrice, '#e0b84a', 2, 0);
    } else if (s.entryBar >= 0 && s.aEndBar >= 0) {
        addLine(s.aEndBar, s.aEndPrice, s.entryBar, s.entryPrice, '#e0b84a', 2, 0);
    }

    // Second impulse C: from the correction extreme through the end of A.
    if (s.bEndBar >= 0) {
        if (s.workedBar >= 0 && s.workedBar > s.bEndBar) {
            addLine(s.bEndBar, s.bEndPrice, s.workedBar, s.workedPrice,
                '#ef6b69', 2, 0);
        } else if (s.outcomeBar > s.bEndBar) {
            addLine(s.bEndBar, s.bEndPrice, s.outcomeBar, s.outcomePrice,
                '#ef6b69', 2, 2);
        }
    }

    // The diagonal end may be beyond the signal's P5; mark that extension.
    if (wp.length >= 6 && s.aStartBar >= 0) {
        const P5 = wp[5];
        if (s.aStartBar !== P5.bar || Math.abs(s.aStartPrice - P5.price) > 1e-12)
            addLine(P5.bar, P5.price, s.aStartBar, s.aStartPrice, '#8a6fd0', 1, 2);
    }

    // Wave labels and the A-B-C annotations.
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

    // The running wave-5 end may lie beyond the signal's P5: label that point as the
    // diagonal's 5, not as the impulse A.
    const p5 = wp.length >= 6 ? wp[5] : null;
    if (s.aStartBar >= 0 &&
        (!p5 || s.aStartBar !== p5.bar ||
         Math.abs(s.aStartPrice - p5.price) > 1e-12)) {
        const t = barTimeFor(s.aStartBar);
        if (t != null) {
            markers.push({
                time: t,
                position: s.isUpDiagonal ? 'aboveBar' : 'belowBar',
                color: '#b78cf2',
                shape: 'circle',
                text: '5',
                size: 0
            });
        }
    }

    // Wave labels are placed at the END of each wave: A at the impulse extreme,
    // B at the correction extreme, C at the second impulse end.
    const brokeP0 = s.noFillReason === 'impulseBreaksP0';
    if (s.aEndBar >= 0) {
        const t = barTimeFor(s.aEndBar);
        if (t != null) {
            markers.push({
                time: t,
                position: tradeUp ? 'aboveBar' : 'belowBar',
                color: brokeP0 ? '#ef6b69' : '#4aa3e0',
                shape: 'circle',
                text: brokeP0 ? 'A!P0' : 'A',
                size: 0
            });
        }
    }

    if (s.bEndBar >= 0) {
        const t = barTimeFor(s.bEndBar);
        if (t != null) {
            markers.push({
                time: t,
                position: tradeUp ? 'belowBar' : 'aboveBar',
                color: '#e0b84a',
                shape: 'circle',
                text: 'B',
                size: 0
            });
        }
    }

    if (s.workedBar >= 0) {
        const t = barTimeFor(s.workedBar);
        if (t != null) {
            markers.push({
                time: t,
                position: tradeUp ? 'aboveBar' : 'belowBar',
                color: '#ef6b69',
                shape: 'circle',
                text: 'C',
                size: 0
            });
        }
    }

    if (s.entryBar >= 0) {
        const t = barTimeFor(s.entryBar);
        if (t != null) {
            markers.push({
                time: t,
                position: tradeUp ? 'belowBar' : 'aboveBar',
                color: '#d1d5db',
                shape: 'arrowUp',
                text: 'вход',
                size: 0
            });
        }
    }

    if (s.diagonalBrokenBar >= 0) {
        const t = barTimeFor(s.diagonalBrokenBar);
        if (t != null) {
            markers.push({
                time: t,
                position: s.isUpDiagonal ? 'aboveBar' : 'belowBar',
                color: '#ef6b69',
                shape: 'square',
                text: 'диагональ сломана',
                size: 0
            });
        }
    }

    markers.sort((a, b) => a.time - b.time);
    baseMarkers = markers;
    candleSeries.setMarkers(markers);

    // Levels
    if (brokeP0 && wp.length >= 6)
        addPriceLine(wp[0].price, '#ef6b69', 'P0');

    if (s.entryBar >= 0) {
        addPriceLine(s.takeProfit1, '#6AA84F', 'TP1');
        if (s.takeProfit2 && Math.abs(s.takeProfit2 - s.takeProfit1) > 1e-12)
            addPriceLine(s.takeProfit2, '#3f7f4f', 'TP2');
        addPriceLine(s.entryPrice, '#d1d5db', 'Вход');
        addPriceLine(s.stopLoss, '#ef5350', 'SL');
    }
}

/** Left edge of the view, extended by the same span currently shown. */
function viewLeftBar(s) {
    const waveStart = (s.wavePoints && s.wavePoints.length)
        ? s.wavePoints[0].bar : s.signalBar;
    const refBar = s.entryBar >= 0 ? s.entryBar
        : (s.aEndBar >= 0 ? s.aEndBar : s.signalBar);
    const span = Math.max(0, refBar - waveStart);
    return Math.max(0, waveStart - span);
}

function renderGeometry(s) {
    const d = s.diagonalHeight || 0;
    const a = s.aHeight || 0;
    const entry = s.entryPrice > 0 ? s.entryPrice : s.aStartPrice;
    $('gD').textContent = entry > 1e-12 ? (d / entry * 100).toFixed(2) + ' %' : '—';
    $('gARatio').textContent = d > 1e-12 ? (a / d).toFixed(3) : '—';
    $('gASize').textContent = entry > 1e-12 ? (a / entry * 100).toFixed(2) + ' %' : '—';
    $('gDepth').textContent = s.bEndBar >= 0
        ? (s.bDepth * 100).toFixed(1) + ' %' : '—';
    $('gEntryLevel').textContent = (numVal('pEnterRatio', 0.5) * 100).toFixed(1) + ' %';
    $('gBarsA').textContent = s.aStartBar >= 0 && s.aEndBar >= 0
        ? String(s.aEndBar - s.aStartBar) : '—';
    $('gFill').textContent = s.entryBar >= 0 ? String(s.barsToFill) : '—';
    $('gTarget').textContent =
        `${numVal('pTarget1', 1.0).toFixed(3)} A` +
        ($('pLadder').value === '1' ? ` / ${numVal('pTarget2', 1.272).toFixed(3)} A` : '');
    $('gInitialMode').textContent = $('pInitialMode').value;
    $('gPeriod').textContent = scan && scan.usedPeriod != null ? scan.usedPeriod : '—';
    $('gImpulsePeriod').textContent = scan && scan.impulsePeriod != null ? scan.impulsePeriod : '—';
}

function renderQuestion(s) {
    if (s.entryBar < 0) {
        renderNoFill(s);
        return;
    }

    phase = 'question';
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.entryBar);

    $('setupCounter').textContent = `${idx + 1} / ${allSetups.length}`;
    const badge = $('dirBadge');
    const tradeUp = !s.isUpDiagonal;
    badge.textContent = tradeUp ? '▲ ЛОНГ (против диагонали)' : '▼ ШОРТ (против диагонали)';
    badge.className = 'dir-badge ' + (tradeUp ? 'up' : 'down');

    $('qTp1').textContent = fmtPrice(s.takeProfit1);
    $('qTp2').textContent = fmtPrice(s.takeProfit2);
    $('qEntry').textContent = fmtPrice(s.entryPrice);
    $('qSl').textContent = fmtPrice(s.stopLoss);
    $('qRr').textContent = isFinite(s.plannedRr) ? s.plannedRr.toFixed(2) : '—';
    document.querySelectorAll('.tp2-row').forEach(
        row => row.style.display = (s.takeProfit2 && Math.abs(s.takeProfit2 - s.takeProfit1) > 1e-12) ? '' : 'none');
    renderGeometry(s);

    $('phaseQuestion').style.display = '';
    $('phaseAnswer').style.display = 'none';
    $('btnEnter').disabled = false;
    $('btnSkip').disabled = false;
    updateProgress(s.entryBar, s.signalTime);
}

function renderNoFill(s) {
    phase = 'nofill';
    drawSetupBase(s);
    const lastBar = s.diagonalBrokenBar >= 0 ? s.diagonalBrokenBar
        : (s.workedBar >= 0 ? s.workedBar
        : (s.aEndBar >= 0 ? s.aEndBar : s.signalBar));
    showCandles(viewLeftBar(s), lastBar);

    $('setupCounter').textContent = `${idx + 1} / ${allSetups.length}`;
    const badge = $('dirBadge');
    const tradeUp = !s.isUpDiagonal;
    badge.textContent = tradeUp ? '▲ ЛОНГ (против диагонали)' : '▼ ШОРТ (против диагонали)';
    badge.className = 'dir-badge ' + (tradeUp ? 'up' : 'down');

    $('qTp1').textContent = '—';
    $('qTp2').textContent = '—';
    $('qEntry').textContent = '—';
    $('qSl').textContent = '—';
    $('qRr').textContent = '—';
    renderGeometry(s);

    $('phaseQuestion').style.display = 'none';
    $('phaseAnswer').style.display = '';
    $('btnEnter').disabled = true;
    $('btnSkip').disabled = true;

    const level = (numVal('pEnterRatio', 0.5) * 100).toFixed(1);
    if (s.diagonalBrokenBar >= 0) {
        $('verdict').textContent =
            'Диагональ сломана: волна 5 вышла за |W3| — сетап невалиден';
        $('aOutcome').textContent = 'Диагональ сломана';
    } else if (s.noFillReason === 'impulseBreaksP0') {
        $('verdict').textContent =
            'Импульс A пробил начало диагонали P0 — импульс уже не заходной, сетап невалиден';
        $('aOutcome').textContent = 'A пробил P0';
    } else if (s.setupWorked) {
        const depth = ((s.bDepth || 0) * 100).toFixed(1);
        $('verdict').textContent =
            `Пропуск: откат B ${depth} % < ${level} %, ` +
            `затем C пробила конец A — сетап отработал без входа`;
        $('aOutcome').textContent = 'Сетап отработал (пропуск)';
    } else {
        $('verdict').textContent = `Нет входа: ${s.noFillReason || 'no fill'}`;
        $('aOutcome').textContent = 'Нет входа';
    }
    $('verdict').className = 'verdict wrong';
    $('aDecision').textContent = '—';
    $('aOutcome').className = '';
    $('aNetR').textContent = '—';
    $('aMfeMae').textContent = '—';
    updateProgress(lastBar, s.signalTime);
}

function renderAnswer(s, decision) {
    if (s.entryBar < 0) {
        renderNoFill(s);
        return;
    }

    phase = 'answer';
    drawSetupBase(s);
    showCandles(viewLeftBar(s), s.outcomeBar >= 0 ? s.outcomeBar : s.entryBar);

    const t = barTimeFor(s.outcomeBar);
    if (t != null) {
        const isTp = s.outcome === 'TP';
        const isSl = s.outcome === 'SL';
        const markers = baseMarkers.slice();
        markers.push({
            time: t,
            position: isTp ? 'aboveBar' : 'belowBar',
            color: isTp ? '#34c77b' : isSl ? '#ef6b69' : '#e0b84a',
            shape: isTp ? 'arrowDown' : 'arrowUp',
            text: isTp ? (s.target2Hit ? 'TP2' : 'TP1') : isSl ? 'SL' : s.outcome
        });
        markers.sort((a, b) => a.time - b.time);
        candleSeries.setMarkers(markers);
    }

    const correct = decision === 'enter' ? s.netR > 0 : s.netR <= 0;
    const verdict = $('verdict');
    verdict.textContent = correct ? '✔ Верно' : '✘ Ошибка';
    verdict.className = 'verdict ' + (correct ? 'correct' : 'wrong');

    $('aDecision').textContent = decision === 'enter' ? 'Войти' : 'Пропустить';
    const oc = $('aOutcome');
    const isBreakeven = s.outcome === 'SL' && s.netR > 0;
    oc.textContent = s.outcome === 'TP'
        ? (s.target2Hit ? 'Тейк-профит TP2 (127 %)' : 'Тейк-профит TP1 (100 %)')
        : isBreakeven ? 'TP1 взят, остаток в безубытке'
        : s.outcome === 'SL' ? 'Стоп-лосс'
        : s.outcome === 'TIME' ? 'Время'
        : s.outcome === 'CENSOR' ? 'Нет данных'
        : s.outcome;
    oc.className = s.outcome === 'TP' ? 'tp' : s.outcome === 'SL' ? 'sl' : '';
    $('aNetR').textContent = isFinite(s.netR) ? s.netR.toFixed(3) + 'R' : '—';
    $('aMfeMae').textContent = `${(s.mfeR || 0).toFixed(2)} / ${(s.maeR || 0).toFixed(2)}R`;

    $('phaseQuestion').style.display = 'none';
    $('phaseAnswer').style.display = '';

    if (!decided.has(s.id)) {
        decided.add(s.id);
        stats.total++;
        if (decision === 'enter') (s.netR > 0 ? stats.win++ : stats.loss++);
        else (s.netR <= 0 ? stats.goodSkip++ : stats.missed++);
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
    if (scan) {
        const winRate = scan.resolvedCount > 0
            ? Math.round(scan.winCount / scan.resolvedCount * 100) : 0;
        $('stScan').textContent =
            `${scan.filledCount}/${scan.signalCount} fill · ` +
            `${scan.skippedCount || 0} skip · ` +
            `${scan.impulseBrokenCount || 0} A>P0 · ` +
            `${scan.brokenCount || 0} broken · ` +
            `${scan.resolvedCount} res · ${winRate}% · ${(scan.avgNetR || 0).toFixed(3)}R`;
    } else {
        $('stScan').textContent = '—';
    }
}

function updateProgress(bar, isoTime) {
    if (!scan) return;
    const span = Math.max(1, scan.endBar - scan.startBar);
    const pct = Math.max(0, Math.min(100, (bar - scan.startBar) / span * 100));
    $('progress').value = pct;
    $('stepCounter').textContent = `${idx + 1} / ${allSetups.length}`;
    if (isoTime) $('barDate').textContent = formatDateTime(isoTime);
}

function goTo(i) {
    if (!allSetups.length || i < 0 || i >= allSetups.length) return;
    idx = i;
    renderQuestion(allSetups[idx]);
    renderTradeList();
}

function nextResolved() {
    for (let i = idx + 1; i < allSetups.length; i++) {
        const s = allSetups[i];
        if (s.entryBar >= 0) {
            goTo(i);
            return;
        }
    }

    // No filled setups left: still show the next signal/skip so the user sees why
    // it is not a decision, instead of leaving the previous answer on screen.
    if (idx + 1 < allSetups.length) {
        goTo(idx + 1);
        return;
    }

    $('status').textContent = 'Все сетапы пройдены';
}

function renderTradeList() {
    const box = $('tradeList');
    if (!allSetups.length) {
        box.innerHTML = '<em style="color:#666">Нет сетапов</em>';
        return;
    }

    const rows = allSetups.map((s, i) => {
        const time = formatDateTime(s.signalTime).slice(0, 10);
        const dir = s.isUpDiagonal ? '↓' : '↑';
        const net = s.entryBar >= 0 && isFinite(s.netR) ? s.netR.toFixed(2) : '—';
        const outcome = s.entryBar >= 0
            ? (s.outcome === 'SL' && s.netR > 0 ? 'TP1→БУ' : s.outcome)
            : (s.noFillReason === 'impulseBreaksP0' ? 'A>P0'
            : (s.diagonalBrokenBar >= 0 ? 'BROKEN'
            : (s.setupWorked ? 'SKIP' : 'NOFILL')));
        const aRatio = s.aStartBar >= 0 && s.diagonalHeight > 0
            ? (s.aHeight / s.diagonalHeight).toFixed(2) : '—';
        const bDepth = s.bEndBar >= 0 && isFinite(s.bDepth)
            ? (s.bDepth * 100).toFixed(0) + '%' : '—';
        const cls = i === idx ? ' class="active"' : '';
        return `<tr${cls} data-i="${i}">
            <td>${i + 1}</td><td>${time}</td><td>${dir}</td><td>${aRatio}</td><td>${bDepth}</td>
            <td>${s.entryBar >= 0 ? fmtPrice(s.entryPrice) : '—'}</td>
            <td>${s.entryBar >= 0 ? fmtPrice(s.stopLoss) : '—'}</td>
            <td>${s.entryBar >= 0 ? fmtPrice(s.takeProfit1) : '—'}</td>
            <td>${s.entryBar >= 0 ? fmtPrice(s.takeProfit2) : '—'}</td>
            <td>${outcome}</td><td>${net}</td>
        </tr>`;
    }).join('');

    box.innerHTML = `<table>
        <thead><tr><th>#</th><th>Дата</th><th>Сторона</th><th>A/D</th><th>B%</th><th>Вход</th><th>SL</th><th>TP1</th><th>TP2</th><th>Итог</th><th>netR</th></tr></thead>
        <tbody>${rows}</tbody></table>`;

    box.querySelectorAll('tr[data-i]').forEach(tr => {
        tr.addEventListener('click', () => goTo(parseInt(tr.dataset.i, 10)));
    });
}

// ── Decision handlers ──
$('btnEnter').addEventListener('click', () => {
    if (phase !== 'question') return;
    renderAnswer(allSetups[idx], 'enter');
});
$('btnSkip').addEventListener('click', () => {
    if (phase !== 'question') return;
    renderAnswer(allSetups[idx], 'skip');
});
$('btnNext').addEventListener('click', nextResolved);

$('btnFit').addEventListener('click', () => {
    try { chart.timeScale().fitContent(); } catch (_) { /* ignore */ }
});

// ── Scan ──
$('btnScan').addEventListener('click', runScan);

async function runScan() {
    const file = $('selFile').value;
    if (!file || file.startsWith('--')) { $('status').textContent = 'Выберите файл'; return; }

    $('btnScan').disabled = true;
    const scanStartedAt = Date.now();
    $('status').textContent = 'Сканирование...';
    if (scanTimer) clearInterval(scanTimer);
    scanTimer = setInterval(() => {
        const seconds = Math.floor((Date.now() - scanStartedAt) / 1000);
        $('status').textContent = seconds < 10
            ? 'Сканирование...'
            : `Сканирование... ${seconds} с (большой файл — подождите)`;
    }, 500);

    const body = {
        file,
        fromDate: dateInputToIso($('inpFromDate').value),
        toDate: dateInputToIso($('inpToDate').value),
        period: numVal('pPeriod', 0),
        minSizePercent: numVal('pMinSize', 0.1),
        barsCount: numVal('pBars', 10),
        minWave2Retrace: numVal('pW2Retrace', 0.5),
        maxWave4RetraceW3: numVal('pMaxW4W3', 0.382),
        initialMode: $('pInitialMode').value,
        enterRatio: numVal('pEnterRatio', 0.5),
        minARatio: numVal('pMinARatio', 0.236),
        minABars: Math.round(numVal('pMinABars', 10)),
        maxABars: Math.round(numVal('pMaxABars', 0)),
        requireSmoothA: $('pRequireSmoothA').value === '1',
        aStartToleranceW3: numVal('pAStartTol', 10) / 100,
        aStartBarTolerance: 2,
        stopBuffer: numVal('pStopBuffer', 0),
        target1Ratio: numVal('pTarget1', 1.0),
        ladder: $('pLadder').value === '1',
        target2Ratio: numVal('pTarget2', 1.272),
        costRate: 0.0002,
        execution: $('pExecution').value,
        maxHoldBars: 500,
        maxWaitBars: 300,
        requireAInsideP0: $('pRequireAInsideP0').value === '1',
        rsiPeriod: Math.round(numVal('pRsiPeriod', 14)),
        rsiDivergence: $('pRsiDiv').value
    };

    try {
        const res = await fetch('/api/diagonal-impulse/scan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.title || err.error || res.statusText);
        }
        scan = await res.json();
        if (scanTimer) { clearInterval(scanTimer); scanTimer = null; }
        onScanLoaded();
    } catch (e) {
        if (scanTimer) { clearInterval(scanTimer); scanTimer = null; }
        $('status').textContent = 'Ошибка: ' + e.message;
    } finally {
        if (scanTimer) { clearInterval(scanTimer); scanTimer = null; }
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

    allSetups = scan.setups || [];

    idx = -1;
    phase = 'idle';
    decided.clear();
    stats.total = stats.win = stats.loss = stats.goodSkip = stats.missed = 0;
    renderStats();

    const n = allSetups.length;
    const periodInfo =
        `период диаг. ${scan.usedPeriod} / имп. ${scan.impulsePeriod} ` +
        `(bps ${(scan.medianBarBps || 0).toFixed(1)})`;
    $('status').textContent =
        `${scan.symbol} ${scan.timeframe}: сетапов — ${n} ` +
        `(сигналов ${scan.signalCount}, филлов ${scan.filledCount}, ` +
        `пропусков C ${scan.skippedCount || 0}, A>P0 ${scan.impulseBrokenCount || 0}, ` +
        `сломано ${scan.brokenCount || 0}, ` +
        `TP/SL ${scan.resolvedCount}) · ${periodInfo}`;

    renderTradeList();

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

    // Start on the first resolved setup; fall back to the first setup.
    let first = allSetups.findIndex(s => s.entryBar >= 0 &&
        (s.outcome === 'TP' || s.outcome === 'SL'));
    if (first < 0) first = 0;
    goTo(first);
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
            $('status').textContent = info.barCount > 50000
                ? `Большой файл (${info.barCount} баров): выберите дату начала — полный скан может занять минуты`
                : 'Готов';
        } else {
            $('rangeInfo').textContent = '';
        }
    } catch (_) { $('rangeInfo').textContent = ''; }
}

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

// ── Sliders and presets ──
const PARAM_IDS = [
    'pPeriod', 'pMinSize', 'pBars', 'pW2Retrace', 'pMaxW4W3',
    'pEnterRatio', 'pMinARatio', 'pMinABars', 'pMaxABars', 'pAStartTol', 'pStopBuffer',
    'pTarget1', 'pTarget2', 'pRsiPeriod'
];

function syncParamReadouts() {
    const period = numVal('pPeriod', 0);
    $('vPeriod').textContent = period === 0 ? '0 (авто)' : String(period);
    $('vMinSize').textContent = numVal('pMinSize', 0.1).toFixed(2) + ' %';
    $('vBars').textContent = String(Math.round(numVal('pBars', 10)));
    $('vW2Retrace').textContent = numVal('pW2Retrace', 0.5).toFixed(2);
    $('vMaxW4W3').textContent = numVal('pMaxW4W3', 0.382).toFixed(3);
    $('vEnterRatio').textContent = numVal('pEnterRatio', 0.5).toFixed(3);
    $('vMinARatio').textContent = numVal('pMinARatio', 0.236).toFixed(3);
    $('vMinABars').textContent = String(Math.round(numVal('pMinABars', 10)));
    const maxABars = Math.round(numVal('pMaxABars', 0));
    $('vMaxABars').textContent = maxABars === 0 ? '0 (off)' : String(maxABars);
    $('vRequireSmoothA').textContent =
        $('pRequireSmoothA').value === '1' ? 'on' : 'off';
    $('vAStartTol').textContent = Math.round(numVal('pAStartTol', 10)) + ' %';
    $('vStopBuffer').textContent = numVal('pStopBuffer', 0).toFixed(2);
    $('vTarget1').textContent = numVal('pTarget1', 1.0).toFixed(3);
    $('vTarget2').textContent = numVal('pTarget2', 1.272).toFixed(3);
    $('vInitialMode').textContent = $('pInitialMode').value;
    $('vRsiPeriod').textContent = String(Math.round(numVal('pRsiPeriod', 14)));
    $('vRsiDiv').textContent = $('pRsiDiv').value.toLowerCase();
    $('vRequireAInsideP0').textContent =
        $('pRequireAInsideP0').value === '1' ? 'on — заходной' : 'off';
    $('vExecution').textContent = $('pExecution').value.toLowerCase();
    $('vLadder').textContent = $('pLadder').value === '1'
        ? `TP1 ${numVal('pTarget1', 1).toFixed(3)} A + TP2 ${numVal('pTarget2', 1.272).toFixed(3)} A`
        : `только TP1 ${numVal('pTarget1', 1).toFixed(3)} A`;

    if (scan && idx >= 0 && idx < allSetups.length)
        renderGeometry(allSetups[idx]);
}

for (const id of PARAM_IDS)
    $(id).addEventListener('input', syncParamReadouts);

for (const id of ['pInitialMode', 'pLadder', 'pExecution', 'pRequireAInsideP0', 'pRequireSmoothA',
    'pRsiDiv'])
    $(id).addEventListener('change', syncParamReadouts);

document.querySelectorAll('.preset').forEach(btn => {
    btn.addEventListener('click', () => {
        $('pEnterRatio').value = btn.dataset.k;
        $('pTarget1').value = btn.dataset.t1;
        $('pTarget2').value = btn.dataset.t2;
        $('pLadder').value = btn.dataset.ladder;
        syncParamReadouts();
    });
});

// ── Collapsible right panel ──
function setPanelOpen(open) {
    const panel = $('gamePanel');
    panel.classList.toggle('collapsed', !open);
    $('btnPanelToggle').textContent = open ? '⚙ Параметры' : '▸ Параметры';
}

$('btnPanelToggle').addEventListener('click', () => {
    setPanelOpen($('gamePanel').classList.contains('collapsed'));
});
$('btnPanelClose').addEventListener('click', () => setPanelOpen(false));
$('btnScanPanel').addEventListener('click', runScan);

syncParamReadouts();
loadFiles();
