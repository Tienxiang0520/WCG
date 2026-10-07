// All gestures share the same aiming, destination feedback and cancellation lifecycle.
export function bind(root, dotnet) {
    let gesture = null, floating = null, aim = null, hint = null, hovered = null;
    let frame = 0, suppressClick = false, disposed = false;
    const call = (method, ...args) => { if (!disposed) return dotnet.invokeMethodAsync(method, ...args).catch(() => {}); };
    function clearHover() { hovered?.classList.remove('drop-hover', 'drop-rejected'); hovered = null; }
    function cleanup() {
        cancelAnimationFrame(frame); frame = 0; clearHover();
        gesture?.source.classList.remove('drag-source');
        floating?.remove(); aim?.remove(); hint?.remove();
        floating = aim = hint = gesture = null;
    }
    function down(e) {
        if (e.button !== 0 || e.target.closest('.modal-shade')) return;
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
        const { currentX: x, currentY: y, originX, originY } = gesture;
        const destination = destinationAt(x, y);
        const valid = destination?.classList.contains('legal') ?? false;
        if (hovered !== destination) { clearHover(); hovered = destination; }
        hovered?.classList.toggle('drop-hover', valid);
        hovered?.classList.toggle('drop-rejected', !valid);
        const color = valid ? '#9cf4bc' : destination ? '#ff8c81' : '#ffce79';
        aim.querySelector('path[data-aim]').setAttribute('stroke', color);
        aim.querySelector('marker path').setAttribute('fill', color);
        // Keep the tip at the pointer; the dragged card sits beside it rather than covering it.
        aim.querySelector('path[data-aim]').setAttribute('d', `M ${originX} ${originY} Q ${originX} ${originY + (y - originY) * .65} ${x} ${y}`);
        let label = '移到亮起的合法位置';
        if (destination && !valid) label = '不可放置 · 放開退回';
        if (valid) {
            const zone = destination.dataset.dropZone;
            label = gesture.kind === 'attack' ? '放開攻擊' : zone === 'energy' ? '放開填能量'
                : gesture.source.classList.contains('card-back') ? '放開蓋牌'
                : zone === 'own' && !destination.dataset.target ? '放開進場' : '放開施放';
        }
        hint.textContent = label;
        Object.assign(hint.style, { left: `${Math.max(8, Math.min(x + 22, window.innerWidth - 210))}px`, top: `${Math.max(8, y - 42)}px`, borderColor: color, color });
        if (floating) {
            floating.style.left = `${Math.max(0, Math.min(x + 26, window.innerWidth - 164))}px`;
            floating.style.top = `${Math.max(0, Math.min(y + 20, window.innerHeight - 222))}px`;
        }
        frame = requestAnimationFrame(draw);
    }
    function start(e) {
        gesture.started = true; gesture.source.classList.add('drag-source');
        document.getSelection()?.removeAllRanges();
        try { root.setPointerCapture(e.pointerId); } catch { /* Capture may be unavailable after an interrupted gesture. */ }
        if (gesture.kind !== 'attack') {
            floating = gesture.source.cloneNode(true);
            floating.classList.remove('drag-source'); floating.removeAttribute('tabindex'); floating.setAttribute('aria-hidden', 'true');
            Object.assign(floating.style, { position: 'fixed', left: '0', top: '0', bottom: 'auto', width: '160px', height: '218px', transform: 'none', zIndex: '2147482998', pointerEvents: 'none', margin: '0', opacity: '.85', transition: 'none', boxShadow: '0 18px 35px #000b' });
            document.body.append(floating);
        }
        aim = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        aim.setAttribute('aria-hidden', 'true'); aim.setAttribute('data-wcg-aim', gesture.kind);
        Object.assign(aim.style, { position: 'fixed', inset: '0', width: '100vw', height: '100vh', pointerEvents: 'none', zIndex: '2147482999', filter: 'drop-shadow(0 2px 4px #000a)' });
        aim.innerHTML = '<defs><marker id="wcg-aim-tip" markerWidth="8" markerHeight="8" refX="6" refY="3" orient="auto"><path d="M0,0 L0,6 L7,3 Z" fill="#ffce79" /></marker></defs><path data-aim fill="none" stroke="#ffce79" stroke-width="4" stroke-linecap="round" marker-end="url(#wcg-aim-tip)" />';
        hint = document.createElement('div'); hint.setAttribute('aria-hidden', 'true'); hint.dataset.wcgDragHint = '';
        Object.assign(hint.style, { position: 'fixed', zIndex: '2147483000', pointerEvents: 'none', padding: '7px 12px', background: '#10232eef', border: '1px solid #ffce79', borderRadius: '8px', font: 'bold 13px sans-serif', whiteSpace: 'nowrap' });
        document.body.append(aim, hint);
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
    return { dispose() { disposed = true; cleanup(); root.removeEventListener('pointerdown', down); root.removeEventListener('click', click, true); document.removeEventListener('pointermove', move); document.removeEventListener('pointerup', up); document.removeEventListener('pointercancel', cancel); document.removeEventListener('keydown', key); window.removeEventListener('blur', cancel); } };
}
