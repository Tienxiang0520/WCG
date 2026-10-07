import { bindFeedback } from "./battle-feedback.js";

// All gestures share the same aiming, destination feedback and cancellation lifecycle.
export function bind(root, dotnet) {
    const feedback = bindFeedback(root);
    let gesture = null, aim = null, hovered = null;
    let pendingDrop = null, flight = null;
    let frame = 0, suppressClick = false, disposed = false;
    const call = (method, ...args) => { if (!disposed) return dotnet.invokeMethodAsync(method, ...args).catch(() => {}); };
    function clearHover() { hovered?.classList.remove('drop-hover', 'drop-rejected'); hovered?.removeAttribute('data-drop-preview'); hovered = null; }
    function cleanup() {
        cancelAnimationFrame(frame); frame = 0; clearHover();
        aim?.remove();
        aim = gesture = null;
    }
    function down(e) {
        if (flight || e.button !== 0 || e.target.closest('.modal-shade')) return;
        const source = e.target.closest('[data-drag-kind]');
        if (!source || !root.contains(source) || !source.dataset.dragKind) return;
        const rect = source.getBoundingClientRect();
        gesture = { id: source.dataset.dragId, kind: source.dataset.dragKind, source,
            x: e.clientX, y: e.clientY, originX: rect.x + rect.width / 2, originY: rect.y + rect.height / 2,
            currentX: e.clientX, currentY: e.clientY, pointer: e.pointerId, started: false };
    }
    function destinationAt(x, y) {
        const destination = document.elementFromPoint(x, y)?.closest('[data-drop-zone]');
        return destination && root.contains(destination) ? destination : null;
    }
    function draw() {
        if (!gesture?.started) return;
        const { currentX: x, currentY: y } = gesture;
        const rect = gesture.source.getBoundingClientRect();
        const originX = rect.x + rect.width / 2, originY = rect.y + rect.height / 2;
        const destination = destinationAt(x, y);
        const valid = destination?.classList.contains('legal') ?? false;
        if (hovered !== destination) { clearHover(); hovered = destination; }
        hovered?.classList.toggle('drop-hover', valid);
        hovered?.classList.toggle('drop-rejected', !valid);
        if (hovered && valid && hovered.dataset.dropZone === 'own' && !hovered.dataset.target)
            hovered.dataset.dropPreview = `第 ${Number(hovered.dataset.slot) + 1} 格 · ${gesture.source.classList.contains('card-back') ? '蓋牌 0費' : '進場'}`;
        else hovered?.removeAttribute('data-drop-preview');
        const color = valid ? '#9cf4bc' : destination ? '#ff8c81' : '#ffce79';
        aim.querySelector('path[data-aim]').setAttribute('stroke', color);
        aim.querySelector('marker path').setAttribute('fill', color);
        // The card stays at its origin while aiming; only the arrow follows the pointer.
        aim.querySelector('path[data-aim]').setAttribute('d', `M ${originX} ${originY} Q ${originX} ${originY + (y - originY) * .65} ${x} ${y}`);
        frame = requestAnimationFrame(draw);
    }
    function start(e) {
        gesture.started = true; pendingDrop = null;
        document.getSelection()?.removeAllRanges();
        try { root.setPointerCapture(e.pointerId); } catch { /* Capture may be unavailable after an interrupted gesture. */ }
        aim = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        aim.setAttribute('aria-hidden', 'true'); aim.setAttribute('data-wcg-aim', gesture.kind);
        Object.assign(aim.style, { position: 'fixed', inset: '0', width: '100vw', height: '100vh', pointerEvents: 'none', zIndex: '2147482999', filter: 'drop-shadow(0 2px 4px #000a)' });
        aim.innerHTML = '<defs><marker id="wcg-aim-tip" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 Z" fill="#ffce79" /></marker></defs><path data-aim fill="none" stroke="#ffce79" stroke-width="4" stroke-linecap="round" marker-end="url(#wcg-aim-tip)" />';
        document.body.append(aim);
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
                from: gesture.source.getBoundingClientRect(), to: destination.getBoundingClientRect(), zone: destination.dataset.dropZone };
        }
        if (destination) call('DropCard', kind, id, destination.dataset.dropZone, Number(destination.dataset.slot ?? -1), destination.dataset.target ?? '');
        else call('EndDrag');
        cleanup();
        setTimeout(() => { suppressClick = false; }, 0);
    }
    function cancel() { if (gesture?.started) call('EndDrag'); cleanup(); }
    function click(e) { if (suppressClick) { e.preventDefault(); e.stopImmediatePropagation(); suppressClick = false; } }
    function key(e) { if (e.key === 'Escape') cancel(); }
    root.addEventListener('pointerdown', down); root.addEventListener('click', click, true);
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
        const visibility = source.style.visibility, inert = root.inert;
        source.style.visibility = 'hidden'; root.inert = true;
        document.body.append(clone);
        const animation = clone.animate(attack ? [
            { transform: 'translate(0, 0) scale(1)', offset: 0 },
            { transform: arrive, offset: .5 },
            { transform: arrive, offset: .62 },
            { transform: 'translate(0, 0) scale(1)', offset: 1 }
        ] : [
            { transform: 'translate(0, 0) scale(1)', opacity: 1, offset: 0 },
            { transform: arrive, opacity: 1, offset: .8 },
            { transform: arrive, opacity: zone === 'own' ? 1 : 0, offset: 1 }
        ], { duration: attack ? 360 : 280, easing: 'cubic-bezier(.2,.7,.3,1)', fill: 'forwards' });
        flight = { animation, clone, restore() { source.style.visibility = visibility; root.inert = inert; clone.remove(); } };
        try { await animation.finished; } catch { /* Disposal cancels presentation without committing another action. */ }
        finally { flight?.restore(); flight = null; }
    }
    return { playDrop, discardDrop, presentEvents: feedback.present, clearEvents: feedback.clear, dispose() { disposed = true; feedback.dispose(); pendingDrop = null; flight?.animation.cancel(); flight?.restore(); flight = null; cleanup(); root.removeEventListener('pointerdown', down); root.removeEventListener('click', click, true); document.removeEventListener('pointermove', move); document.removeEventListener('pointerup', up); document.removeEventListener('pointercancel', cancel); document.removeEventListener('keydown', key); window.removeEventListener('blur', cancel); } };
}
