// Saves the page's parameter controls in a cookie (one cookie per page), so the
// next visit — even after the server restarts — starts with the last settings.
// Migration: ids that are not in the cookie keep their markup defaults, so a
// parameter added later starts from its default; a value that no longer fits the
// control (option gone, out of range) falls back to that default too.
(function () {
    // The CSV picker and the date range are re-derived from the selected file.
    const SKIP = new Set(['selFile', 'inpFromDate', 'inpToDate']);
    const NAME = 'rp_params' + location.pathname.replace(/[^\w]/g, '_');
    let last = '';
    let timer = null;

    const controls = () => Array.from(
        document.querySelectorAll('input, select, textarea'))
        .filter(el => el.id && !SKIP.has(el.id) && el.type !== 'button' && el.type !== 'submit');

    function read() {
        const m = document.cookie.match(new RegExp('(?:^|; )' + NAME + '=([^;]*)'));
        try { return m ? JSON.parse(decodeURIComponent(m[1])) : {}; }
        catch (_) { return {}; }   // unreadable cookie → all defaults
    }

    function restore() {
        const saved = read();
        for (const el of controls()) {
            const v = saved[el.id];
            if (v === undefined) continue;   // new parameter → markup default
            const dflt = el.type === 'checkbox' ? el.defaultChecked : el.value;
            try {
                if (el.type === 'checkbox') {
                    el.checked = !!v;
                } else if (el.multiple) {
                    const want = new Set(Array.isArray(v) ? v : []);
                    if (Array.from(el.options).some(o => want.has(o.value)))
                        for (const o of el.options) o.selected = want.has(o.value);
                } else {
                    el.value = v;
                    if (el.value === '' && dflt !== '') el.value = dflt; // no longer valid
                }
            } catch (_) { /* keep the default */ }
        }
    }

    function snapshot() {
        const out = {};
        for (const el of controls())
            out[el.id] = el.type === 'checkbox' ? el.checked
                : el.multiple ? Array.from(el.selectedOptions).map(o => o.value)
                : el.value;
        return JSON.stringify(out);
    }

    function save() {
        const json = snapshot();
        if (json === last) return;
        last = json;
        document.cookie = NAME + '=' + encodeURIComponent(json) +
            '; path=/; max-age=31536000; SameSite=Lax';
    }

    restore();
    last = snapshot();   // what was just restored needs no write
    // 'input' covers typing and slider drags, 'click' the preset buttons that
    // assign values in JS without firing change.
    for (const ev of ['input', 'change', 'click'])
        document.addEventListener(ev, () => {
            clearTimeout(timer);
            timer = setTimeout(save, 300);
        });
})();
