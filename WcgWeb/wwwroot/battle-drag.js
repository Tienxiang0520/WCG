// Pointer gestures stay local; all legality and mutations remain in the game engine.
export function attach(root, receiver) {
    let candidate = null, gesture = null, disposed = false, suppressClick = false;
    let latestZone = null, generation = 0, scrollFrame = null;
    function zoneAt(x, y) { return document.elementFromPoint(x, y)?.closest('[data-drop-zone]'); }
    function clean() {
        generation++;
        if (scrollFrame) cancelAnimationFrame(scrollFrame); scrollFrame = null;
        gesture?.ghost?.remove(); gesture?.hint?.remove();
        candidate?.source?.classList.remove('drag-origin');
        root.querySelectorAll('.drop-legal,.drop-hover').forEach(el => el.classList.remove('drop-legal', 'drop-hover'));
        gesture = null; candidate = null; latestZone = null;
    }
    function scrollTick() {
        if (!candidate || !gesture?.ghost) return;
        if (candidate.y < 85) window.scrollBy(0, -8);
        else if (candidate.y > innerHeight - 65) window.scrollBy(0, 8);
        moveGhost(candidate.x, candidate.y);
        scrollFrame = requestAnimationFrame(scrollTick);
    }
    function cancel() {
        const hadDrag = !!gesture;
        clean();
        setTimeout(() => { suppressClick = false; }, 350);
        if (hadDrag && !disposed) receiver.invokeMethodAsync('CancelDrag').catch(() => {});
    }
    async function begin(c) {
        const token = generation;
        gesture = { starting: true };
        suppressClick = true;
        try {
            const plan = await receiver.invokeMethodAsync('BeginDrag', c.kind, c.id);
            if (disposed || token !== generation || !candidate) return;
            if (!plan) { cancel(); return; }
            const ghost = document.createElement('div'); ghost.className = 'drag-ghost'; ghost.setAttribute('aria-hidden', 'true');
            const copy = c.source.cloneNode(true); copy.removeAttribute('id'); copy.removeAttribute('data-drag-kind');
            copy.classList.remove('chosen'); copy.querySelectorAll('[id]').forEach(x => x.removeAttribute('id'));
            ghost.append(copy); document.body.append(ghost);
            const hint = document.createElement('div'); hint.className = 'drag-instruction'; hint.textContent = plan.label; document.body.append(hint);
            gesture = { ghost, hint, zones: new Set(plan.zones) };
            c.source.classList.add('drag-origin');
            root.querySelectorAll('[data-drop-zone]').forEach(el => { if (gesture.zones.has(el.dataset.dropZone)) el.classList.add('drop-legal'); });
            moveGhost(c.x, c.y);
            scrollFrame = requestAnimationFrame(scrollTick);
        } catch { cancel(); }
    }
    function moveGhost(x, y) {
        if (!gesture?.ghost) return;
        gesture.ghost.style.left = `${x}px`; gesture.ghost.style.top = `${y}px`;
        const node = zoneAt(x, y), zone = node?.dataset.dropZone;
        const legal = gesture.zones.has(zone);
        root.querySelectorAll('.drop-hover').forEach(el => el.classList.remove('drop-hover'));
        if (legal) node.classList.add('drop-hover');
        gesture.ghost.classList.toggle('invalid-drop', !legal);
        const next = legal ? zone : null;
        if (next !== latestZone) {
            latestZone = next;
            receiver.invokeMethodAsync('HoverDrag', next).catch(() => {});
        }
    }
    function down(e) {
        if (!e.isPrimary || e.button !== 0 || candidate || gesture || !root.contains(e.target)) return;
        const source = e.target.closest('[data-drag-kind]');
        if (!source || e.target.closest('.card-detail-backdrop,.hand-selection,button:not(.field-action)')) return;
        candidate = { source, kind: source.dataset.dragKind, id: source.dataset.dragId, pointer: e.pointerId, startX: e.clientX, startY: e.clientY, x: e.clientX, y: e.clientY, touch: e.pointerType === 'touch' };
    }
    function move(e) {
        const c = candidate;
        if (!c || e.pointerId !== c.pointer) return;
        c.x = e.clientX; c.y = e.clientY;
        if (!gesture) {
            const dx = c.x - c.startX, dy = c.y - c.startY;
            if (Math.hypot(dx, dy) < (c.touch ? 12 : 8)) return;
            // Horizontal touch motion keeps the hand's native scrolling.
            if (c.touch && c.kind === 'hand' && Math.abs(dx) > Math.abs(dy)) { clean(); return; }
            void begin(c);
        }
        if (gesture) { e.preventDefault(); moveGhost(c.x, c.y); }
    }
    async function up(e) {
        if (!candidate || e.pointerId !== candidate.pointer) return;
        const c = candidate, dragging = !!gesture;
        const node = zoneAt(e.clientX, e.clientY), zone = node?.dataset.dropZone;
        const legal = gesture?.zones?.has(zone);
        clean();
        if (!dragging) return; // Let a short press follow the normal click path.
        e.preventDefault();
        setTimeout(() => { suppressClick = false; }, 350);
        if (legal) {
            try { await receiver.invokeMethodAsync('DropDrag', c.kind, c.id, zone); }
            catch { if (!disposed) receiver.invokeMethodAsync('CancelDrag').catch(() => {}); }
        } else if (!disposed) receiver.invokeMethodAsync('CancelDrag').catch(() => {});
    }
    function click(e) { if (suppressClick && e.detail !== 0) { e.preventDefault(); e.stopImmediatePropagation(); suppressClick = false; } }
    function key(e) { if (e.key === 'Escape' && (candidate || gesture)) { cancel(); suppressClick = false; } }
    const lost = () => { cancel(); suppressClick = false; };
    document.addEventListener('pointerdown', down, true);
    document.addEventListener('pointermove', move, { capture: true, passive: false });
    document.addEventListener('pointerup', up, true);
    document.addEventListener('pointercancel', lost, true);
    document.addEventListener('click', click, true);
    document.addEventListener('keydown', key, true);
    window.addEventListener('blur', lost);
    return { dispose() {
        disposed = true; clean();
        document.removeEventListener('pointerdown', down, true); document.removeEventListener('pointermove', move, true);
        document.removeEventListener('pointerup', up, true); document.removeEventListener('pointercancel', lost, true);
        document.removeEventListener('click', click, true); document.removeEventListener('keydown', key, true); window.removeEventListener('blur', lost);
    }};
}
