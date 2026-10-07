// Kenney CC0 samples, bundled locally. Audio never blocks a game command or presentation.
export const CUES = {
    select: ['card-slide-1.ogg', .65], flip: ['card-fan-1.ogg', .6],
    place: ['card-place-1.ogg', .8], energy: ['card-shove-1.ogg', .65], draw: ['card-slide-4.ogg', .55],
    attack: ['impactSoft_heavy_000.ogg', .75], damage: ['impactPunch_medium_000.ogg', .7], death: ['impactGlass_light_003.ogg', .55],
    click: ['click_001.ogg', .45], spell: ['glass_001.ogg', .6], heal: ['confirmation_001.ogg', .5],
    turn: ['confirmation_002.ogg', .45], gameover: ['confirmation_004.ogg', .55],
    cancel: ['back_001.ogg', .4], error: ['error_002.ogg', .4], trigger: ['pluck_001.ogg', .45]
};
const EVENT_CUES = { play:'place',summon:'place',set:'place',energy:'energy',draw:'draw',take:'draw',recover:'draw',bounce:'draw',attack:'attack',damage:'damage',heal:'heal',death:'death',turn:'turn',gameover:'gameover',trigger:'trigger',status:'trigger',reveal:'flip' };
const KEY = 'wcg.soundEnabled';
export function createBattleAudio(root = null, env = {}) {
    const fetcher = env.fetch ?? (url => fetch(url));
    const factory = env.contextFactory ?? (() => new (window.AudioContext ?? window.webkitAudioContext)());
    const hidden = env.hidden ?? (() => document.hidden);
    const now = env.now ?? (() => performance.now());
    let enabled = true, disposed = false, context = null, master = null;
    const buffers = new Map(), encoded = new Map(), voices = new Set(), last = new Map();
    let storage;
    try { storage = env.storage ?? window.localStorage; enabled = storage.getItem(KEY) !== 'false'; } catch { /* Private storage still permits session-local sound. */ }
    for (const [file] of Object.values(CUES)) {
        encoded.set(file, Promise.resolve().then(() => fetcher(new URL(`./audio/${file}`, import.meta.url).href))
            .then(response => response.ok ? response.arrayBuffer() : null).catch(() => null));
    }
    function unlock() {
        if (disposed || !enabled) return;
        try {
            if (!context) {
                context = factory(); master = context.createGain(); master.gain.value = .32; master.connect(context.destination);
                for (const [file, data] of encoded) buffers.set(file, data.then(bytes => bytes ? context.decodeAudioData(bytes.slice(0)) : null).catch(() => null));
            }
            if (context.state === 'suspended') void context.resume().catch(() => {});
        } catch { /* Unsupported or blocked audio leaves gameplay fully usable. */ }
    }
    function stop() {
        for (const voice of voices) { try { voice.stop(); } catch { } }
        voices.clear(); last.clear();
    }
    function setEnabled(value) {
        enabled = !!value;
        try { storage?.setItem(KEY, String(enabled)); } catch { }
        if (!enabled) stop(); else unlock();
        return enabled;
    }
    async function play(key, delay = 0) {
        if (disposed || !enabled || hidden()) return;
        const cue = CUES[key] ?? CUES[EVENT_CUES[key]];
        if (!cue) return;
        unlock(); if (!context) return;
        const time = now(); if (time - (last.get(cue[0]) ?? -Infinity) < 65) return;
        last.set(cue[0], time);
        const buffer = await buffers.get(cue[0]);
        if (!buffer || disposed || !enabled || hidden() || context.state !== 'running') return;
        if (voices.size >= 6) { const oldest = voices.values().next().value; try { oldest.stop(); } catch { } voices.delete(oldest); }
        const source = context.createBufferSource(), gain = context.createGain();
        source.buffer = buffer; gain.gain.value = cue[1]; source.connect(gain); gain.connect(master); voices.add(source);
        source.onended = () => { voices.delete(source); source.disconnect(); gain.disconnect(); };
        source.start(context.currentTime + Math.max(0, Math.min(1, delay)));
    }
    function input(e) {
        unlock();
        if (e.type === 'keydown' && (!['Enter', ' '].includes(e.key) || e.repeat)) return;
        if (e.target?.closest('[data-sound-toggle]')) return;
        if (e.target?.closest('.v06-hand-card,.field-card')) void play(e.button === 2 ? 'flip' : 'select');
        else if (e.target?.closest('button')) void play('click');
    }
    root?.addEventListener('pointerdown', input, true); root?.addEventListener('keydown', input, true);
    if (env.activated ?? (typeof navigator !== 'undefined' && navigator.userActivation?.hasBeenActive)) unlock();
    return {
        get enabled() { return enabled; }, unlock, play, setEnabled,
        toggle() { return setEnabled(!enabled); },
        dispose() {
            disposed = true; stop(); root?.removeEventListener('pointerdown', input, true); root?.removeEventListener('keydown', input, true);
            if (context) void context.close().catch(() => {});
        }
    };
}
