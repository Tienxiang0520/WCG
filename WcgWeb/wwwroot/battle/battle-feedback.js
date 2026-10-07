// Present only public engine events. Keep the old board until this batch completes.
export function bindFeedback(root, sound = null) {
    let layer = null, disposed = false, priorInert = false, locked = false;
    const animations = new Set(), hidden = new Map(), ghosts = new Map();
    const scope = root.getAttributeNames().find(name => name.startsWith('b-'));
    function node(tag, className, text = '') {
        const el = document.createElement(tag); el.className = className; el.textContent = text;
        if (scope) el.setAttribute(scope, '');
        return el;
    }
    function clear() {
        for (const animation of animations) animation.cancel(); animations.clear();
        for (const [el, visibility] of hidden) el.style.visibility = visibility;
        hidden.clear(); ghosts.clear(); layer?.remove(); layer = null;
        if (locked) root.inert = priorInert;
        locked = false;
    }
    function hide(el) {
        if (!el || hidden.has(el)) return;
        hidden.set(el, el.style.visibility); el.style.visibility = 'hidden';
    }
    async function animate(el, frames, duration) {
        if (disposed || !el) return;
        const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const animation = el.animate(frames, { duration: reduced ? Math.min(duration, 100) : duration, easing: 'ease-out', fill: 'forwards' });
        animations.add(animation);
        try { await animation.finished; } catch { /* Leaving the board cancels the visual batch. */ }
        finally { animations.delete(animation); }
    }
    function field(id) {
        return id ? [...root.querySelectorAll('[data-target]')].find(el => el.dataset.target === id)?.querySelector('.field-card') : null;
    }
    function hand(id) {
        return id ? [...root.querySelectorAll('.v06-hand-card')].find(el => el.dataset.dragId === id) : null;
    }
    function hero(side) { return root.querySelector(side === 'player' ? '.player.hero' : '.opponent.hero'); }
    function slot(side, index) { return root.querySelector(`[data-drop-zone="${side === 'player' ? 'own' : 'enemy'}"][data-slot="${index}"]`); }
    function visual(id) { return ghosts.get(id) ?? field(id); }
    function place(el, rect) {
        Object.assign(el.style, { position: 'fixed', left: `${rect.x}px`, top: `${rect.y}px`, width: `${rect.width}px`, height: `${rect.height}px`, bottom: 'auto', margin: '0', transform: 'none', transformOrigin: 'center', pointerEvents: 'none', transition: 'none' });
        layer.append(el); return el;
    }
    function publicCard(card) {
        const el = node('div', `event-card ${card ? '' : 'event-card-back'}`);
        // Card data comes exclusively from public BattleEvent payloads; never inspect AI hand data.
        if (!card) { el.append(node('strong', '', '魂 誓'), node('small', '', '背面卡片')); return el; }
        if (/^WCG-\d{3}$/.test(card.cardId)) {
            const art = node('div', 'card-art-layer'), image = node('img', 'card-illustration');
            image.src = `card-art/${card.cardId}.webp`; image.alt = ''; image.draggable = false;
            image.addEventListener('error', () => image.remove(), { once: true });
            art.append(image); el.append(art);
        }
        for (const direction of card.arrows ?? []) {
            if (['left', 'right', 'up'].includes(direction)) el.append(node('span', `arrowmark ${direction}`));
        }
        el.append(node('strong', '', card.name));
        if (card.pp != null) {
            const stats = node('div', 'field-stats');
            stats.append(node('b', '', String(card.pp)), node('b', '', String(card.dp))); el.append(stats);
        }
        return el;
    }
    function originCard(side, card) {
        const r = hero(side).getBoundingClientRect();
        const el = publicCard(card);
        if (side === 'computer') el.className += ' enemy-card';
        return place(el, { x: r.x + r.width / 2 - 80, y: r.y + r.height / 2 - 100, width: 160, height: 200 });
    }
    async function fly(el, destination, keep = false) {
        if (!el || !destination) { el?.remove(); return; }
        const from = el.getBoundingClientRect(), to = destination.getBoundingClientRect();
        const dx = to.x + to.width / 2 - from.x - from.width / 2;
        const dy = to.y + to.height / 2 - from.y - from.height / 2;
        const scale = keep ? Math.min(1, to.width / from.width, to.height / from.height) : .65;
        await animate(el, [{ transform: 'translate(0,0) scale(1)', opacity: 1 }, { transform: `translate(${dx}px,${dy}px) scale(${scale})`, opacity: keep ? 1 : 0 }], 460);
        if (!keep) el.remove();
    }
    async function flash(el, color = '#ff957b') {
        if (!el) return;
        const r = el.getBoundingClientRect(), glow = place(node('div', 'event-flash'), r);
        glow.style.borderColor = color;
        await animate(glow, [{ opacity: 0, boxShadow: `0 0 0 ${color}` }, { opacity: 1, boxShadow: `0 0 35px ${color}`, offset: .25 }, { opacity: 0, boxShadow: `0 0 5px ${color}` }], 260);
        glow.remove();
    }
    async function number(el, text, heal = false) {
        if (!el) return;
        const r = el.getBoundingClientRect();
        const pop = place(node('div', `event-number ${heal ? 'healing' : ''}`, text), { x: r.x + r.width / 2 - 45, y: r.y - 8, width: 90, height: 55 });
        void sound?.play(heal ? 'heal' : 'damage');
        pop.dataset.wcgEffect = heal ? 'heal' : 'damage';
        await Promise.all([flash(el, heal ? '#91ffc1' : '#ff957b'), animate(pop, [{ opacity: 0, transform: 'translateY(0) scale(.7)' }, { opacity: 1, transform: 'translateY(-10px) scale(1.15)', offset: .2 }, { opacity: 0, transform: 'translateY(-45px) scale(1)' }], 580)]);
        pop.remove();
    }
    async function attack(ev) {
        const source = visual(ev.instanceId), destination = ev.targetId ? visual(ev.targetId) : hero(ev.side === 'player' ? 'computer' : 'player');
        if (!source || !destination) return;
        if (ev.side === 'player') { await flash(destination); return; } // Player already lunged on release.
        const from = source.getBoundingClientRect(), to = destination.getBoundingClientRect();
        const moving = place(source.cloneNode(true), from); moving.dataset.wcgEffect = 'attack'; hide(source);
        const dx = to.x + to.width / 2 - from.x - from.width / 2, dy = to.y + to.height / 2 - from.y - from.height / 2;
        void sound?.play('attack', .23);
        await animate(moving, [{ transform: 'translate(0,0)' }, { transform: `translate(${dx}px,${dy}px) scale(1.06)`, offset: .5 }, { transform: 'translate(0,0)' }], 460);
        moving.remove(); source.style.visibility = hidden.get(source) ?? ''; hidden.delete(source);
        await flash(destination);
    }
    async function death(ev) {
        const source = visual(ev.instanceId);
        if (!source) return;
        const departing = place(source.cloneNode(true), source.getBoundingClientRect()); departing.dataset.wcgEffect = 'death'; hide(source);
        await flash(departing);
        void sound?.play('death');
        await animate(departing, [{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'translateY(35px) rotate(7deg) scale(.55)' }], 420);
        departing.remove(); ghosts.get(ev.instanceId)?.remove(); ghosts.delete(ev.instanceId);
    }
    async function present(previous, next, events) {
        clear(); if (disposed || !events.length) return;
        priorInert = root.inert; root.inert = true; locked = true;
        layer = node('div', 'battle-feedback-layer'); layer.setAttribute('aria-hidden', 'true'); document.body.append(layer);
        const messageRect = root.querySelector('.battle-message').getBoundingClientRect();
        const banner = place(node('div', 'event-banner'), messageRect);
        const played = new Set();
        // Next public field provides fixed destinations; old public field preserves departing sources.
        const locations = new Map([...previous.player.field.map(m => [m.card.instanceId, ['player', m.slot]]), ...previous.computer.field.map(m => [m.card.instanceId, ['computer', m.slot]]), ...next.player.field.map(m => [m.card.instanceId, ['player', m.slot]]), ...next.computer.field.map(m => [m.card.instanceId, ['computer', m.slot]])]);
        const destination = id => { const location = locations.get(id); return location ? slot(...location) : null; };
        try {
            for (const ev of [...events].sort((a, b) => a.order - b.order)) {
                if (disposed) break;
                layer.dataset.wcgEvent = ev.type; layer.dataset.wcgEventSide = ev.side;
                banner.textContent = `${ev.side === 'player' ? '我方' : '電腦'} · ${ev.label || ev.type}${ev.card ? ` · ${ev.card.name}` : ''}`;
                if (ev.type === 'attack') await attack(ev);
                else if (ev.type === 'damage' && ev.amount > 0) await number(hero(ev.side), `−${ev.amount}`);
                else if (ev.type === 'heal' && ev.amount > 0) await number(hero(ev.side), `+${ev.amount}`, true);
                else if (ev.type === 'death') await death(ev);
                else if (ev.type === 'play') {
                    hide(hand(ev.instanceId));
                    if (ev.side === 'computer') {
                        const isField = destination(ev.instanceId), moving = originCard(ev.side, ev.card);
                        void sound?.play(isField ? 'place' : 'spell', .35);
                        await fly(moving, isField ?? visual(ev.targetId) ?? root.querySelector('.battlefield'), !!isField);
                        if (isField) ghosts.set(ev.instanceId, moving);
                    }
                    played.add(ev.instanceId);
                } else if (ev.type === 'summon' || ev.type === 'set') {
                    const target = destination(ev.instanceId);
                    if (target) {
                        if (!ghosts.has(ev.instanceId)) {
                            const card = ev.side === 'computer' && !played.has(ev.instanceId) ? originCard(ev.side, ev.type === 'set' ? null : ev.card) : publicCard(ev.type === 'set' ? null : ev.card);
                            if (ev.side === 'computer' && !played.has(ev.instanceId)) { void sound?.play('place', .35); await fly(card, target, true); }
                            else place(card, target.getBoundingClientRect());
                            ghosts.set(ev.instanceId, card);
                        }
                        hide(hand(ev.instanceId)); await flash(target, '#93ffc0');
                    }
                } else if (ev.type === 'energy') {
                    if (ev.side === 'computer') { void sound?.play('energy', .35); await fly(originCard(ev.side, null), hero(ev.side)); }
                    else await flash(root.querySelector('.energy-zone'), '#8ad7ff');
                } else if (ev.type === 'trigger' || ev.type === 'status' || ev.type === 'reveal') {
                    void sound?.play(ev.type === 'reveal' ? 'flip' : 'trigger');
                    await flash(visual(ev.instanceId) ?? hero(ev.side), '#ffe5a1');
                } else if (ev.type === 'bounce') {
                    const source = visual(ev.instanceId);
                    if (source) { const moving = place(source.cloneNode(true), source.getBoundingClientRect()); hide(source); void sound?.play('draw'); await fly(moving, hero(ev.side)); }
                } else if (ev.type === 'turn') {
                    // Player pressed the mechanical button already; sound again only when their next turn begins.
                    if (ev.side === 'player') void sound?.play('turn');
                    await flash(hero(ev.side), '#9edcff');
                }
                else if (ev.type === 'draw' || ev.type === 'take' || ev.type === 'recover') { void sound?.play('draw'); await flash(hero(ev.side), '#9edcff'); }
                else if (ev.type === 'gameover') { void sound?.play('gameover'); await flash(hero(ev.side), '#ffe5a1'); }
                else if (ev.type === 'pay') await flash(ev.side === 'player' ? root.querySelector('.energy-zone') : hero(ev.side), '#8ad7ff');
            }
        } catch (error) { clear(); throw error; }
        // C# commits the new snapshot and renders it before clearing these transient cards.
    }
    return { present, clear, dispose() { disposed = true; clear(); } };
}
