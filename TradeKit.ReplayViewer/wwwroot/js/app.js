// ── State ──
let currentFrame = 0;
let replayData = null;     // { candles, replay, snapshot, startBar, endBar }
let selectedNodeId = null;
let autoTimer = null;
let autoSpeed = 500;       // ms
let fileInfo = null;        // { barCount, firstBarTime, lastBarTime }

// ── DOM refs ──
const $ = id => document.getElementById(id);
const el = {
    selFile: $('selFile'), inpFrom: $('inpFromDate'), inpTo: $('inpToDate'),
    inpFromCal: $('inpFromDateCal'), inpToCal: $('inpToDateCal'),
    btnFull: $('btnFull'), rangeInfo: $('rangeInfo'),
    inpDead: $('inpDead'), btnLoad: $('btnLoad'), status: $('status'),
    nodeList: $('nodeList'), treeBarLabel: $('treeBarLabel'),
    btnFirst: $('btnFirst'), btnPrev: $('btnPrev'), btnNext: $('btnNext'),
    btnLast: $('btnLast'), btnAuto: $('btnAuto'),
    slider: $('slider'), frmCurrent: $('frmCurrent'), frmTotal: $('frmTotal'),
    frameLabel: $('frameLabel')
};

// ── Date helpers ──
function isoToDateInput(isoStr) {
    if (!isoStr) return '';
    // "2024-05-01T10:00:00.0000000Z" → "01/05/2024"
    const m = isoStr.match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!m) return '';
    return `${m[3]}/${m[2]}/${m[1]}`;
}

function dateInputToIso(dateStr) {
    if (!dateStr) return null;
    // "01/05/2024" → "2024-05-01T00:00:00Z"
    // Accept both dd/MM/yyyy and dd.MM.yyyy
    const m = dateStr.match(/^(\d{2})[./](\d{2})[./](\d{4})/);
    if (!m) return null;
    return `${m[3]}-${m[2]}-${m[1]}T00:00:00Z`;
}

function formatDate(isoStr) {
    // "2024-05-01" → "01.05.2024" (day first)
    const m = isoStr ? isoStr.match(/^(\d{4})-(\d{2})-(\d{2})/) : null;
    return m ? `${m[3]}.${m[2]}.${m[1]}` : isoStr || '?';
}

/** Convert ISO string to yyyy-MM-dd (native date input value). */
function isoToCalValue(isoStr) {
    if (!isoStr) return '';
    const m = isoStr.match(/^(\d{4}-\d{2}-\d{2})/);
    return m ? m[1] : '';
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
el.btnLoad.addEventListener('click', runReplay);
el.btnFull.addEventListener('click', resetRange);
el.btnFirst.addEventListener('click', () => goFrame(0));
el.btnPrev.addEventListener('click', () => goFrame(currentFrame - 1));
el.btnNext.addEventListener('click', () => goFrame(currentFrame + 1));
el.btnLast.addEventListener('click', () => goFrame((replayData?.replay?.frames?.length || 1) - 1));
el.btnAuto.addEventListener('click', toggleAuto);
el.slider.addEventListener('input', () => goFrame(parseInt(el.slider.value)));

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

/** Set both text (dd/MM/yyyy) and hidden calendar (yyyy-MM-dd) fields. */
function setDateValues(firstIso, lastIso) {
    el.inpFrom.value = isoToDateInput(firstIso);
    el.inpFromCal.value = isoToCalValue(firstIso);
    el.inpTo.value = isoToDateInput(lastIso);
    el.inpToCal.value = isoToCalValue(lastIso);
}

function updateRangeInfo() {
    if (!fileInfo) return;
    const fullStart = formatDate(fileInfo.firstBarTime);
    const fullEnd = formatDate(fileInfo.lastBarTime);
    const selStart = el.inpFrom.value || isoToDateInput(fileInfo.firstBarTime);
    const selEnd = el.inpTo.value || isoToDateInput(fileInfo.lastBarTime);
    el.rangeInfo.textContent = `[${selStart.replace(/\//g, '.')} … ${selEnd.replace(/\//g, '.')}]  /  ${fullStart} … ${fullEnd}`;
}

// ── Run replay ──
async function runReplay() {
    const file = el.selFile.value;
    if (!file) return;

    el.btnLoad.disabled = true;
    el.status.textContent = 'Running markup...';
    currentFrame = 0;
    selectedNodeId = null;

    try {
        const res = await fetch('/api/replay/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                file,
                fromDate: dateInputToIso(el.inpFrom.value),
                toDate: dateInputToIso(el.inpTo.value),
                deadDepth: parseInt(el.inpDead.value) || 1
            })
        });
        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.detail || err.title || 'Unknown error');
        }

        replayData = await res.json();
        el.status.textContent = `OK — ${replayData.snapshot.nodes.length} nodes, ${replayData.replay.frames.length} frames`;

        // Init playback
        const n = replayData.replay.frames.length;
        el.slider.max = Math.max(0, n - 1);
        el.slider.value = 0;
        el.frmTotal.textContent = n;
        el.btnLoad.disabled = false;

        // Show first frame
        goFrame(0);
    } catch (e) {
        el.status.textContent = 'Error: ' + e.message;
        el.btnLoad.disabled = false;
    }
}

// ── Frame navigation ──
function goFrame(idx) {
    if (!replayData) return;
    const frames = replayData.replay.frames;
    if (frames.length === 0) return;

    idx = Math.max(0, Math.min(idx, frames.length - 1));
    currentFrame = idx;
    el.slider.value = idx;
    el.frmCurrent.textContent = idx;

    const frame = frames[idx];
    el.frameLabel.textContent = `Bar ${frame.newPivot?.barIndex ?? '?'} / ${replayData.candles.length}`;
    el.treeBarLabel.textContent = frame.newPivot?.barIndex ?? '0';

    renderTree(frame);
    updatePlaybackButtons();
}

// ── Tree panel ──
function renderTree(frame) {
    // Build node lookup
    const nodeMap = {};
    for (const n of replayData.snapshot.nodes) nodeMap[n.id] = n;

    // Group by status
    const aliveIds = new Set(frame.aliveNodeIds || []);
    const aliveNodes = [];
    const deadNodes = [];

    for (const n of replayData.snapshot.nodes) {
        if (aliveIds.has(n.id)) aliveNodes.push(n);
        else deadNodes.push(n);
    }

    // Render
    let html = '';
    if (aliveNodes.length === 0 && deadNodes.length === 0) {
        html = '<em style="color:#666;padding:12px;display:block">No nodes yet</em>';
    }

    for (const n of aliveNodes) {
        html += nodeHtml(n, true);
    }
    if (deadNodes.length > 0) {
        html += '<div style="padding:6px 12px;font-size:11px;color:#666;border-top:1px solid #333840;margin-top:4px">Dead / pruned</div>';
        for (const n of deadNodes) {
            if (n.status === 'DEAD')
                html += nodeHtml(n, false);
        }
    }

    el.nodeList.innerHTML = html;

    // Click handlers
    el.nodeList.querySelectorAll('.node-card').forEach(card => {
        card.addEventListener('click', () => {
            const nid = card.dataset.nodeId;
            el.nodeList.querySelectorAll('.node-card').forEach(c => c.classList.remove('selected'));
            card.classList.add('selected');
            selectedNodeId = nid;
            drawFromSnapshot(replayData.snapshot, replayData.candles, selectedNodeId);
        });
    });

    // Auto-select best node
    if (!selectedNodeId && frame.bestNodeId && nodeMap[frame.bestNodeId]) {
        const bestCard = el.nodeList.querySelector(`[data-node-id="${CSS.escape(frame.bestNodeId)}"]`);
        if (bestCard) {
            bestCard.classList.add('selected');
            selectedNodeId = frame.bestNodeId;
            drawFromSnapshot(replayData.snapshot, replayData.candles, selectedNodeId);
        }
    }
}

function nodeHtml(n, alive) {
    const statusClass = n.status === 'COMPLETE' ? 'badge-complete'
        : n.status === 'PROJECTED' ? 'badge-projected'
        : n.status === 'DEAD' ? 'badge-dead'
        : 'badge-open';
    const statusLabel = n.status === 'DEAD' ? (n.deathReason || 'DEAD') : n.status;

    const childModels = (n.children || []).map(cid => {
        const c = replayData.snapshot.nodes.find(x => x.id === cid);
        return c ? `${c.wavePos || '?'}:${c.model}` : '';
    }).filter(Boolean).join(' ');

    // Get zigzag prices for this node
    const zz = replayData.snapshot.zigzag;
    const startPx = zz && n.startPivot >= 0 && n.startPivot < zz.length
        ? zz[n.startPivot].price : null;
    const endPx = zz && n.endPivot >= 0 && n.endPivot < zz.length
        ? zz[n.endPivot].price : null;

    return `
    <div class="node-card ${alive ? '' : 'node-dead'}" data-node-id="${escHtml(n.id)}">
      <div>
        <span class="badge ${statusClass}">${statusLabel}</span>
        <span class="node-model">${n.model}</span>
        <span style="color:#8892a0;font-size:11px">${n.wavePos === 'root' ? 'root' : n.wavePos}</span>
        <span style="float:right;font-size:11px;color:#8892a0">${(n.score || 0).toFixed(3)}</span>
      </div>
      <div class="node-meta">L${n.level} pivots [${n.startPivot}..${n.endPivot}] ${n.isUp ? '↑' : '↓'}</div>
      <div class="node-meta">
        ${startPx != null ? startPx.toFixed(5) : '?'} → ${endPx != null ? endPx.toFixed(5) : '?'}
      </div>
      ${childModels ? `<div class="node-waves">${childModels}</div>` : ''}
      ${n.status === 'DEAD' ? `<div class="cancel-info" style="color:#d65757">death: ${n.deathReason}</div>` : ''}
    </div>`;
}

function escHtml(s) {
    if (!s) return '';
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

// ── Auto play ──
function toggleAuto() {
    if (autoTimer) {
        clearInterval(autoTimer);
        autoTimer = null;
        el.btnAuto.textContent = '▶▶';
        el.btnAuto.style.background = '#333840';
    } else {
        el.btnAuto.textContent = '⏹';
        el.btnAuto.style.background = '#cc4444';
        autoTimer = setInterval(() => {
            if (currentFrame >= (replayData?.replay?.frames?.length || 0) - 1) {
                toggleAuto();
                return;
            }
            goFrame(currentFrame + 1);
        }, autoSpeed);
    }
}

function updatePlaybackButtons() {
    const n = replayData?.replay?.frames?.length || 0;
    el.btnFirst.disabled = currentFrame === 0;
    el.btnPrev.disabled = currentFrame === 0;
    el.btnNext.disabled = currentFrame >= n - 1;
    el.btnLast.disabled = currentFrame >= n - 1;
}

// ── Calendar ↔ text sync ──
el.inpFromCal.addEventListener('change', () => {
    el.inpFrom.value = isoToDateInput(el.inpFromCal.value + 'T00:00:00Z');
    updateRangeInfo();
});
el.inpToCal.addEventListener('change', () => {
    el.inpTo.value = isoToDateInput(el.inpToCal.value + 'T00:00:00Z');
    updateRangeInfo();
});
// Open calendar on text input or icon click
el.inpFrom.addEventListener('click', () => {
    try { el.inpFromCal.showPicker(); } catch{/* no-op */}
});
el.inpTo.addEventListener('click', () => {
    try { el.inpToCal.showPicker(); } catch{/* no-op */}
});
$('iconFromCal').addEventListener('click', () => {
    try { el.inpFromCal.showPicker(); } catch{/* no-op */}
});
$('iconToCal').addEventListener('click', () => {
    try { el.inpToCal.showPicker(); } catch{/* no-op */}
});
