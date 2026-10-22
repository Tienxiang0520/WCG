// Phone gate: Soul Oath currently supports tablets and computers only.
// Rule (keep in sync with the .phone-gate media query in wcg.css): a touch-first device (primary pointer is coarse)
// whose shorter viewport side is under 600 CSS px. Phones are about 320–430 px on their short side in either
// orientation; the smallest tablets (iPad mini, 8" Android) are 600 px or more. Desktop windows (fine pointer)
// are never blocked, however small, so resizing a browser window cannot lock a computer out.
// On a phone the loading screen is replaced by a notice and the game download never starts.
(() => {
    'use strict';
    const QUERY = '(pointer: coarse) and (max-width: 599.98px), (pointer: coarse) and (max-height: 599.98px)';
    const isPhone = ({ width, height, coarse }) => !!coarse && Math.min(width, height) < 600;
    const english = (() => {
        try { const saved = globalThis.localStorage?.getItem('wcg.language'); if (saved === 'en' || saved === 'zh-Hant') return saved === 'en'; } catch { /* storage blocked */ }
        const first = String(globalThis.navigator?.languages?.find(x => String(x).trim()) ?? globalThis.navigator?.language ?? '').trim().toLowerCase();
        return first === 'en' || first.startsWith('en-');
    })();
    const message = english
        ? 'Soul Oath currently supports tablets and computers only. Please play on a larger screen.'
        : '本遊戲目前只支援平板與電腦，請改用大螢幕遊玩';
    const gate = { query: QUERY, isPhone, message, blocked: false };
    globalThis.wcgPhoneGate = gate;
    let media = null;
    try { media = globalThis.matchMedia?.(QUERY) ?? null; } catch { /* very old browser: never block */ }
    if (!media?.matches) return;
    gate.blocked = true;
    try {
        const app = globalThis.document?.getElementById('app');
        if (app) {
            app.innerHTML = '';
            const main = document.createElement('main'); main.className = 'startup phone-notice'; main.setAttribute('role', 'alert');
            const h1 = document.createElement('h1'); h1.textContent = english ? 'Soul Oath' : '魂誓';
            const p = document.createElement('p'); p.textContent = message;
            main.append(h1, p); app.append(main);
        }
        if (english) { document.documentElement.lang = 'en'; document.title = 'Soul Oath'; }
    } catch { /* partial DOM */ }
    // Moving to a larger screen (e.g. desktop emulation turned off) starts the game normally.
    media.addEventListener?.('change', event => { if (!event.matches) globalThis.location?.reload(); });
})();
