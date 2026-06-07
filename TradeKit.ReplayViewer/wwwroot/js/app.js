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
    btnFull: $('btnFull'), rangeInfo: $('rangeInfo'),
    inpDead: $('inpDead'), btnLoad: $('btnLoad'), status: $('status'),
    nodeList: $('nodeList'), treeBarLabel: $('treeBarLabel'),
    btnNext: $('btnNext'), btnLast: $('btnLast'), btnAuto: $('btnAuto'),
    slider: $('slider'), frmCurrent: $('frmCurrent'), frmTotal: $('frmTotal'),
    frameLabel: $('frameLabel')
};

// ── Date helpers ──
/** Native <input type="date"> value (yyyy-MM-dd) → ISO instant for the API. */
function dateInputToIso(dateStr) {
    if (!dateStr) return null;
    const m = dateStr.match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!m) return null;
    return `${m[1]}-${m[2]}-${m[3]}T00:00:00Z`;
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

// ── Run replay (SSE streaming) ──
async function runReplay() {
    const file = el.selFile.value;
    if (!file) return;

    el.btnLoad.disabled = true;
    el.status.textContent = 'Running markup...';
    currentFrame = 0;
    selectedNodeId = null;
    stopAuto();
    clearMarkup();

    replayData = {
        candles: [],
        replay: { $schema: 'ew-markup-tree-replay/v2', symbol: '', timeframe: '', frames: [] },
        snapshot: { nodes: [], zigzag: [] },
        startBar: 0, endBar: 0
    };

    try {
        const res = await fetch('/api/replay/stream', {
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
            const err = await res.text();
            throw new Error(err || 'Unknown error');
        }

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buf = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buf += decoder.decode(value, { stream: true });
            const lines = buf.split('\n');
            buf = lines.pop() || ''; // keep incomplete last line

            for (const line of lines) {
                if (!line.startsWith('data: ')) continue;
                try {
                    const event = JSON.parse(line.slice(6));
                    handleSseEvent(event);
                } catch { /* skip malformed */ }
            }
        }
    } catch (e) {
        el.status.textContent = 'Error: ' + e.message;
        el.btnLoad.disabled = false;
    }
}

function handleSseEvent(event) {
    switch (event.type) {
        case 'init':
            replayData.candles = event.candles;
            replayData.startBar = event.startBar;
            replayData.endBar = event.endBar;
            setCandles(event.candles);
            el.status.textContent = 'Initialised — streaming frames...';
            break;

        case 'frame':
            replayData.replay.frames.push(event.frame);
            const n = replayData.replay.frames.length;
            el.slider.max = Math.max(0, n - 1);
            el.slider.value = n - 1;
            el.frmTotal.textContent = n;
            el.frmCurrent.textContent = n - 1;
            currentFrame = n - 1;
            const f = event.frame;
            el.frameLabel.textContent = `Bar ${f.newPivot?.barIndex ?? '?'} / ${replayData.candles.length}`;
            el.treeBarLabel.textContent = f.newPivot?.barIndex ?? '0';
            renderLiveFrame(f);
            el.status.textContent = `Frame ${n} — bar ${f.newPivot?.barIndex ?? '?'}`;
            break;

        case 'done':
            replayData.snapshot = event.snapshot;
            const totalFrames = replayData.replay.frames.length;
            el.status.textContent = `OK — ${event.snapshot.nodes.length} nodes, ${totalFrames} frames`;
            el.btnLoad.disabled = false;
            // Show last frame with full tree
            if (totalFrames > 0) {
                const lastFrame = replayData.replay.frames[totalFrames - 1];
                currentFrame = totalFrames - 1;
                el.slider.max = Math.max(0, totalFrames - 1);
                el.slider.value = currentFrame;
                el.frmCurrent.textContent = currentFrame;
                renderTree(lastFrame);
                // Auto-select best node
                if (lastFrame.bestNodeId) {
                    const bestCard = el.nodeList.querySelector(`[data-node-id="${CSS.escape(lastFrame.bestNodeId)}"]`);
                    if (bestCard) {
                        bestCard.classList.add('selected');
                        selectedNodeId = lastFrame.bestNodeId;
                        drawFromSnapshot(replayData.snapshot, replayData.candles, selectedNodeId);
                    }
                }
            }
            updatePlaybackButtons();
            break;

        case 'error':
            el.status.textContent = 'Error: ' + (event.message || 'Unknown');
            el.btnLoad.disabled = false;
            break;
    }
}

/** Lightweight tree render during streaming (no snapshot yet). */
function renderLiveFrame(frame) {
    const aliveIds = frame.aliveNodeIds || [];
    let html = '';
    if (aliveIds.length === 0) {
        html = '<em style="color:#666;padding:12px;display:block">No nodes yet</em>';
    } else {
        for (const id of aliveIds) {
            html += `<div class="node-card" data-node-id="${escHtml(id)}" style="cursor:default">
              <div><span class="badge badge-open">ALIVE</span>
              <span class="node-model">${escHtml(id)}</span></div>
            </div>`;
        }
    }

    // Show recent events
    if (frame.events && frame.events.length > 0) {
        const recent = frame.events.slice(-8);
        html += '<div style="padding:4px 12px;font-size:10px;color:#555d6b;border-top:1px solid #333840;margin-top:4px">Recent events</div>';
        for (const ev of recent) {
            const icon = ev.type === 'BORN' ? '🟢' : ev.type === 'DIED' ? '🔴' : ev.type === 'COMPLETED' ? '✅' : '⏳';
            html += `<div class="node-meta" style="padding:1px 12px;font-size:10px">${icon} ${ev.type} ${escHtml(ev.nodeId)}</div>`;
        }
    }

    el.nodeList.innerHTML = html;
}

// ── Full tree render (after streaming completes) ──

function renderTree(frame) {
    // Build node lookup
    const nodeMap = {};
    for (const n of replayData.snapshot.nodes) nodeMap[n.id] = n;

    const aliveIds = new Set(frame.aliveNodeIds || []);
    const aliveNodes = [];
    const deadNodes = [];

    for (const n of replayData.snapshot.nodes) {
        if (aliveIds.has(n.id)) aliveNodes.push(n);
        else deadNodes.push(n);
    }

    let html = '';
    if (aliveNodes.length === 0 && deadNodes.length === 0) {
        html = '<em style="color:#666;padding:12px;display:block">No nodes yet</em>';
    }

    for (const n of aliveNodes) {
        html += nodeHtml(n, true);
    }
    if (deadNodes.length > 0) {
        html += `<div style="padding:6px 12px;font-size:11px;color:#666;border-top:1px solid #333840;margin-top:4px">Dead / pruned</div>`;
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

    const zz = replayData.snapshot.zigzag;
    const startPx = zz && n.startPivot >= 0 && n.startPivot < zz.length
        ? zz[n.startPivot].price : null;
    const endPx = zz && n.endPivot >= 0 && n.endPivot < zz.length
        ? zz[n.endPivot].price : null;

    return `
    <div class="node-card ${alive ? '' : 'node-dead'}" data-node-id="${escHtml(n.id)}">
      <div>
        <span class="badge ${statusClass}">${statusLabel}</span>
        <span class="node-model">${escHtml(n.model)}</span>
        <span style="color:#8892a0;font-size:11px">${n.wavePos === 'root' ? 'root' : escHtml(n.wavePos)}</span>
        <span style="float:right;font-size:11px;color:#8892a0">${(n.score || 0).toFixed(3)}</span>
      </div>
      <div class="node-meta">L${n.level} pivots [${n.startPivot}..${n.endPivot}] ${n.isUp ? '↑' : '↓'}</div>
      <div class="node-meta">${startPx != null ? startPx.toFixed(5) : '?'} → ${endPx != null ? endPx.toFixed(5) : '?'}</div>
      ${childModels ? `<div class="node-waves">${childModels}</div>` : ''}
      ${n.status === 'DEAD' ? `<div class="cancel-info" style="color:#d65757">death: ${escHtml(n.deathReason)}</div>` : ''}
    </div>`;
}

function escHtml(s) {
    if (!s) return '';
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
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

    // Re-draw selected node on chart
    if (selectedNodeId && replayData.snapshot.nodes.length > 0) {
        drawFromSnapshot(replayData.snapshot, replayData.candles, selectedNodeId);
    }
}

// ── Playback (forward only) ──
function updatePlaybackButtons() {
    const frames = replayData?.replay?.frames || [];
    const atEnd = frames.length === 0 || currentFrame >= frames.length - 1;
    el.btnNext.disabled = atEnd;
    el.btnLast.disabled = atEnd;
}

function toggleAuto() {
    if (autoTimer) {
        stopAuto();
        return;
    }
    const frames = replayData?.replay?.frames || [];
    if (frames.length === 0) return;
    // Restart from the beginning if we are already at the last frame.
    if (currentFrame >= frames.length - 1) goFrame(0);

    el.btnAuto.textContent = '⏸';
    autoTimer = setInterval(() => {
        const total = replayData?.replay?.frames?.length || 0;
        if (currentFrame >= total - 1) {
            stopAuto();
            return;
        }
        goFrame(currentFrame + 1);
    }, autoSpeed);
}

function stopAuto() {
    if (autoTimer) {
        clearInterval(autoTimer);
        autoTimer = null;
    }
    el.btnAuto.textContent = '▶▶';
}

