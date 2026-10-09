// Battle-board adapter over the site-wide sound manager (wcg-audio.js). Audio never blocks a game command or presentation.
import { CUES, ALIASES, createAudioManager, getAudio } from './wcg-audio.js';
export { CUES };
export function createBattleAudio(root = null, env = null) {
    // Tests inject a private manager; the page shares one manager (and its settings) with every other screen.
    const owned = !!env;
    const audio = owned ? createAudioManager(env) : getAudio();
    let disposed = false;
    if (owned && env.activated) audio.unlock();
    const play = (key, delay = 0, options) => disposed ? Promise.resolve() : audio.play(key, delay, options);
    function input(e) {
        audio.unlock();
        if (e.type === 'keydown' && (!['Enter', ' '].includes(e.key) || e.repeat)) return;
        const t = e.target;
        if (!t?.closest || t.closest('[data-sound-toggle]') || t.closest('button:disabled')) return;
        const tagged = t.closest('[data-sfx]');
        if (tagged) void play(tagged.dataset.sfx);
        else if (t.closest('.v06-hand-card,.field-card')) void play(e.button === 2 ? 'flip' : 'select');
        else if (t.closest('.end-turn')) void play('turn');
        else if (t.closest('.close-dialog')) void play('cancel');
        else if (t.closest('button')) void play('button');
    }
    root?.addEventListener('pointerdown', input, true); root?.addEventListener('keydown', input, true);
    if (!owned && typeof navigator !== 'undefined' && navigator.userActivation?.hasBeenActive) audio.unlock();
    return {
        get enabled() { return audio.enabled; }, unlock: () => audio.unlock(), play,
        setEnabled: value => audio.setEnabled(value),
        toggle() { return audio.setEnabled(!audio.enabled); },
        dispose() {
            disposed = true; audio.stop('battle');
            root?.removeEventListener('pointerdown', input, true); root?.removeEventListener('keydown', input, true);
            if (owned) audio.close();
        }
    };
}
export { ALIASES };
