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
/** IDs of tree nodes the user has collapsed in the panel — preserved across re-renders. */
const collapsedNodeIds = new Set();
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

    // Build id→node map for fast lookup
    const nodeMap = {};
    for (const n of snapshot.nodes) nodeMap[n.id] = n;

    // Roots: nodes with no parent (parentId === null)
    const roots = snapshot.nodes.filter(n => n.parentId === null);
    // Also include orphans whose parent isn't in the snapshot (defensive)
    const orphans = snapshot.nodes.filter(n =>
        n.parentId !== null && !nodeMap[n.parentId] && n.parentId !== 'root');
    const allRoots = [...roots, ...orphans];

    // Separate alive/dead roots for ordering
    const aliveRoots = allRoots.filter(n => n.status !== 'DEAD');
    const deadRoots = allRoots.filter(n => n.status === 'DEAD');

    let html = '';

    /**
     * Render a node and (when not collapsed by the user) its descendants.
     * `ancestorCollapsed` propagates the hidden state down so collapsed subtrees
     * never have to re-check every ancestor on every redraw.
     */
    function renderSubtree(n, depth, ancestorCollapsed) {
        const collapsed = collapsedNodeIds.has(n.id);
        const hidden = ancestorCollapsed;
        html += nodeHtml(snapshot, n, n.status !== 'DEAD', depth, collapsed, hidden);
        if (!collapsed) {
            const childIds = n.children || [];
            for (const cid of childIds) {
                const child = nodeMap[cid];
                if (child) renderSubtree(child, depth + 1, ancestorCollapsed);
            }
        }
    }

    if (aliveRoots.length === 0 && deadRoots.length === 0) {
        html = '<em style="color:#666;padding:12px;display:block">No nodes yet</em>';
    } else {
        for (const r of aliveRoots) renderSubtree(r, 0);

        if (deadRoots.length > 0) {
            html += `<div style="padding:6px 12px;font-size:11px;color:#666;border-top:1px solid #333840;margin-top:4px">Dead / pruned</div>`;
            for (const r of deadRoots) renderSubtree(r, 0);
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
            drawFromSnapshot(currentSnapshot, allCandles, selectedNodeId);
        });
    });

    // Toggle handlers — clicking the chevron (▸/▾) collapses/expands a subtree.
    // stopPropagation prevents the card's own select-handler from firing.
    el.nodeList.querySelectorAll('.tree-toggle[data-collapse-id]').forEach(btn => {
        btn.addEventListener('click', e => {
            e.stopPropagation();
            const nid = btn.dataset.collapseId;
            if (collapsedNodeIds.has(nid)) collapsedNodeIds.delete(nid);
            else collapsedNodeIds.add(nid);
            // Update the affected card's class in place — no full re-render needed.
            const card = el.nodeList.querySelector(`.node-card[data-node-id="${CSS.escape(nid)}"]`);
            if (card) applyCollapsedClass(card, collapsedNodeIds.has(nid));
            // Hide/show direct descendants.
            toggleDescendants(nid, collapsedNodeIds.has(nid));
        });
    });

    // Auto-select the best node (default: first alive root) and draw it
    const pick = (bestNodeId && nodeMap[bestNodeId])
        ? bestNodeId
        : (aliveRoots[0] ? aliveRoots[0].id : (allRoots[0] ? allRoots[0].id : null));
    if (pick) {
        const card = el.nodeList.querySelector(`[data-node-id="${CSS.escape(pick)}"]`);
        if (card) card.classList.add('selected');
        selectedNodeId = pick;
        drawFromSnapshot(currentSnapshot, allCandles, selectedNodeId);
    }

    // Keep the newest branches visible: scroll the panel to the bottom
    el.nodeList.scrollTop = el.nodeList.scrollHeight;
}

function nodeHtml(snapshot, n, alive, depth, collapsed, hidden) {
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

    const hasKids = (n.children || []).length > 0;
    const indent = depth * 14;
    const isRoot = n.wavePos === 'root' || n.parentId === null;

    // Chevron: ▾ (down) when expanded, ▸ (right) when collapsed. The toggle
    // is rendered for any node that has children — clicking it folds/unfolds
    // the subtree. Leaves get a hidden placeholder so the badge column aligns.
    let toggleHtml;
    if (hasKids) {
        const glyph = collapsed ? '▸' : '▾';
        toggleHtml = `<span class="tree-toggle${collapsed ? ' collapsed' : ''}" data-collapse-id="${escHtml(n.id)}" title="Collapse/expand children">${glyph}</span>`;
    } else {
        toggleHtml = '<span class="tree-toggle leaf" aria-hidden="true">·</span>';
    }

    const hiddenAttr = hidden ? ' node-hidden' : '';
    const collapsedAttr = collapsed ? ' node-collapsed' : '';

    return `
    <div class="node-card ${alive ? '' : 'node-dead'}${isRoot ? ' node-root' : ''}${collapsedAttr}${hiddenAttr}"
         data-node-id="${escHtml(n.id)}"
         data-parent-id="${n.parentId ? escHtml(n.parentId) : ''}"
         style="margin-left:${indent}px">
      ${depth > 0 ? `<div class="tree-guide" style="left:${-indent - 2}px;height:${hasKids ? '100%' : '50%'}"></div>` : ''}
      <div>
        ${toggleHtml}
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

/** Apply/unapply the visual + state class to a node card. */
function applyCollapsedClass(card, collapsed) {
    card.classList.toggle('node-collapsed', collapsed);
    const btn = card.querySelector('.tree-toggle[data-collapse-id]');
    if (btn) {
        btn.classList.toggle('collapsed', collapsed);
        btn.textContent = collapsed ? '▸' : '▾';
    }
}

/**
 * Walk forward in DOM order toggling `node-hidden` on direct children of `parentId`.
 * Uses a depth counter: a hidden parent's grandchildren stay hidden when it
 * re-expands (so we only flip cards whose nearest non-hidden ancestor changes).
 */
function toggleDescendants(parentId, hide) {
    let node = el.nodeList.querySelector(`.node-card[data-node-id="${CSS.escape(parentId)}"]`);
    if (!node) return;
    let cursor = node.nextElementSibling;
    while (cursor && cursor.classList.contains('node-card')) {
        // Stop walking once we leave this subtree. We check by walking the
        // parentId chain: if the cursor's nearest ancestor is no longer
        // `parentId` (or anything under it), we've stepped out.
        if (!isStrictDescendantOf(parentId, cursor, el.nodeList)) break;

        cursor.classList.toggle('node-hidden', hide);
        // When hiding, also hide its own descendants so the collapse is recursive.
        // When showing, we leave nested collapsed-state intact (user's choice).
        if (hide) hideAllDescendants(cursor);
        cursor = cursor.nextElementSibling;
    }
}

function hideAllDescendants(card) {
    let cursor = card.nextElementSibling;
    while (cursor && cursor.classList.contains('node-card')) {
        if (!isStrictDescendantOf(card.dataset.nodeId, cursor, el.nodeList)) break;
        cursor.classList.add('node-hidden');
        cursor = cursor.nextElementSibling;
    }
}

/**
 * True if `cursor` (a card) sits in the subtree rooted at the card with id
 * `ancestorId`. Walks the parentId chain via the rendered cards' data attrs.
 */
function isStrictDescendantOf(ancestorId, cursor, root) {
    let pid = cursor.dataset.parentId;
    while (pid) {
        if (pid === ancestorId) return true;
        const parent = root.querySelector(`.node-card[data-node-id="${CSS.escape(pid)}"]`);
        if (!parent) return false;
        pid = parent.dataset.parentId;
    }
    return false;
}

