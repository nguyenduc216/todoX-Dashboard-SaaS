let currentKey, current, root, handler;
let restored = new WeakSet();
const read = key => { try { return JSON.parse(sessionStorage.getItem(key)) || {}; } catch { return {}; } };
const write = () => { try { sessionStorage.setItem(currentKey, JSON.stringify(current)); } catch {} };
function capture() {
    if (!root || !current) return;
    root.querySelectorAll('[data-rvideo-scroll]').forEach(el => {
        if (el.clientHeight > 0) current.scroll[el.dataset.rvideoScroll] = el.scrollTop;
    });
    write();
}
export function open(key, tab) {
    detach(); restored = new WeakSet(); currentKey = key;
    const saved = read(key);
    current = { tab: Number.isInteger(saved.tab) ? Math.max(0, Math.min(2, saved.tab)) : tab, scroll: saved.scroll || {} };
    return current.tab;
}
export function attach(key, tab) {
    if (key !== currentKey) return;
    const next = document.getElementById('rvideo-view-root');
    if (!next) return;
    if (root !== next) {
        if (root && handler) root.removeEventListener('scroll', handler, true);
        root = next;
        handler = event => {
            const name = event.target.dataset?.rvideoScroll;
            if (name) { current.scroll[name] = event.target.scrollTop; write(); }
        };
        root.addEventListener('scroll', handler, true);
    }
    root.querySelectorAll('[data-rvideo-scroll]').forEach(el => {
        if (!el.clientHeight || restored.has(el)) return;
        const top = Number(current.scroll[el.dataset.rvideoScroll]) || 0;
        el.scrollTop = Math.max(0, Math.min(top, el.scrollHeight - el.clientHeight));
        restored.add(el);
    });
}
export function saveTab(key, tab) {
    if (key !== currentKey) return;
    capture(); current.tab = tab; write();
}
export function detach() {
    capture();
    if (root && handler) root.removeEventListener('scroll', handler, true);
    root = null; handler = null;
}
