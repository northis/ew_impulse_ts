// ── Chart (TradingView Lightweight Charts) ──
const chart = LightweightCharts.createChart(
    document.getElementById('chartContainer'), {
    layout: {
        background: { color: '#1a1d23' },
        textColor: '#8892a0'
    },
    grid: {
        vertLines: { color: '#272b33' },
        horzLines: { color: '#272b33' }
    },
    crosshair: {
        mode: LightweightCharts.CrosshairMode.Normal,
        vertLine: { color: '#3D85C6' },
        horzLine: { color: '#3D85C6' }
    },
    rightPriceScale: {
        borderColor: '#333840',
        autoScale: true
    },
    timeScale: {
        borderColor: '#333840',
        timeVisible: true,
        secondsVisible: false
    }
});

// Candlestick series
const candleSeries = chart.addCandlestickSeries({
    upColor: '#26a69a', downColor: '#ef5350',
    borderUpColor: '#26a69a', borderDownColor: '#ef5350',
    wickUpColor: '#26a69a', wickDownColor: '#ef5350'
});

// Model → colour map
const MODEL_COLORS = {
    IMPULSE: '#3D85C6', SIMPLE_IMPULSE: '#3D85C6',
    DIAGONAL_CONTRACTING_INITIAL: '#3D85C6', DIAGONAL_CONTRACTING_ENDING: '#3D85C6',
    ZIGZAG: '#FF9800', DOUBLE_ZIGZAG: '#FF9800', TRIPLE_ZIGZAG: '#FF9800',
    TRIANGLE_CONTRACTING: '#787B86', TRIANGLE_RUNNING: '#787B86',
    TRIANGLE_EXPANDING: '#787B86',
    FLAT_EXTENDED: '#6AA84F', FLAT_RUNNING: '#6AA84F', FLAT_REGULAR: '#6AA84F'
};

// ── Helpers ──

// Absolute bar index → unix time (seconds). Rebuilt by setCandles so wave/pivot
// drawing works regardless of how the candle array is sliced.
let barTimeMap = new Map();

/** Unix timestamp (seconds) for an absolute bar index, or null if not revealed yet. */
function barTimeFor(barIndex) {
    const t = barTimeMap.get(barIndex);
    return t === undefined ? null : t;
}

/** Apply the price-axis precision detected from the CSV. */
function setPricePrecision(decimals) {
    const d = Math.max(0, Math.min(8, decimals | 0));
    candleSeries.applyOptions({
        priceFormat: { type: 'price', precision: d, minMove: Math.pow(10, -d) }
    });
}

/** Parse hex colour → rgba */
function rgba(hex, alpha) {
    const r = parseInt(hex.slice(1, 3), 16);
    const g = parseInt(hex.slice(3, 5), 16);
    const b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r},${g},${b},${alpha})`;
}

// ── Layers (destroyed between frames) ──
let layers = []; // { series } objects

function clearMarkup() {
    layers.forEach(l => chart.removeSeries(l));
    layers = [];
}

/** Replace the chart's candles from an accumulated, absolute-bar-indexed array. */
function setCandles(candles) {
    barTimeMap = new Map();
    const data = [];
    for (const c of candles) {
        const t = Math.floor(new Date(c.time).getTime() / 1000);
        barTimeMap.set(c.barIndex, t);
        data.push({ time: t, open: c.open, high: c.high, low: c.low, close: c.close });
    }
    data.sort((a, b) => a.time - b.time);
    candleSeries.setData(data);
    chart.timeScale().fitContent();
}

// ── Draw from snapshot ──

/** Build lookup: node.id → EwNodeDto */
function nodeMap(snapshot) {
    const m = {};
    if (!snapshot || !snapshot.nodes) return m;
    for (const n of snapshot.nodes) m[n.id] = n;
    return m;
}

/** Index zigzag pivot price by pivot index */
function zzPrice(snapshot, pivotIdx) {
    if (!snapshot || !snapshot.zigzag || pivotIdx < 0 || pivotIdx >= snapshot.zigzag.length)
        return null;
    return snapshot.zigzag[pivotIdx].price;
}

/** Absolute bar index of a zigzag pivot (by pivot index). */
function zzBar(snapshot, pivotIdx) {
    if (!snapshot || !snapshot.zigzag || pivotIdx < 0 || pivotIdx >= snapshot.zigzag.length)
        return -1;
    return snapshot.zigzag[pivotIdx].barIndex;
}

/**
 * Main entry: draw selected node + its children on the chart.
 * Called when user clicks a node card or auto-selects best node.
 */
function drawFromSnapshot(snapshot, candles, selectedNodeId) {
    clearMarkup();

    if (!snapshot || !snapshot.zigzag || !candles) return;

    const nm = nodeMap(snapshot);

    // 1. Draw pivots as small dot markers
    drawPivotMarkers(snapshot.zigzag, candles);

    // 2. Walk the selected node's tree and draw all child wave lines
    const root = nm[selectedNodeId];
    if (!root) return;

    drawNodeTree(root, nm, candles, snapshot);
}

/** Recursively draw a node + its children as wave lines */
function drawNodeTree(node, nm, candles, snapshot) {
    const color = MODEL_COLORS[node.model] || '#888';
    const isConfirmed = node.status === 'COMPLETE' || node.status === 'OPEN';

    // Draw this node's own line (from startPivot to endPivot)
    addWaveLine(node, candles, snapshot, color, isConfirmed);

    // Draw children
    if (node.children) {
        for (const childId of node.children) {
            const child = nm[childId];
            if (!child) continue;
            const childColor = MODEL_COLORS[child.model] || color;
            const childConfirmed = child.status === 'COMPLETE' || child.status === 'OPEN'
                || node.status === 'COMPLETE';
            addWaveLine(child, candles, snapshot, childColor, childConfirmed);

            // Recurse
            if (child.children && child.children.length > 0)
                drawNodeTree(child, nm, candles, snapshot);
        }
    }

    // PROJECTED: draw dashed continuation from last child end to this node end
    if (node.status === 'PROJECTED' && node.children && node.children.length > 0) {
        const lastChild = nm[node.children[node.children.length - 1]];
        if (lastChild) {
            addProjectionLine(lastChild.endPivot, node.endPivot, candles, snapshot, color);
        }
    }
}

function addWaveLine(node, candles, snapshot, color, isConfirmed) {
    const t1 = barTimeFor(zzBar(snapshot, node.startPivot));
    const t2 = barTimeFor(zzBar(snapshot, node.endPivot));
    if (!t1 || !t2) return;

    const p1 = zzPrice(snapshot, node.startPivot);
    const p2 = zzPrice(snapshot, node.endPivot);
    if (p1 == null || p2 == null) return;

    const s = chart.addLineSeries({
        color: rgba(color, isConfirmed ? 1 : 0.5),
        lineWidth: isConfirmed ? 2 : 1,
        lineStyle: isConfirmed ? 0 : 2, // 0=solid, 2=dotted
        lastValueVisible: false,
        priceLineVisible: false
    });
    s.setData([{ time: t1, value: p1 }, { time: t2, value: p2 }]);
    layers.push(s);
}

function addProjectionLine(fromPivot, toPivot, candles, snapshot, color) {
    const t1 = barTimeFor(zzBar(snapshot, fromPivot));
    const t2 = barTimeFor(zzBar(snapshot, toPivot));
    if (!t1 || !t2) return;

    const p1 = zzPrice(snapshot, fromPivot);
    const p2 = zzPrice(snapshot, toPivot);
    if (p1 == null || p2 == null) return;

    const s = chart.addLineSeries({
        color: rgba(color, 0.4),
        lineWidth: 1, lineStyle: 2, // dotted
        lastValueVisible: false, priceLineVisible: false
    });
    s.setData([{ time: t1, value: p1 }, { time: t2, value: p2 }]);
    layers.push(s);
}

function drawPivotMarkers(zz, candles) {
    if (!zz || zz.length === 0) return;
    const data = [];
    for (const p of zz) {
        const t = barTimeFor(p.barIndex);
        if (!t) continue;
        data.push({
            time: t,
            value: p.price,
            color: '#556677',
            shape: p.isHigh ? 'arrowDown' : 'arrowUp',
            size: 2,
            position: p.isHigh ? 'aboveBar' : 'belowBar'
        });
    }
    if (data.length > 0) {
        const s = chart.addLineSeries({
            color: '#333840', lineWidth: 1, lastValueVisible: false,
            priceLineVisible: false
        });
        s.setData(data.map(d => ({ time: d.time, value: d.value })));
        layers.push(s);
    }
}
