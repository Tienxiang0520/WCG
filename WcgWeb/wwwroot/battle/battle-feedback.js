import { motionReduced } from './battle-fx.js';

// Present only public engine events. Keep the old board until this batch completes.
// `fx` adds optional decorative effects (particles, shake, banners) that are never awaited for long.
export function bindFeedback(root, sound = null, fx = null) {
    let layer = null, disposed = false, priorInert = false, locked = false;
    const animations = new Set(), hidden = new Map(), ghosts = new Map();
    let publicUnits = new Map();
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
        publicUnits.clear();
        if (locked) root.inert = priorInert;
        locked = false;
    }
    function hide(el) {
        if (!el || hidden.has(el)) return;
        hidden.set(el, el.style.visibility); el.style.visibility = 'hidden';
    }
    async function animate(el, frames, duration, easing = 'ease-out') {
        if (disposed || !el) return;
        const reduced = motionReduced();
        const animation = el.animate(frames, { duration: reduced ? Math.min(duration, 100) : duration, easing, fill: 'forwards' });
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
        if (publicUnits.get(card.instanceId)?.status?.includes('嘲諷')) {
            el.className += ' has-taunt';
            const frame = node('img', 'taunt-frame'); frame.src = 'battle/taunt-frame.svg'; frame.alt = ''; frame.draggable = false;
            el.append(frame);
        }
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
        // A lifted arc reads as "played from the hand" instead of a flat slide.
        const lift = Math.min(90, Math.abs(dy) * .35 + 30), tilt = dx > 0 ? 6 : -6;
        await animate(el, [
            { transform: 'translate(0,0) scale(1) rotate(0deg)', opacity: 1 },
            { transform: `translate(${dx * .45}px,${dy * .45 - lift}px) scale(${keep ? 1.12 : .95}) rotate(${tilt}deg)`, opacity: 1, offset: .45 },
            { transform: `translate(${dx}px,${dy}px) scale(${keep ? scale * 1.06 : scale}) rotate(0deg)`, opacity: keep ? 1 : .2, offset: .86 },
            { transform: `translate(${dx}px,${dy}px) scale(${scale})`, opacity: keep ? 1 : 0 }], 520, 'cubic-bezier(.25,.75,.35,1)');
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
        // Hero numbers land on the portrait itself; a portrait near the top edge drifts its number downward.
        const face = el.querySelector?.('.hero-face') ?? null, anchor = face ?? el, r = anchor.getBoundingClientRect();
        const amount = Number(String(text).replace(/[^\d]/g, '')) || 1;
        const nearTop = r.y < 90, top = face ? r.y + r.height / 2 - 32 : nearTop ? r.y + r.height + 26 : r.y - 14, drift = face && nearTop ? -1 : 1;
        const pop = place(node('div', `event-number ${heal ? 'healing' : ''} ${amount >= 3 ? 'big' : ''}`, text), { x: r.x + r.width / 2 - 60, y: top, width: 120, height: 64 });
        void sound?.play(heal ? 'heal' : 'damage');
        pop.dataset.wcgEffect = heal ? 'heal' : 'damage';
        if (heal) void fx?.burst(el, { color: '#9dffc9', count: 12, spread: 60, size: 6, rise: 50, duration: 760 });
        else {
            void fx?.burst(anchor, { color: '#ff9f80', count: 8 + amount * 3, spread: 70, size: 6 });
            if (face) { void fx?.flare(anchor, '#ffb39a', .9); void fx?.glow(anchor, '#ff5a3c', 300); }
            void fx?.recoil(el, null, Math.min(1, .35 + amount * .2));
            void fx?.shake(Math.min(1, .25 + amount * .18));
        }
        await Promise.all([flash(el, heal ? '#91ffc1' : '#ff957b'), animate(pop, [
            { opacity: 0, transform: `translateY(${6 * drift}px) scale(.35)` },
            { opacity: 1, transform: `translateY(${-8 * drift}px) scale(1.3)`, offset: .18 },
            { opacity: 1, transform: `translateY(${-14 * drift}px) scale(1)`, offset: .55 },
            { opacity: 0, transform: `translateY(${-48 * drift}px) scale(.95)` }], 680, 'cubic-bezier(.2,.8,.3,1)')]);
        pop.remove();
    }
    async function attack(ev) {
        const source = visual(ev.instanceId), destination = ev.targetId ? visual(ev.targetId) : hero(ev.side === 'player' ? 'computer' : 'player');
        if (!source || !destination) return;
        const power = Math.min(1, Math.max(.25, (Number(publicUnits.get(ev.instanceId)?.pp ?? ev.card?.pp) || 1000) / 3000));
        if (ev.side === 'player') { await flash(destination); return; } // Player already lunged (with contact sparks) on release.
        const from = source.getBoundingClientRect(), to = destination.getBoundingClientRect();
        const moving = place(source.cloneNode(true), from); moving.dataset.wcgEffect = 'attack'; hide(source);
        const dx = to.x + to.width / 2 - from.x - from.width / 2, dy = to.y + to.height / 2 - from.y - from.height / 2;
        void sound?.play('attack', .23);
        // Wind-up, fast lunge that stops just short of the target, then recoil home.
        await animate(moving, [
            { transform: 'translate(0,0) scale(1)' },
            { transform: `translate(${-dx * .06}px,${-dy * .06}px) scale(1.08) rotate(${dx > 0 ? -3 : 3}deg)`, offset: .22 },
            { transform: `translate(${dx * .86}px,${dy * .86}px) scale(1.1)`, offset: .5 }], 300, 'cubic-bezier(.5,0,.75,.4)');
        void fx?.impact(destination, moving, power);
        await animate(moving, [
            { transform: `translate(${dx * .86}px,${dy * .86}px) scale(1.1)` },
            { transform: `translate(${dx * .7}px,${dy * .7}px) scale(1.04)`, offset: .25 },
            { transform: 'translate(0,0) scale(1)' }], 260, 'cubic-bezier(.2,.7,.3,1)');
        moving.remove(); source.style.visibility = hidden.get(source) ?? ''; hidden.delete(source);
        await flash(destination);
    }
    async function death(ev) {
        const source = visual(ev.instanceId);
        if (!source) return;
        const departing = place(source.cloneNode(true), source.getBoundingClientRect()); departing.dataset.wcgEffect = 'death'; hide(source);
        await flash(departing);
        void sound?.play('death');
        if (fx && !motionReduced()) {
            // Shards carry the exit; the solid card vanishes quickly underneath them.
            void fx.shatter(departing);
            await animate(departing, [{ opacity: 1, transform: 'scale(1)', filter: 'brightness(1.8)' }, { opacity: 0, transform: 'scale(1.04)', filter: 'brightness(2.2)' }], 160);
        } else await animate(departing, [{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'translateY(35px) rotate(7deg) scale(.55)' }], 420);
        departing.remove(); ghosts.get(ev.instanceId)?.remove(); ghosts.delete(ev.instanceId);
    }
    async function present(previous, next, events) {
        clear(); if (disposed || !events.length) return;
        priorInert = root.inert; root.inert = true; locked = true;
        layer = node('div', 'battle-feedback-layer'); layer.setAttribute('aria-hidden', 'true'); document.body.append(layer);
        const messageRect = root.querySelector('.battle-message').getBoundingClientRect();
        const banner = place(node('div', 'event-banner'), messageRect);
        const played = new Set();
        publicUnits = new Map([...previous.player.field, ...previous.computer.field, ...next.player.field, ...next.computer.field].map(m => [m.card.instanceId, m]));
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
                        const spellTarget = isField ? null : visual(ev.targetId) ?? root.querySelector('.battlefield');
                        await fly(moving, isField ?? spellTarget, !!isField);
                        if (isField) ghosts.set(ev.instanceId, moving);
                        else { void fx?.ring(spellTarget, '#b9e6ff', .6); void fx?.burst(spellTarget, { color: '#bfe9ff', count: 16, spread: 80, size: 6 }); }
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
                        hide(hand(ev.instanceId));
                        void fx?.slam(target, ev.type === 'set' ? 0 : ev.card?.cost ?? 1);
                        await flash(target, '#93ffc0');
                    }
                } else if (ev.type === 'energy') {
                    if (ev.side === 'computer') { void sound?.play('energy', .35); await fly(originCard(ev.side, null), hero(ev.side)); }
                    else { const zone = root.querySelector('.energy-zone'); void fx?.burst(zone, { color: '#8ad7ff', count: 12, spread: 55, size: 6, rise: 20 }); await flash(zone, '#8ad7ff'); }
                } else if (ev.type === 'trigger' || ev.type === 'status' || ev.type === 'reveal') {
                    void sound?.play(ev.type === 'reveal' ? 'flip' : 'trigger');
                    void fx?.ring(visual(ev.instanceId) ?? hero(ev.side), '#ffe5a1', .7);
                    await flash(visual(ev.instanceId) ?? hero(ev.side), '#ffe5a1');
                } else if (ev.type === 'bounce') {
                    const source = visual(ev.instanceId);
                    if (source) { const moving = place(source.cloneNode(true), source.getBoundingClientRect()); hide(source); void sound?.play('draw'); await fly(moving, hero(ev.side)); }
                } else if (ev.type === 'turn') {
                    // Player pressed the mechanical button already; sound again only when their next turn begins.
                    if (ev.side === 'player') void sound?.play('turn');
                    // Non-blocking banner; input unlocks on the normal snapshot commit.
                    void fx?.banner(ev.side === 'player' ? '你的回合' : '電腦回合', ev.side);
                    await flash(hero(ev.side), '#9edcff');
                }
                else if (ev.type === 'draw' || ev.type === 'take' || ev.type === 'recover') { void sound?.play('draw'); await flash(hero(ev.side), '#9edcff'); }
                else if (ev.type === 'gameover') {
                    void sound?.play('gameover');
                    // The gameover event names the losing side.
                    void fx?.banner(ev.side === 'player' ? '敗北' : '勝利', ev.side === 'player' ? 'defeat' : 'victory', 1800);
                    // Portrait finish: the loser's frame cracks with dust, the winner's sparkles gold.
                    const loser = hero(ev.side), winner = hero(ev.side === 'player' ? 'computer' : 'player');
                    const loserFace = loser?.querySelector?.('.hero-face') ?? loser, winnerFace = winner?.querySelector?.('.hero-face') ?? winner;
                    if (loserFace) { void fx?.impact(loserFace, null, .9); void fx?.burst(loserFace, { color: '#9aa3ad', count: 16, spread: 70, size: 14, kind: 'dust', origin: 'bottom' }); }
                    if (winnerFace) { void fx?.ring(winnerFace, '#ffd65a', 1.4); void fx?.burst(winnerFace, { color: '#ffe08a', count: 22, spread: 90, size: 6, rise: 50, duration: 900 }); }
                    await flash(hero(ev.side), '#ffe5a1');
                }
                else if (ev.type === 'pay') await flash(ev.side === 'player' ? root.querySelector('.energy-zone') : hero(ev.side), '#8ad7ff');
            }
        } catch (error) { clear(); throw error; }
        // C# commits the new snapshot and renders it before clearing these transient cards.
    }
    return { present, clear, dispose() { disposed = true; clear(); fx?.clear(); } };
}
