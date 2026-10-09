// Central sound manager for the whole site. Kenney CC0 samples are bundled locally (see audio/CREDITS.md);
// audio never blocks navigation, a game command or a presentation, and every failure is silent.
// Cue: [file, gain, category]. Categories mix into master; master/UI/battle volumes and the mute switch persist.
export const CUES = {
    // Interface
    hover: ['rollover2.ogg', .22, 'ui'], click: ['click_001.ogg', .45, 'ui'], nav: ['bookFlip2.ogg', .5, 'ui'],
    tab: ['select_001.ogg', .5, 'ui'], toggleOn: ['toggle_001.ogg', .5, 'ui'], toggleOff: ['toggle_002.ogg', .5, 'ui'],
    back: ['back_001.ogg', .4, 'ui'], deckAdd: ['card-place-2.ogg', .6, 'ui'], deckRemove: ['card-slide-2.ogg', .55, 'ui'],
    save: ['confirmation_002.ogg', .5, 'ui'], toastSuccess: ['confirmation_001.ogg', .4, 'ui'], toastError: ['error_002.ogg', .4, 'ui'],
    modalOpen: ['open_002.ogg', .35, 'ui'], modalClose: ['close_002.ogg', .3, 'ui'], avatar: ['pluck_002.ogg', .55, 'ui'],
    matchFound: ['jingles_PIZZI10.ogg', .5, 'ui'], matchStart: ['drawKnife1.ogg', .55, 'ui'],
    win: ['jingles_PIZZI02.ogg', .55, 'ui'], lose: ['jingles_PIZZI01.ogg', .5, 'ui'],
    rankUp: ['jingles_PIZZI04.ogg', .6, 'ui'], rankDown: ['jingles_PIZZI05.ogg', .5, 'ui'],
    starGain: ['glass_002.ogg', .55, 'ui'], starLoss: ['minimize_005.ogg', .45, 'ui'],
    // Battle
    select: ['card-slide-1.ogg', .65, 'battle'], flip: ['card-fan-1.ogg', .6, 'battle'],
    place: ['card-place-1.ogg', .8, 'battle'], set: ['card-shove-2.ogg', .6, 'battle'], field: ['impactPlate_light_000.ogg', .55, 'battle'],
    spell: ['glass_001.ogg', .6, 'battle'], summon: ['impactWood_medium_000.ogg', .6, 'battle'],
    energy: ['card-shove-1.ogg', .65, 'battle'], energyFill: ['chips-stack-1.ogg', .5, 'battle'], draw: ['card-slide-4.ogg', .55, 'battle'],
    swing: ['knifeSlice.ogg', .35, 'battle'], attack: ['impactSoft_heavy_000.ogg', .75, 'battle'],
    damage: ['impactPunch_medium_000.ogg', .7, 'battle'], faceHit: ['impactPunch_heavy_001.ogg', .7, 'battle'],
    death: ['impactGlass_medium_001.ogg', .55, 'battle'], heal: ['maximize_004.ogg', .45, 'battle'],
    shieldBreak: ['impactGlass_heavy_002.ogg', .6, 'battle'], taunt: ['impactMetal_heavy_001.ogg', .45, 'battle'],
    trigger: ['pluck_001.ogg', .45, 'battle'], turn: ['impactWood_heavy_000.ogg', .65, 'battle'], timer: ['tick_001.ogg', .5, 'battle'],
    victory: ['jingles_PIZZI02.ogg', .6, 'battle'], defeat: ['jingles_PIZZI01.ogg', .55, 'battle'],
    cancel: ['back_001.ogg', .4, 'battle'], error: ['error_002.ogg', .4, 'battle'], button: ['click_001.ogg', .45, 'battle'],
    gameover: ['confirmation_004.ogg', .55, 'battle'], glass: ['impactGlass_light_003.ogg', .55, 'battle']
};
export const ALIASES = { play: 'place', recover: 'draw', take: 'draw', bounce: 'draw', status: 'trigger', reveal: 'flip' };
export const CATEGORIES = ['ui', 'battle'];
const ENABLED_KEY = 'wcg.soundEnabled', VOLUME_KEY = 'wcg.audio';
export const DEFAULT_VOLUMES = Object.freeze({ master: .8, ui: .6, battle: .9 });
const BASE = .4, VOICES = { ui: 4, battle: 6 };
const clamp = v => Math.max(0, Math.min(1, Number.isFinite(+v) ? +v : 0));

export function createAudioManager(env = {}) {
    const fetcher = env.fetch ?? (url => fetch(url));
    const factory = env.contextFactory ?? (() => new (globalThis.AudioContext ?? globalThis.webkitAudioContext)());
    const hidden = env.hidden ?? (() => typeof document !== 'undefined' && document.hidden);
    const now = env.now ?? (() => performance.now());
    const base = env.base ?? new URL('./audio/', import.meta.url).href;
    let enabled = true, closed = false, context = null, master = null;
    const volumes = { ...DEFAULT_VOLUMES }, buses = new Map(), buffers = new Map(), encoded = new Map(), voices = new Map(), last = new Map(), listeners = new Set();
    let storage = null;
    try {
        storage = env.storage ?? globalThis.localStorage;
        enabled = storage.getItem(ENABLED_KEY) !== 'false';
        const saved = JSON.parse(storage.getItem(VOLUME_KEY) ?? 'null');
        if (saved && typeof saved === 'object') for (const k of Object.keys(DEFAULT_VOLUMES)) if (k in saved) volumes[k] = clamp(saved[k]);
    } catch { /* Private storage still permits session-local sound. */ }
    for (const [file] of Object.values(CUES)) {
        if (encoded.has(file)) continue;
        encoded.set(file, Promise.resolve().then(() => fetcher(new URL(file, base).href))
            .then(response => response?.ok ? response.arrayBuffer() : null).catch(() => null));
    }
    const notify = () => { for (const fn of listeners) { try { fn(settings()); } catch { } } };
    function applyGains() {
        if (!master) return;
        try { master.gain.value = BASE * volumes.master; for (const [cat, bus] of buses) bus.gain.value = volumes[cat] ?? 1; } catch { }
    }
    function unlock() {
        if (closed || !enabled) return;
        try {
            if (!context) {
                context = factory(); master = context.createGain(); master.connect(context.destination);
                for (const cat of CATEGORIES) { const bus = context.createGain(); bus.connect(master); buses.set(cat, bus); }
                applyGains();
                for (const [file, data] of encoded) buffers.set(file, data.then(bytes => bytes ? context.decodeAudioData(bytes.slice(0)) : null).catch(() => null));
            }
            if (context.state === 'suspended') void context.resume().catch(() => {});
        } catch { /* Unsupported or blocked audio leaves the site fully usable. */ }
    }
    function stop(category = null) {
        for (const [cat, set] of voices) {
            if (category && cat !== category) continue;
            for (const voice of set) { try { voice.stop(); } catch { } }
            set.clear();
        }
        if (!category) last.clear();
    }
    function persist() { try { storage?.setItem(VOLUME_KEY, JSON.stringify(volumes)); } catch { } }
    function setEnabled(value) {
        enabled = !!value;
        try { storage?.setItem(ENABLED_KEY, String(enabled)); } catch { }
        if (!enabled) stop(); else unlock();
        notify(); return enabled;
    }
    function setVolume(category, value) {
        if (!(category in volumes)) return settings();
        volumes[category] = clamp(value); applyGains(); persist(); notify(); return settings();
    }
    function settings() { return { enabled, master: volumes.master, ui: volumes.ui, battle: volumes.battle }; }
    async function play(key, delay = 0, options = {}) {
        if (closed || !enabled || hidden()) return;
        const cue = CUES[key] ?? CUES[ALIASES[key]];
        if (!cue) return;
        const [file, gainValue, category] = cue;
        if ((volumes[category] ?? 1) <= 0 || volumes.master <= 0) return;
        unlock(); if (!context) return;
        const time = now(); if (time - (last.get(file) ?? -Infinity) < (options.gap ?? 65)) return;
        last.set(file, time);
        const buffer = await buffers.get(file);
        if (!buffer || closed || !enabled || hidden() || context.state !== 'running') return;
        let set = voices.get(category); if (!set) voices.set(category, set = new Set());
        if (set.size >= (VOICES[category] ?? 4)) { const oldest = set.values().next().value; try { oldest.stop(); } catch { } set.delete(oldest); }
        const source = context.createBufferSource(), gain = context.createGain();
        source.buffer = buffer; gain.gain.value = gainValue * (options.volume ?? 1);
        if (options.detune && source.playbackRate) source.playbackRate.value = 1 + (Math.random() * 2 - 1) * options.detune;
        source.connect(gain); gain.connect(buses.get(category) ?? master); set.add(source);
        source.onended = () => { set.delete(source); source.disconnect(); gain.disconnect(); };
        source.start(context.currentTime + Math.max(0, Math.min(2, delay)));
        // Opt-in trace for automated checks (set globalThis.__wcgAudioTrace = [] in a test page).
        if (Array.isArray(globalThis.__wcgAudioTrace)) globalThis.__wcgAudioTrace.push(key);
    }
    return {
        get enabled() { return enabled; }, get volumes() { return { ...volumes }; },
        unlock, play, stop, setEnabled, setVolume, settings,
        subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
        close() { closed = true; stop(); listeners.clear(); if (context) void context.close().catch(() => {}); }
    };
}

let shared = null;
/** The page-wide manager shared by the battle board, settings and page interface sounds. */
export function getAudio() {
    if (shared) return shared;
    shared = createAudioManager();
    if (typeof document !== 'undefined') {
        const first = () => shared.unlock();
        for (const type of ['pointerdown', 'keydown', 'touchend']) document.addEventListener(type, first, { capture: true, passive: true });
        if (navigator.userActivation?.hasBeenActive) shared.unlock();
    }
    return shared;
}

// ---- Settings page bridge (Blazor JS interop) ----
export function readAudioSettings() { return getAudio().settings(); }
export function saveAudioEnabled(value) { return getAudio().setEnabled(!!value); }
export function saveAudioVolume(category, value) { return getAudio().setVolume(String(category), Number(value)); }
export function previewAudio(category) {
    const audio = getAudio(); audio.unlock();
    void audio.play(category === 'battle' ? 'attack' : category === 'ui' ? 'toastSuccess' : 'starGain', .05);
}

// ---- Page-wide interface sounds ----
// The battle board has its own input sounds; everything inside it is left to the board.
const BOARD = '.v06-board';
const DIALOG = '[role="dialog"],.modal.show';
const HOVERABLE = '.btn,.nav-link,.filter-chip,.segmented button,[role="radio"],.setting-switch';
function clickCue(target) {
    const tagged = target.closest('[data-sfx]');
    if (tagged) return tagged.dataset.sfx || null;
    if (target.closest('.nav-link,a[href]:not([href^="http"])')) return target.closest('.nav-link') ? 'nav' : 'click';
    const sw = target.closest('[role="switch"],input[type="checkbox"]');
    if (sw) { const on = sw.matches('input') ? !sw.checked : sw.getAttribute('aria-checked') !== 'true'; return on ? 'toggleOn' : 'toggleOff'; }
    if (target.closest('[role="tab"],[role="radio"],.segmented button,.filter-chip,input[type="radio"]')) return 'tab';
    if (target.closest('.btn-close,.close-dialog,.wcg-toast-close')) return 'back';
    if (target.closest('button,.btn,[role="button"],summary,select')) return 'click';
    return null;
}
function disabled(el) { return !!el.closest('button:disabled,[aria-disabled="true"],.disabled'); }
function showCues(el) { return (el.dataset.sfxShow ?? '').split(/\s+/).filter(Boolean); }

export function installGlobalAudio() {
    if (typeof document === 'undefined') return false;
    if (globalThis.__wcgGlobalAudio) return true;
    globalThis.__wcgGlobalAudio = true;
    const audio = getAudio();
    let hovered = null, hoverAt = 0;
    document.addEventListener('pointerdown', e => {
        if (e.button > 0 || !(e.target instanceof Element) || e.target.closest(BOARD) || disabled(e.target)) return;
        const cue = clickCue(e.target); if (cue) void audio.play(cue, 0, { detune: cue === 'click' ? .03 : 0 });
    }, true);
    document.addEventListener('keydown', e => {
        if (e.repeat || !['Enter', ' '].includes(e.key) || !(e.target instanceof Element) || e.target.closest(BOARD) || disabled(e.target)) return;
        if (e.target.matches('input:not([type=checkbox]):not([type=radio]),textarea')) return;
        const cue = clickCue(e.target); if (cue) void audio.play(cue);
    }, true);
    document.addEventListener('pointerover', e => {
        if (e.pointerType !== 'mouse' || !(e.target instanceof Element) || e.target.closest(BOARD)) return;
        const el = e.target.closest(HOVERABLE);
        if (!el || el === hovered || disabled(el)) { if (!el) hovered = null; return; }
        hovered = el; const t = performance.now(); if (t - hoverAt < 90) return; hoverAt = t;
        void audio.play('hover', 0, { detune: .04, gap: 90 });
    }, true);
    addEventListener('popstate', () => void audio.play('nav'));
    const seen = new WeakSet();
    const visit = (added, el) => {
        if (!(el instanceof Element)) return;
        const all = [el, ...el.querySelectorAll('[data-sfx-show],.wcg-toast,' + DIALOG)];
        for (const node of all) {
            if (node.closest(BOARD) && !node.matches('[data-sfx-show]')) continue;
            if (added) {
                if (seen.has(node)) continue;
                if (node.matches('[data-sfx-show]')) { seen.add(node); showCues(node).forEach((cue, i) => void audio.play(cue, 1.1 + i * .8)); }
                else if (node.matches('.wcg-toast') && !node.matches('[data-sfx-quiet]')) { seen.add(node); void audio.play(node.classList.contains('error') ? 'toastError' : 'toastSuccess'); }
                else if (node.matches(DIALOG)) { seen.add(node); void audio.play('modalOpen'); }
            } else if (node.matches(DIALOG) && seen.has(node)) { seen.delete(node); void audio.play('modalClose'); }
        }
    };
    new MutationObserver(records => {
        for (const r of records) { for (const n of r.addedNodes) visit(true, n); for (const n of r.removedNodes) visit(false, n); }
    }).observe(document.body, { childList: true, subtree: true });
    return true;
}
