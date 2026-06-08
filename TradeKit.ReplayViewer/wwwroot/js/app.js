// ── State ──
let session = null;          // { startBar, endBar, totalSteps, priceDecimals, symbol, timeframe }
let stepIndex = 0;           // number of segments analysed so far
let allCandles = [];         // accumulated candles (absolute barIndex), revealed segment by segment
let currentSnapshot = null;  // latest per-step tree snapshot
let selectedNodeId = null;
let playing = false;         // continuous play in progress
let pauseRequested = false;  // finish current segment, then stop
let priceDecimals = 5;
let fileInfo = null;         // { barCount, firstBarTime, lastBarTime }
const PLAY_DELAY = 150;      // ms between auto steps

// ── DOM refs ──
const $ = id => document.getElementById(id);
const el = {
    selFile: $('selFile'), inpFrom: $('inpFromDate'), inpTo: $('inpToDate'),
    btnFull: $('btnFull'), rangeInfo: $('rangeInfo'),
    inpDead: $('inpDead'), btnLoad: $('btnLoad'), status: $('status'),
    nodeList: $('nodeList'), treeBarLabel: $('treeBarLabel'),
    btnPlay: $('btnPlay'), btnStep: $('btnStep'),
    stepCounter: $('stepCounter'), progress: $('progress'), barDate: $('barDate')
};

// ── Date helpers ──
/** "dd.mm.yyyy" (day-first) text-input value → ISO instant for the API. Also accepts ISO. */
function dateInputToIso(dateStr) {
    if (!dateStr) return null;
    let m = dateStr.match(/^\s*(\d{1,2})[.\/-](\d{1,2})[.\/-](\d{4})\s*$/); // dd.mm.yyyy
    if (m) return `${m[3]}-${m[2].padStart(2,'0')}-${m[1].padStart(2,'0')}T00:00:00Z`;
    m = dateStr.match(/^(\d{4})-(\d{2})-(\d{2})/);                          // yyyy-mm-dd
    if (m) return `${m[1]}-${m[2]}-${m[3]}T00:00:00Z`;
    return null;
}

function formatDate(isoStr) {
    // "2024-05-01" / ISO → "01.05.2024" (day first)
    const m = isoStr ? isoStr.match(/^(\d{4})-(\d{2})-(\d{2})/) : null;
    return m ? `${m[3]}.${m[2]}.${m[1]}` : isoStr || '?';
}

/** "2024-05-01T13:00:00Z" → "01.05.2024 13:00" (day first, with time). */
function formatDateTime(isoStr) {
    const m = isoStr ? isoStr.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/) : null;
    return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}` : (formatDate(isoStr));
}

/** Convert ISO string to "dd.mm.yyyy" (day-first text-input value). */
function isoToCalValue(isoStr) {
    return formatDate(isoStr) === '?' ? '' : formatDate(isoStr);
}

// ── Init ──
(async function init() {
    el.status.textContent = 'Loading file list...';
    try {
        const res = await fetch('/api/replay/files');
        const files = await res.json();
        el.selFile.innerHTML = '';
        if (files.length === 0) {
            el.selFile.innerHTML = '<option>-- no CSV files in data/ --</option>';
            el.status.textContent = 'No CSV files found. Set REPLAY_DATA_DIR env var.';
            return;
        }

        // Build file index for quick lookup
        const fileIndex = {};
        for (const f of files) {
            fileIndex[f.name] = f;
            const opt = document.createElement('option');
            opt.value = f.name;
            const kb = (f.sizeBytes / 1024).toFixed(0);
            const barHint = f.barCount ? `  [${f.barCount} bars]` : '';
            opt.textContent = `${f.name} (${kb} KB)${barHint}`;
            el.selFile.appendChild(opt);
        }
        el.status.textContent = 'Ready. Select a file and click Run.';

        // Auto-select first file to pre-populate date range
        if (files.length > 0) {
            el.selFile.value = files[0].name;
            populateDatesFromFileInfo(files[0]);
        }

        // Update dates when user changes selection
        el.selFile.addEventListener('change', async () => {
            const name = el.selFile.value;
            if (name && fileIndex[name]) {
                populateDatesFromFileInfo(fileIndex[name]);
            }
        });
    } catch (e) {
        el.status.textContent = 'Error: ' + e.message;
    }
})();

// ── Events ──
el.btnLoad.addEventListener('click', loadReplay);
el.btnFull.addEventListener('click', resetRange);
el.btnPlay.addEventListener('click', togglePlay);
el.btnStep.addEventListener('click', () => { if (!playing) doStep(); });

el.inpFrom.addEventListener('change', updateRangeInfo);
el.inpTo.addEventListener('change', updateRangeInfo);

/** Populate date fields from a CsvFileInfo object (from the listing cache). */
function populateDatesFromFileInfo(info) {
    if (!info) return;
    fileInfo = info;
    if (info.barCount) {
        setDateValues(info.firstBarTime, info.lastBarTime);
        updateRangeInfo();
        el.status.textContent = `${info.barCount} bars`;
    }
}

function resetRange() {
    if (!fileInfo) return;
    setDateValues(fileInfo.firstBarTime, fileInfo.lastBarTime);
    updateRangeInfo();
}

/** Set native date inputs (yyyy-MM-dd). */
function setDateValues(firstIso, lastIso) {
    el.inpFrom.value = isoToCalValue(firstIso);
    el.inpTo.value = isoToCalValue(lastIso);
}

function updateRangeInfo() {
    if (!fileInfo) return;
    const fullStart = formatDate(fileInfo.firstBarTime);
    const fullEnd = formatDate(fileInfo.lastBarTime);
    const selStart = formatDate(el.inpFrom.value || fileInfo.firstBarTime);
    const selEnd = formatDate(el.inpTo.value || fileInfo.lastBarTime);
    el.rangeInfo.textContent = `[${selStart} … ${selEnd}]  /  ${fullStart} … ${fullEnd}`;
}

// ── Load (initialise an on-demand stepping session) ──
async function loadReplay() {
    const file = el.selFile.value;
    if (!file) return;

    pauseRequested = true;            // stop any running play loop
    playing = false;
    setPlayButton(false);
    el.btnLoad.disabled = true;
    el.btnPlay.disabled = true;
    el.btnStep.disabled = true;
    el.status.textContent = 'Initialising...';

    // Reset accumulators + chart
    session = null;
    stepIndex = 0;
    allCandles = [];
    currentSnapshot = null;
    selectedNodeId = null;
    clearMarkup();
    setCandles([]);
    el.nodeList.innerHTML = '<em style="color:#666;padding:12px;display:block">Analysing…</em>';
    el.treeBarLabel.textContent = '0';
    el.stepCounter.textContent = '0 / 0';
    el.progress.value = 0;
    el.barDate.textContent = '—';

    try {
        const res = await fetch('/api/replay/init', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                file,
                fromDate: dateInputToIso(el.inpFrom.value),
                toDate: dateInputToIso(el.inpTo.value),
                deadDepth: parseInt(el.inpDead.value) || 1
            })
        });
        if (!res.ok) throw new Error((await res.text()) || 'Unknown error');

        session = await res.json();
        priceDecimals = session.priceDecimals || 5;
        setPricePrecision(priceDecimals);

        el.status.textContent = `${session.symbol} ${session.timeframe} — ${session.totalSteps} segments`;
        el.stepCounter.textContent = `0 / ${session.totalSteps}`;
        el.btnPlay.disabled = false;
        el.btnStep.disabled = false;
    } catch (e) {
        el.status.textContent = 'Error: ' + e.message;
    } finally {
        el.btnLoad.disabled = false;
    }
}

// ── Step: analyse exactly one zigzag segment ──
/** Returns true if more segments remain afterwards, false when finished. */
async function doStep() {
    if (!session) return false;
    try {
        const res = await fetch('/api/replay/step', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ deadDepth: parseInt(el.inpDead.value) || 1 })
        });
        if (!res.ok) throw new Error((await res.text()) || 'Step failed');

        const data = await res.json();
        if (data.done) {
            el.btnStep.disabled = true;
            el.status.textContent = `Finished — ${stepIndex} / ${session.totalSteps} segments`;
            return false;
        }

        // Reveal the candles that make up this segment
        if (data.newCandles && data.newCandles.length) {
            for (const c of data.newCandles) allCandles.push(c);
            setCandles(allCandles);
        }

        currentSnapshot = data.snapshot;
        stepIndex++;

        // Progress = position of the current bar within the date range (not clickable)
        const f = data.frame;
        const curBar = f?.newPivot?.barIndex
            ?? (allCandles.length ? allCandles[allCandles.length - 1].barIndex : session.startBar);
        const span = Math.max(1, session.endBar - session.startBar);
        el.progress.value = Math.max(0, Math.min(100, ((curBar - session.startBar) / span) * 100));

        const barIso = f?.closeTime || f?.newPivot?.closeTime
            || (allCandles.length ? allCandles[allCandles.length - 1].time : null);
        el.barDate.textContent = barIso ? formatDateTime(barIso) : `bar ${curBar}`;
        el.treeBarLabel.textContent = curBar;
        el.stepCounter.textContent = `${stepIndex} / ${session.totalSteps}`;
        el.status.textContent = `Segment ${stepIndex} — bar ${curBar}`;

        // Render the per-step tree and auto-select the best (first) node
        renderTree(currentSnapshot, f?.bestNodeId);

        return stepIndex < session.totalSteps;
    } catch (e) {
        el.status.textContent = 'Error: ' + e.message;
        return false;
    }
}

// ── Play / pause ──
function setPlayButton(isPlaying) {
    el.btnPlay.textContent = isPlaying ? '⏸ Pause' : '▶ Play';
    el.btnPlay.classList.toggle('playing', isPlaying);
}

const delay = ms => new Promise(r => setTimeout(r, ms));

async function togglePlay() {
    if (playing) {
        // Pause: finish the current segment, then stop
        pauseRequested = true;
        return;
    }
    if (!session) return;

    playing = true;
    pauseRequested = false;
    setPlayButton(true);
    el.btnStep.disabled = true;

    while (!pauseRequested) {
        const more = await doStep();
        if (!more) break;
        if (pauseRequested) break;
        await delay(PLAY_DELAY);
    }

    playing = false;
    pauseRequested = false;
    setPlayButton(false);
    el.btnStep.disabled = stepIndex >= (session?.totalSteps || 0);
}


// ── Per-step tree render ──

function renderTree(snapshot, bestNodeId) {
    if (!snapshot || !snapshot.nodes) return;

    const aliveNodes = [];
    const deadNodes = [];
    for (const n of snapshot.nodes) {
        if (n.status === 'DEAD') deadNodes.push(n);
        else aliveNodes.push(n);
    }

    let html = '';
    if (aliveNodes.length === 0 && deadNodes.length === 0) {
        html = '<em style="color:#666;padding:12px;display:block">No nodes yet</em>';
    }
    for (const n of aliveNodes) html += nodeHtml(snapshot, n, true);
    if (deadNodes.length > 0) {
        html += `<div style="padding:6px 12px;font-size:11px;color:#666;border-top:1px solid #333840;margin-top:4px">Dead / pruned</div>`;
        for (const n of deadNodes) html += nodeHtml(snapshot, n, false);
    }

    el.nodeList.innerHTML = html;

    // Click handlers
    el.nodeList.querySelectorAll('.node-card').forEach(card => {
        card.addEventListener('click', () => {
            const nid = card.dataset.nodeId;
            el.nodeList.querySelectorAll('.node-card').forEach(c => c.classList.remove('selected'));
            card.classList.add('selected');
            selectedNodeId = nid;
            drawFromSnapshot(currentSnapshot, allCandles, selectedNodeId);
        });
    });

    // Auto-select the best node (default: first/"n0") and draw it
    const pick = (bestNodeId && snapshot.nodes.some(n => n.id === bestNodeId))
        ? bestNodeId
        : (snapshot.nodes[0] ? snapshot.nodes[0].id : null);
    if (pick) {
        const card = el.nodeList.querySelector(`[data-node-id="${CSS.escape(pick)}"]`);
        if (card) card.classList.add('selected');
        selectedNodeId = pick;
        drawFromSnapshot(currentSnapshot, allCandles, selectedNodeId);
    }

    // Keep the newest branches visible: scroll the panel to the bottom
    el.nodeList.scrollTop = el.nodeList.scrollHeight;
}

function nodeHtml(snapshot, n, alive) {
    const statusClass = n.status === 'COMPLETE' ? 'badge-complete'
        : n.status === 'PROJECTED' ? 'badge-projected'
        : n.status === 'DEAD' ? 'badge-dead'
        : 'badge-open';
    const statusLabel = n.status === 'DEAD' ? (n.deathReason || 'DEAD') : n.status;

    const childModels = (n.children || []).map(cid => {
        const c = snapshot.nodes.find(x => x.id === cid);
        return c ? `${c.wavePos || '?'}:${c.model}` : '';
    }).filter(Boolean).join(' ');

    const zz = snapshot.zigzag;
    const startPx = zz && n.startPivot >= 0 && n.startPivot < zz.length
        ? zz[n.startPivot].price : null;
    const endPx = zz && n.endPivot >= 0 && n.endPivot < zz.length
        ? zz[n.endPivot].price : null;
    const px = v => v != null ? v.toFixed(priceDecimals) : '?';

    return `
    <div class="node-card ${alive ? '' : 'node-dead'}" data-node-id="${escHtml(n.id)}">
      <div>
        <span class="badge ${statusClass}">${statusLabel}</span>
        <span class="node-model">${escHtml(n.model)}</span>
        <span style="color:#8892a0;font-size:11px">${n.wavePos === 'root' ? 'root' : escHtml(n.wavePos)}</span>
        <span style="float:right;font-size:11px;color:#8892a0">${(n.score || 0).toFixed(3)}</span>
      </div>
      <div class="node-meta">L${n.level} pivots [${n.startPivot}..${n.endPivot}] ${n.isUp ? '↑' : '↓'}</div>
      <div class="node-meta">${px(startPx)} → ${px(endPx)}</div>
      ${childModels ? `<div class="node-waves">${childModels}</div>` : ''}
      ${n.status === 'DEAD' ? `<div class="cancel-info" style="color:#d65757">death: ${escHtml(n.deathReason)}</div>` : ''}
    </div>`;
}

function escHtml(s) {
    if (!s) return '';
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

