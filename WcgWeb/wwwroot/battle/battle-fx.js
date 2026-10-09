// Procedural, original battle effects. Everything here is decorative: no game command, lock or
// snapshot depends on it, every node is pointer-events:none, and nothing is awaited by the
// presentation pipeline unless a caller explicitly chooses to wait for a short, bounded beat.
export const MOTION_KEY = 'wcg.motion';
const MAX_PARTICLES = 90;

function storageOf(env) { try { return env.storage ?? globalThis.localStorage ?? null; } catch { return null; } }
function systemReduced(env) {
    try { return !!(env.matchMedia ?? globalThis.window?.matchMedia)?.call(globalThis.window, '(prefers-reduced-motion: reduce)')?.matches; }
    catch { return false; }
}
/** 'full' | 'reduced' | 'system' (follow the operating system preference). */
export function readMotionSetting(env = {}) {
    try { const value = storageOf(env)?.getItem(MOTION_KEY); return value === 'full' || value === 'reduced' ? value : 'system'; }
    catch { return 'system'; }
}
export function saveMotionSetting(value, env = {}) {
    const next = value === 'full' || value === 'reduced' ? value : 'system';
    try { const storage = storageOf(env); if (next === 'system') storage?.removeItem(MOTION_KEY); else storage?.setItem(MOTION_KEY, next); } catch { /* Session-only preference. */ }
    applyMotionAttribute(env);
    return next;
}
export function motionReduced(env = {}) {
    const setting = readMotionSetting(env);
    return setting === 'reduced' || (setting === 'system' && systemReduced(env));
}
/** Mirrors the effective preference on <html data-wcg-motion> so plain CSS can drop ambience. */
export function applyMotionAttribute(env = {}) {
    try { const html = (env.document ?? globalThis.document)?.documentElement; if (html?.setAttribute) html.setAttribute('data-wcg-motion', motionReduced(env) ? 'reduced' : 'full'); } catch { }
}

export function createFx(root, env = {}) {
    const doc = env.document ?? globalThis.document;
    const random = env.random ?? Math.random;
    const animations = new Set();
    let layer = null, disposed = false;
    const nodes = new Set();
    const reduced = () => motionReduced(env);
    function ensureLayer() {
        if (layer) return layer;
        layer = doc.createElement('div'); layer.className = 'wcg-fx-layer'; layer.setAttribute('aria-hidden', 'true');
        doc.body.append(layer); return layer;
    }
    function dropLayerIfIdle() { if (layer && nodes.size === 0) { layer.remove(); layer = null; } }
    function spawn(className, x, y, w = 0, h = 0, text = '') {
        const el = doc.createElement('div'); el.className = `wcg-fx ${className}`; if (text) el.textContent = text;
        Object.assign(el.style, { left: `${x - w / 2}px`, top: `${y - h / 2}px` });
        if (w) el.style.width = `${w}px`; if (h) el.style.height = `${h}px`;
        ensureLayer().append(el); nodes.add(el); return el;
    }
    async function run(el, frames, options, removeAfter = true) {
        if (disposed || !el?.animate) { if (removeAfter && el) { el.remove(); nodes.delete(el); } dropLayerIfIdle(); return; }
        const animation = el.animate(frames, { fill: 'forwards', ...options });
        animations.add(animation);
        try { await animation.finished; } catch { /* Disposal or a newer effect cancelled this one. */ }
        finally { animations.delete(animation); if (removeAfter) { el.remove(); nodes.delete(el); } dropLayerIfIdle(); }
    }
    const center = rect => ({ x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 });
    const rectOf = target => target?.getBoundingClientRect ? target.getBoundingClientRect() : target;

    /** Screen shake on the board; strength 0..1. Purely visual and cancelled by later shakes. */
    let shaking = null;
    function shake(strength = .4) {
        if (disposed || reduced() || !root?.animate) return Promise.resolve();
        const s = Math.max(0, Math.min(1, strength)), px = 2 + s * 9, frames = [];
        for (let i = 0; i < 7; i++) {
            const decay = 1 - i / 7, angle = random() * Math.PI * 2;
            frames.push({ transform: `translate(${(Math.cos(angle) * px * decay).toFixed(1)}px,${(Math.sin(angle) * px * decay).toFixed(1)}px) rotate(${((random() - .5) * s * 1.2 * decay).toFixed(2)}deg)` });
        }
        frames.unshift({ transform: 'translate(0,0)' }); frames.push({ transform: 'translate(0,0)' });
        shaking?.cancel();
        const animation = root.animate(frames, { duration: 180 + s * 200, easing: 'linear' });
        shaking = animation; animations.add(animation);
        return animation.finished.catch(() => {}).finally(() => { animations.delete(animation); if (shaking === animation) shaking = null; });
    }
    /** Radial dust/spark burst. */
    function burst(target, { color = '#ffe2a6', count = 14, spread = 90, size = 7, rise = 0, duration = 620, kind = 'spark', origin = 'center' } = {}) {
        if (disposed || reduced()) return Promise.resolve();
        const r = rectOf(target); if (!r) return Promise.resolve();
        const { x } = center(r), y = origin === 'bottom' ? r.y + r.height * .92 : r.y + r.height / 2, jobs = [];
        const n = Math.min(count, Math.max(0, MAX_PARTICLES - nodes.size));
        for (let i = 0; i < n; i++) {
            // Ground dust fans out sideways from under the card; sparks spray in every direction.
            const angle = origin === 'bottom' ? Math.PI + random() * Math.PI : random() * Math.PI * 2;
            const distance = spread * (.35 + random() * .65), s = size * (.5 + random());
            const sx = origin === 'bottom' ? (random() - .5) * r.width * .8 : 0;
            const el = spawn(`wcg-fx-${kind}`, x + sx, y, s, s);
            if (kind === 'dust') el.style.background = `radial-gradient(circle, ${color} 0%, ${color}aa 35%, transparent 70%)`;
            else { el.style.background = color; el.style.boxShadow = `0 0 ${Math.round(s * 1.6)}px ${color}`; }
            const dx = Math.cos(angle) * distance * (origin === 'bottom' ? 1.4 : 1), dy = Math.sin(angle) * distance * (origin === 'bottom' ? .35 : .7) - rise * (.5 + random());
            jobs.push(run(el, [
                { transform: 'translate(0,0) scale(1)', opacity: 1 },
                { transform: `translate(${(dx * .75).toFixed(1)}px,${(dy * .75).toFixed(1)}px) scale(.85)`, opacity: .9, offset: .55 },
                { transform: `translate(${dx.toFixed(1)}px,${(dy + (kind === 'dust' ? 10 : 0)).toFixed(1)}px) scale(.2)`, opacity: 0 }
            ], { duration: duration * (.7 + random() * .5), easing: 'cubic-bezier(.15,.7,.35,1)' }));
        }
        return Promise.all(jobs);
    }
    /** Expanding shock ring. */
    function ring(target, color = '#ffe2a6', size = 1) {
        if (disposed || reduced()) return Promise.resolve();
        const r = rectOf(target); if (!r) return Promise.resolve();
        const { x, y } = center(r), d = Math.max(r.width, r.height) * .9 * size;
        const el = spawn('wcg-fx-ring', x, y, d, d); el.style.borderColor = color; el.style.boxShadow = `0 0 24px ${color}, inset 0 0 18px ${color}`;
        return run(el, [{ transform: 'scale(.35)', opacity: .95 }, { transform: 'scale(1.25)', opacity: 0 }], { duration: 480, easing: 'cubic-bezier(.2,.8,.3,1)' });
    }
    /** Brief additive flash covering a card or portrait. */
    function glow(target, color = '#ffffff', duration = 260) {
        if (disposed || reduced()) return Promise.resolve();
        const r = rectOf(target); if (!r) return Promise.resolve();
        const el = spawn('wcg-fx-glow', r.x + r.width / 2, r.y + r.height / 2, r.width, r.height);
        el.style.background = `radial-gradient(ellipse at center, ${color}cc, ${color}33 55%, transparent 75%)`;
        return run(el, [{ opacity: 0 }, { opacity: 1, offset: .2 }, { opacity: 0 }], { duration, easing: 'ease-out' });
    }
    /** Knock a visible element away from a point and back (hit reaction / recoil). */
    function recoil(el, from = null, strength = .5) {
        if (disposed || reduced() || !el?.animate) return Promise.resolve();
        const r = el.getBoundingClientRect(); let dx = 0, dy = -1;
        if (from) { const a = center(rectOf(from)), b = center(r); const len = Math.hypot(b.x - a.x, b.y - a.y) || 1; dx = (b.x - a.x) / len; dy = (b.y - a.y) / len; }
        const px = 6 + strength * 12;
        const animation = el.animate([
            { transform: 'translate(0,0)', filter: 'brightness(1)' },
            { transform: `translate(${(dx * px).toFixed(1)}px,${(dy * px).toFixed(1)}px) rotate(${((random() - .5) * 6 * strength).toFixed(1)}deg)`, filter: 'brightness(1.8) saturate(1.3)', offset: .25 },
            { transform: 'translate(0,0)', filter: 'brightness(1)' }
        ], { duration: 320, easing: 'cubic-bezier(.2,.7,.3,1)', composite: 'add' });
        animations.add(animation);
        return animation.finished.catch(() => {}).finally(() => animations.delete(animation));
    }
    /** Break a card clone into shards that fall and fade. */
    function shatter(source, rect = null) {
        if (disposed || !source) return Promise.resolve();
        const r = rect ?? source.getBoundingClientRect();
        if (reduced()) return Promise.resolve();
        const cols = 3, rows = 3, jobs = [];
        for (let row = 0; row < rows; row++) for (let col = 0; col < cols; col++) {
            const shard = source.cloneNode(true);
            const x0 = col / cols * 100, x1 = (col + 1) / cols * 100, y0 = row / rows * 100, y1 = (row + 1) / rows * 100;
            const jx = () => (random() - .5) * 12, jy = () => (random() - .5) * 12;
            shard.removeAttribute?.('data-target'); shard.removeAttribute?.('data-drag-id'); shard.removeAttribute?.('data-drag-kind');
            Object.assign(shard.style, { position: 'fixed', left: `${r.x}px`, top: `${r.y}px`, width: `${r.width}px`, height: `${r.height}px`, margin: '0', transform: 'none', transition: 'none', visibility: 'visible',
                clipPath: `polygon(${x0 + jx()}% ${y0 + jy()}%, ${x1 + jx()}% ${y0 + jy()}%, ${x1 + jx()}% ${y1 + jy()}%, ${x0 + jx()}% ${y1 + jy()}%)` });
            shard.classList?.add('wcg-fx-shard'); ensureLayer().append(shard); nodes.add(shard);
            const cx = (col - 1) * (30 + random() * 40), cy = (row - 1) * 25 + 40 + random() * 60;
            jobs.push(run(shard, [
                { transform: 'translate(0,0) rotate(0)', opacity: 1, filter: 'brightness(1.6)' },
                { transform: `translate(${(cx * .25).toFixed(1)}px,${(cy * .1 - 8).toFixed(1)}px) rotate(${((random() - .5) * 10).toFixed(1)}deg)`, opacity: 1, filter: 'brightness(1.2)', offset: .25 },
                { transform: `translate(${cx.toFixed(1)}px,${cy.toFixed(1)}px) rotate(${((random() - .5) * 70).toFixed(1)}deg) scale(.7)`, opacity: 0, filter: 'brightness(.6)' }
            ], { duration: 640 + random() * 180, easing: 'cubic-bezier(.3,.1,.6,1)' }));
        }
        void burst(r, { color: '#cfc6b8', count: 10, spread: 70, size: 5, kind: 'dust' });
        return Promise.all(jobs);
    }
    /** Center-screen banner such as 「你的回合」. Never captures input. */
    function banner(text, tone = 'player', duration = 1150) {
        if (disposed) return Promise.resolve();
        const board = root?.getBoundingClientRect?.() ?? { x: 0, y: 0, width: 800, height: 600 };
        const el = spawn(`wcg-fx-banner ${tone}`, board.x + board.width / 2, board.y + board.height * .46, Math.min(560, board.width * .8), 92);
        const inner = doc.createElement('span'); inner.textContent = text; el.append(inner);
        if (reduced()) return run(el, [{ opacity: 0 }, { opacity: 1, offset: .15 }, { opacity: 1, offset: .8 }, { opacity: 0 }], { duration: Math.min(duration, 700) });
        return run(el, [
            { transform: 'scale(1.6)', opacity: 0, letterSpacing: '.6em' },
            { transform: 'scale(.96)', opacity: 1, letterSpacing: '.18em', offset: .18 },
            { transform: 'scale(1)', opacity: 1, letterSpacing: '.22em', offset: .78 },
            { transform: 'scale(1.06)', opacity: 0, letterSpacing: '.3em' }
        ], { duration, easing: 'cubic-bezier(.2,.8,.3,1)' });
    }
    /** Short landing "slam": dust, ring and shake scaled by card cost. */
    function slam(target, cost = 1) {
        const power = Math.max(0, Math.min(1, (Number(cost) || 0) / 7));
        void ring(target, power > .55 ? '#ffd27a' : '#cfe8ff', .9 + power * .5);
        void burst(target, { color: '#e2d6bd', count: 12 + Math.round(power * 16), spread: 70 + power * 80, size: 18 + power * 14, kind: 'dust', origin: 'bottom', duration: 720 });
        if (power > .4) void burst(target, { color: '#ffd9a0', count: 6 + Math.round(power * 8), spread: 50 + power * 40, size: 5, origin: 'bottom', rise: 30 });
        if (power >= .28) void shake(.15 + power * .7);
        return Promise.resolve();
    }
    /** Four-point flare at a contact point. */
    function flare(target, color = '#fff2cf', scale = 1) {
        if (disposed || reduced()) return Promise.resolve();
        const r = rectOf(target); if (!r) return Promise.resolve();
        const d = Math.min(r.width, r.height) * .95 * scale, el = spawn('wcg-fx-flare', r.x + r.width / 2, r.y + r.height / 2, d, d);
        el.style.background = `radial-gradient(circle, #ffffff 0%, ${color} 30%, transparent 70%)`;
        return run(el, [{ transform: 'scale(.2) rotate(0deg)', opacity: 1 }, { transform: 'scale(1.15) rotate(25deg)', opacity: .95, offset: .3 }, { transform: 'scale(1.4) rotate(45deg)', opacity: 0 }], { duration: 380, easing: 'cubic-bezier(.2,.8,.3,1)' });
    }
    function impact(target, from = null, power = .5) {
        void flare(target, '#ffd59a', .6 + power * .5);
        void ring(target, '#ffcf8a', .55 + power * .4);
        void burst(target, { color: '#ffd59a', count: 14 + Math.round(power * 14), spread: 70 + power * 70, size: 7 + power * 4 });
        void glow(target, '#ffefd0', 220);
        void shake(.2 + power * .6);
        const el = target?.getBoundingClientRect ? target : null;
        if (el) void recoil(el, from, power);
        return Promise.resolve();
    }
    function clear() {
        for (const animation of animations) { try { animation.cancel(); } catch { } }
        animations.clear(); shaking = null; nodes.clear(); layer?.remove(); layer = null;
    }
    return { shake, burst, ring, glow, flare, recoil, shatter, banner, slam, impact, clear, reduced,
        get active() { return animations.size; }, dispose() { disposed = true; clear(); } };
}
