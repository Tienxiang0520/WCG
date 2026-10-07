// Pointer dragging keeps the floating card above all game layers. Clicks remain card details.
export function bind(root, dotnet) {
    let gesture = null, floating = null, aim = null, suppressClick = false, disposed = false;
    const call = (method, ...args) => { if (!disposed) return dotnet.invokeMethodAsync(method, ...args).catch(() => {}); };
    function cleanup() { floating?.remove(); floating = null; aim?.remove(); aim = null; gesture = null; }
    function down(e) {
        if (e.button !== 0 || e.target.closest('.modal-shade')) return;
        const source = e.target.closest('[data-drag-kind]');
        if (!source || !root.contains(source) || !source.dataset.dragKind) return;
        gesture = { id: source.dataset.dragId, kind: source.dataset.dragKind, source, x: e.clientX, y: e.clientY, pointer: e.pointerId, started: false };
    }
    function move(e) {
        if (!gesture || gesture.pointer !== e.pointerId) return;
        if (!gesture.started && Math.hypot(e.clientX - gesture.x, e.clientY - gesture.y) < 8) return;
        e.preventDefault();
        if (!gesture.started) {
            gesture.started = true;
            floating = gesture.source.cloneNode(true);
            floating.removeAttribute('tabindex'); floating.setAttribute('aria-hidden', 'true');
            Object.assign(floating.style, { position: 'fixed', left: '0', top: '0', bottom: 'auto', width: '160px', height: '218px', transform: 'none', zIndex: '2147483000', pointerEvents: 'none', margin: '0', opacity: '.95', transition: 'none', boxShadow: '0 18px 35px #000b' });
            document.body.append(floating);
            if (gesture.kind === 'attack') {
                floating.style.display = 'none';
                aim = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
                Object.assign(aim.style, { position: 'fixed', inset: '0', width: '100vw', height: '100vh', pointerEvents: 'none', zIndex: '2147482999' });
                aim.innerHTML = '<defs><marker id="wcg-attack-tip" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 Z" fill="#ffce79" /></marker></defs><line stroke="#ffce79" stroke-width="4" marker-end="url(#wcg-attack-tip)" />';
                const line = aim.querySelector('line'); line.setAttribute('x1', gesture.x); line.setAttribute('y1', gesture.y);
                document.body.append(aim);
            }
            call('BeginDrag', gesture.kind, gesture.id);
        }
        if (aim) { const line = aim.querySelector('line'); line.setAttribute('x2', e.clientX); line.setAttribute('y2', e.clientY); }
        floating.style.left = `${e.clientX - 80}px`; floating.style.top = `${e.clientY - 50}px`;
    }
    function up(e) {
        if (!gesture || gesture.pointer !== e.pointerId) return;
        if (!gesture.started) { cleanup(); return; }
        e.preventDefault(); suppressClick = true;
        const destination = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-drop-zone]');
        const { kind, id } = gesture;
        if (destination && root.contains(destination)) call('DropCard', kind, id, destination.dataset.dropZone, Number(destination.dataset.slot ?? -1), destination.dataset.target ?? '');
        else call('EndDrag');
        cleanup();
        // Release-generated click must never open a detail dialog after dragging.
        setTimeout(() => { suppressClick = false; }, 0);
    }
    function cancel() { if (gesture?.started) call('EndDrag'); cleanup(); }
    function click(e) { if (suppressClick) { e.preventDefault(); e.stopImmediatePropagation(); suppressClick = false; } }
    function key(e) { if (e.key === 'Escape') cancel(); }
    root.addEventListener('pointerdown', down);
    root.addEventListener('click', click, true);
    document.addEventListener('pointermove', move, { passive: false });
    document.addEventListener('pointerup', up, { passive: false });
    document.addEventListener('pointercancel', cancel);
    document.addEventListener('keydown', key);
    window.addEventListener('blur', cancel);
    return { dispose() { disposed = true; cleanup(); root.removeEventListener('pointerdown', down); root.removeEventListener('click', click, true); document.removeEventListener('pointermove', move); document.removeEventListener('pointerup', up); document.removeEventListener('pointercancel', cancel); document.removeEventListener('keydown', key); window.removeEventListener('blur', cancel); } };
}
