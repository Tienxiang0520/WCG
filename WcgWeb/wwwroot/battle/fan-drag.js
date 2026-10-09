import { createBattleAudio } from "./battle-audio.js";
import { bindFeedback } from "./battle-feedback.js";
import { applyMotionAttribute, createFx, motionReduced, readMotionSetting, saveMotionSetting } from "./battle-fx.js";

// All gestures share the same aiming, destination feedback and cancellation lifecycle.
export function bind(root, dotnet) {
    const sound = createBattleAudio(root);
    const fx = createFx(root);
    const feedback = bindFeedback(root, sound, fx);
    applyMotionAttribute();
    // Soft tick when the end-turn button starts suggesting the turn is done (no legal moves left).
    const endTurnWatch = new MutationObserver(records => {
        for (const r of records) if (r.target.classList?.contains('end-turn') && r.target.classList.contains('no-moves') && !String(r.oldValue ?? '').includes('no-moves')) void sound.play('timer');
    });
    endTurnWatch.observe(root, { subtree: true, attributes: true, attributeFilter: ['class'], attributeOldValue: true });
    let gesture = null, aim = null, hovered = null;
    let pendingDrop = null, flight = null;
    let frame = 0, suppressClick = false, disposed = false;
    const call = (method, ...args) => { if (!disposed) return dotnet.invokeMethodAsync(method, ...args).catch(() => {}); };
    function visibleOrigin(source) {
        const rect = source.getBoundingClientRect(), board = root.getBoundingClientRect();
        // Resting hand cards extend below the board; aim from their visible portion.
        return { x: (Math.max(0, board.left, rect.left) + Math.min(innerWidth, board.right, rect.right)) / 2,
            y: (Math.max(0, board.top, rect.top) + Math.min(innerHeight, board.bottom, rect.bottom)) / 2 };
    }
    function clearHover() { hovered?.classList.remove('drop-hover', 'drop-rejected'); hovered?.removeAttribute('data-drop-preview'); hovered = null; }
    // Cursor-following tilt and sheen for the hovered hand card (mouse only; touch keeps the plain lift).
    let tilted = null;
    function untilt() { if (!tilted) return; for (const name of ['--tilt-x', '--tilt-y', '--shine-x', '--shine-y']) tilted.style.removeProperty(name); tilted = null; }
    function hoverTilt(e) {
        if (gesture?.started || e.pointerType !== 'mouse' || motionReduced()) { untilt(); return; }
        const card = e.target.closest?.('.v06-hand-card');
        if (card !== tilted) untilt();
        if (!card || !root.contains(card)) return;
        const r = card.getBoundingClientRect(), px = (e.clientX - r.left) / r.width - .5, py = (e.clientY - r.top) / r.height - .5;
        card.style.setProperty('--tilt-x', `${(-py * 14).toFixed(2)}deg`); card.style.setProperty('--tilt-y', `${(px * 16).toFixed(2)}deg`);
        card.style.setProperty('--shine-x', `${((px + .5) * 100).toFixed(1)}%`); card.style.setProperty('--shine-y', `${((py + .5) * 100).toFixed(1)}%`);
        tilted = card;
    }
    function cleanup() {
        cancelAnimationFrame(frame); frame = 0; clearHover();
        aim?.remove(); root.classList.remove('aiming');
        aim = gesture = null;
    }
    function down(e) {
        if (flight || e.button !== 0 || e.target.closest('.modal-shade')) return;
        const source = e.target.closest('[data-drag-kind]');
        if (!source || !root.contains(source) || !source.dataset.dragKind) return;
        const origin = visibleOrigin(source);
        gesture = { id: source.dataset.dragId, kind: source.dataset.dragKind, source,
            x: e.clientX, y: e.clientY, originX: origin.x, originY: origin.y,
            currentX: e.clientX, currentY: e.clientY, pointer: e.pointerId, started: false };
    }
    function destinationAt(x, y) {
        // Skip the dragged card itself: on phones the lifted card can cover the energy zone next to the hand.
        const source = gesture?.source;
        const hit = document.elementsFromPoint(x, y).find(el => !source?.contains(el));
        const destination = hit?.closest('[data-drop-zone]');
        return destination && root.contains(destination) ? destination : null;
    }
    function draw() {
        if (!gesture?.started) return;
        const { currentX: px, currentY: py } = gesture;
        const { x: originX, y: originY } = visibleOrigin(gesture.source);
        const destination = destinationAt(px, py);
        const valid = destination?.classList.contains('legal') ?? false;
        if (hovered !== destination) { clearHover(); hovered = destination; }
        hovered?.classList.toggle('drop-hover', valid);
        hovered?.classList.toggle('drop-rejected', !valid);
        if (hovered && valid && hovered.dataset.dropZone === 'own' && !hovered.dataset.target)
            { const n = Number(hovered.dataset.slot) + 1, set = gesture.source.classList.contains('card-back');
                hovered.dataset.dropPreview = document.documentElement.lang === 'en' ? `Slot ${n} · ${set ? 'Set face-down, 0 cost' : 'Enter'}` : `第 ${n} 格 · ${set ? '蓋牌 0費' : '進場'}`; }
        else hovered?.removeAttribute('data-drop-preview');
        const color = valid ? '#9cf4bc' : destination ? '#ff8c81' : '#ffce79';
        aim.dataset.state = valid ? 'valid' : destination ? 'invalid' : 'free';
        for (const path of aim.querySelectorAll('path[data-aim]')) path.setAttribute('stroke', color);
        aim.querySelector('marker path').setAttribute('fill', color);
        // The card stays at its origin while aiming; the arrow follows the pointer and snaps onto a legal target
        // (a hero's portrait, or the centre of a card/slot) so the lock-on reads clearly under a finger.
        let x = gesture.currentX, y = gesture.currentY;
        if (valid && hovered) { const r = (hovered.querySelector?.('.hero-face') ?? hovered).getBoundingClientRect(); x = r.x + r.width / 2; y = r.y + r.height / 2; }
        const d = `M ${originX} ${originY} Q ${originX} ${originY + (y - originY) * .65} ${x} ${y}`;
        for (const path of aim.querySelectorAll('path[data-aim]')) path.setAttribute('d', d);
        const reticle = aim.querySelector('[data-reticle]');
        reticle.setAttribute('transform', `translate(${x} ${y})`); reticle.setAttribute('stroke', color);
        frame = requestAnimationFrame(draw);
    }
    function start(e) {
        gesture.started = true; pendingDrop = null;
        document.getSelection()?.removeAllRanges();
        try { root.setPointerCapture(e.pointerId); } catch { /* Capture may be unavailable after an interrupted gesture. */ }
        aim = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        aim.setAttribute('aria-hidden', 'true'); aim.setAttribute('data-wcg-aim', gesture.kind);
        Object.assign(aim.style, { position: 'fixed', inset: '0', width: '100vw', height: '100vh', pointerEvents: 'none', zIndex: '2147482999', filter: 'drop-shadow(0 2px 4px #000a)' });
        aim.setAttribute('class', 'wcg-aim');
        // Glow underlay + flowing dashed core + a reticle that locks on when hovering a legal drop.
        aim.innerHTML = '<defs><marker id="wcg-aim-tip" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 Z" fill="#ffce79" /></marker></defs>'
            + '<path data-aim class="wcg-aim-glow" fill="none" stroke="#ffce79" stroke-width="12" stroke-linecap="round" />'
            + '<path data-aim class="wcg-aim-core" fill="none" stroke="#ffce79" stroke-width="5" stroke-linecap="round" marker-end="url(#wcg-aim-tip)" />'
            + '<g data-reticle class="wcg-aim-reticle" fill="none" stroke="#ffce79" stroke-width="3"><g class="wcg-aim-spin"><circle r="22" stroke-dasharray="26 8.5" /><path d="M -32 0 H -15 M 15 0 H 32 M 0 -32 V -15 M 0 15 V 32" /></g></g>';
        document.body.append(aim); root.classList.add('aiming'); untilt();
        call('BeginDrag', gesture.kind, gesture.id);
        draw();
    }
    function move(e) {
        if (!gesture || gesture.pointer !== e.pointerId) return;
        gesture.currentX = e.clientX; gesture.currentY = e.clientY;
        if (!gesture.started && Math.hypot(e.clientX - gesture.x, e.clientY - gesture.y) < 8) return;
        e.preventDefault();
        if (!gesture.started) start(e);
    }
    function up(e) {
        if (!gesture || gesture.pointer !== e.pointerId) return;
        if (!gesture.started) { cleanup(); return; }
        e.preventDefault(); suppressClick = true;
        const destination = destinationAt(e.clientX, e.clientY);
        const { kind, id } = gesture;
        // Capture first; the managed rules decide whether this drop can animate.
        if (destination) {
            pendingDrop = { source: gesture.source, clone: gesture.source.cloneNode(true), kind,
                from: gesture.source.getBoundingClientRect(), to: (destination.querySelector?.('.hero-face') ?? destination).getBoundingClientRect(), zone: destination.dataset.dropZone };
        }
        if (destination) call('DropCard', kind, id, destination.dataset.dropZone, Number(destination.dataset.slot ?? -1), destination.dataset.target ?? '');
        else call('EndDrag');
        cleanup();
        setTimeout(() => { suppressClick = false; }, 0);
    }
    function cancel() { if (gesture?.started) { void sound.play('cancel'); call('EndDrag'); } cleanup(); }
    function click(e) { if (suppressClick) { e.preventDefault(); e.stopImmediatePropagation(); suppressClick = false; } }
    function key(e) { if (e.key === 'Escape') cancel(); }
    function leave() { untilt(); }
    // Touch: pressing a tucked hand card raises it fully until the finger lifts (tap still opens the zoom).
    let lifted = null;
    function liftDown(e) {
        if (e.pointerType === 'mouse' || e.target.closest?.('.modal-shade')) return;
        const card = e.target.closest?.('.v06-hand-card');
        if (!card || !root.contains(card)) return;
        lifted?.classList.remove('lifted'); lifted = card; card.classList.add('lifted');
    }
    function liftUp() { lifted?.classList.remove('lifted'); lifted = null; }
    // Size the board to the area the browser actually shows (address bar and navigation bar excluded).
    const html = document.documentElement, viewport = globalThis.visualViewport;
    function fitViewport() {
        const h = viewport && Math.abs((viewport.scale ?? 1) - 1) < .01 ? viewport.height : innerHeight;
        if (h > 0) { html.style.setProperty('--wcg-vh', `${Math.round(h)}px`); html.dataset.wcgVh = ''; }
    }
    fitViewport(); viewport?.addEventListener('resize', fitViewport); addEventListener('resize', fitViewport);
    root.addEventListener('pointerdown', liftDown); document.addEventListener('pointerup', liftUp); document.addEventListener('pointercancel', liftUp);
    root.addEventListener('pointerdown', down); root.addEventListener('click', click, true);
    root.addEventListener('pointermove', hoverTilt); root.addEventListener('pointerleave', leave);
    document.addEventListener('pointermove', move, { passive: false }); document.addEventListener('pointerup', up, { passive: false });
    document.addEventListener('pointercancel', cancel); document.addEventListener('keydown', key); window.addEventListener('blur', cancel);
    function discardDrop() { pendingDrop = null; }
    async function playDrop() {
        const drop = pendingDrop; pendingDrop = null;
        if (!drop || disposed || flight) return;
        const { source, clone, from, to, kind, zone } = drop;
        clone.removeAttribute('tabindex'); clone.setAttribute('aria-hidden', 'true');
        clone.dataset.wcgFlight = kind;
        clone.removeAttribute('data-drag-kind'); clone.removeAttribute('data-drag-id');
        Object.assign(clone.style, { position: 'fixed', left: `${from.x}px`, top: `${from.y}px`, bottom: 'auto',
            width: `${from.width}px`, height: `${from.height}px`, margin: '0', transform: 'none',
            transformOrigin: 'center', transition: 'none', pointerEvents: 'none', zIndex: '2147482998',
            opacity: '1', boxShadow: '0 18px 35px #000b' });
        const dx = to.x + to.width / 2 - (from.x + from.width / 2);
        const dy = to.y + to.height / 2 - (from.y + from.height / 2);
        const attack = kind === 'attack';
        const landing = zone === 'own' ? Math.min(1, to.width / from.width, to.height / from.height) : zone === 'energy' ? .35 : .72;
        const arrive = `translate(${dx}px, ${dy}px) scale(${attack ? 1.05 : landing})`;
        const lift = Math.min(110, Math.abs(dy) * .3 + 40), spin = zone === 'energy' ? -160 : dx > 0 ? 5 : -5;
        const visibility = source.style.visibility, inert = root.inert;
        source.style.visibility = 'hidden'; root.inert = true;
        document.body.append(clone);
        const reduced = motionReduced();
        const frames = reduced ? (attack ? [
            { transform: 'translate(0, 0) scale(1)' }, { transform: arrive }, { transform: 'translate(0, 0) scale(1)' }
        ] : [
            { transform: 'translate(0, 0) scale(1)', opacity: 1 },
            { transform: arrive, opacity: zone === 'own' ? 1 : 0 }
        ]) : attack ? [
            // Wind-up, lunge to contact, short hold for the hit, recoil home.
            { transform: 'translate(0, 0) scale(1) rotate(0deg)', offset: 0 },
            { transform: `translate(${-dx * .07}px, ${-dy * .07}px) scale(1.1) rotate(${dx > 0 ? -3 : 3}deg)`, offset: .2, easing: 'cubic-bezier(.55,0,.8,.3)' },
            { transform: arrive, offset: .48 },
            { transform: arrive, offset: .58 },
            { transform: 'translate(0, 0) scale(1)', offset: 1 }
        ] : [
            // Lift out of the hand along an arc, then slam down slightly oversized.
            { transform: 'translate(0, 0) scale(1) rotate(0deg)', opacity: 1, offset: 0 },
            { transform: `translate(${dx * .4}px, ${dy * .4 - lift}px) scale(${zone === 'own' ? 1.15 : 1}) rotate(${spin * .5}deg)`, opacity: 1, offset: .42 },
            { transform: `translate(${dx}px, ${dy}px) scale(${attack ? 1.05 : landing * (zone === 'own' ? 1.08 : 1)}) rotate(${zone === 'energy' ? spin : 0}deg)`, opacity: zone === 'own' ? 1 : .35, offset: .85 },
            { transform: arrive, opacity: zone === 'own' ? 1 : 0, offset: 1 }
        ];
        const duration = reduced ? 120 : attack ? 420 : 340;
        const animation = clone.animate(frames, { duration, easing: 'cubic-bezier(.2,.7,.3,1)', fill: 'forwards' });
        const spell = !source.classList.contains('card-back') && source.dataset.cardType?.startsWith('法術');
        const fieldCard = !source.classList.contains('card-back') && source.dataset.cardType?.includes('結界');
        if (attack) void sound.play('swing');
        void sound.play(attack ? 'attack' : zone === 'energy' ? 'energy' : source.classList.contains('card-back') ? 'set' : spell ? 'spell' : fieldCard ? 'field' : 'place', attack ? .18 : .25);
        // Decorative contact effects; the drop itself is still resolved by the engine afterwards.
        const contact = setTimeout(() => {
            if (disposed) return;
            if (attack) fx.impact(to, from, .55);
            else if (zone === 'energy') { fx.burst(to, { color: '#8ad7ff', count: 12, spread: 50, size: 6, rise: 20 }); fx.glow(to, '#8ad7ff'); }
            else if (zone !== 'own') { fx.ring(to, '#b9e6ff', .6); fx.burst(to, { color: '#bfe9ff', count: 14, spread: 70, size: 6 }); }
        }, reduced ? 0 : duration * (attack ? .48 : .85));
        flight = { animation, clone, restore() { clearTimeout(contact); source.style.visibility = visibility; root.inert = inert; clone.remove(); } };
        try { await animation.finished; } catch { /* Disposal cancels presentation without committing another action. */ }
        finally { flight?.restore(); flight = null; }
    }
    return { readSound: () => sound.enabled, toggleSound: () => sound.toggle(),
        readMotion: () => motionReduced(),
        // Cycles to the opposite of the current effective state and remembers it on this device.
        toggleMotion: () => { saveMotionSetting(motionReduced() ? 'full' : 'reduced'); if (motionReduced()) fx.clear(); return motionReduced(); },
        motionSetting: () => readMotionSetting(), playError: () => { void sound.play("error"); }, playDrop, discardDrop, presentEvents: feedback.present, clearEvents: feedback.clear, dispose() { disposed = true; endTurnWatch.disconnect(); feedback.dispose(); fx.dispose(); untilt(); root.removeEventListener('pointermove', hoverTilt); root.removeEventListener('pointerleave', leave); sound.dispose(); pendingDrop = null; flight?.animation.cancel(); flight?.restore(); flight = null; cleanup(); root.removeEventListener('pointerdown', down); root.removeEventListener('pointerdown', liftDown); document.removeEventListener('pointerup', liftUp); document.removeEventListener('pointercancel', liftUp); liftUp(); viewport?.removeEventListener('resize', fitViewport); removeEventListener('resize', fitViewport); html.style.removeProperty('--wcg-vh'); delete html.dataset.wcgVh; root.removeEventListener('click', click, true); document.removeEventListener('pointermove', move); document.removeEventListener('pointerup', up); document.removeEventListener('pointercancel', cancel); document.removeEventListener('keydown', key); window.removeEventListener('blur', cancel); } };
}
