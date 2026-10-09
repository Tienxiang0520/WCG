import { motionReduced } from './battle-fx.js';
import { planSteps, batchSpeed, isSpellCard, keywordOf } from './effect-plan.js';

// Fixed animation strings. Event labels and card names arrive already localized from the server.
const EN = { '我方': 'You', '電腦': 'Computer', '魂 誓': 'SOUL OATH', '背面卡片': 'Face-down card', '你的回合': 'Your turn', '電腦回合': "Computer's turn", '勝利': 'Victory', '敗北': 'Defeat',
    '快轉': 'Fast-forward', '點擊快轉': 'Tap to fast-forward', '聖盾': 'Holy Shield', '嘲諷': 'Taunt', '沉默': 'Silence', '劇毒': 'Poison', '貫穿': 'Trample', '衝鋒': 'Charge', '反擊': 'Counter',
    '橫置': 'Tapped', '直立': 'Ready', '附著': 'Attach', '抽牌': 'Draw', '棄牌': 'Discard', '返回手牌': 'Return to hand',
    '費': 'Cost', '力量': 'Power', '傷害': 'Damage', '魂誓': 'Soul Oath', '無異能': 'No ability', '怪物': 'Monster', '法術': 'Spell', '法術（反擊）': 'Counter spell', '結界': 'Ward',
    '中立': 'Neutral', '深淵': 'Abyss', '狂怒': 'Fury', '理智': 'Reason', '生機': 'Life', '秩序': 'Order' };
const WILL_KEYS = { '狂怒': 'wrath', '理智': 'reason', '生機': 'vitality', '秩序': 'order', '深淵': 'abyss' };
const STAR = 'M10 0 11.6 8.4 20 10 11.6 11.6 10 20 8.4 11.6 0 10 8.4 8.4Z';
// Per-will ornaments (corner, name spark, gem, emblem) — keep in sync with Services/CardFaceThemes.cs (CardFaceTests checks).
const FACE_THEMES = {"order": {"corner": "M10 0 11.6 8.4 20 10 11.6 11.6 10 20 8.4 11.6 0 10 8.4 8.4Z", "spark": "M10 0 11.6 8.4 20 10 11.6 11.6 10 20 8.4 11.6 0 10 8.4 8.4Z", "gem": "M10 0 11.6 8.4 20 10 11.6 11.6 10 20 8.4 11.6 0 10 8.4 8.4Z", "emblem": "<circle cx=\"50\" cy=\"50\" r=\"44\"/><circle cx=\"50\" cy=\"50\" r=\"31\"/><circle cx=\"50\" cy=\"50\" r=\"6\"/><path d=\"M50 2 56 44 98 50 56 56 50 98 44 56 2 50 44 44Z\"/><path d=\"M50 18 53 47 82 50 53 53 50 82 47 53 18 50 47 47Z\" transform=\"rotate(45 50 50)\"/>"}, "wrath": {"corner": "M1 1 19 3 13 6 17 9 9 9 11 13 6 12 3 19Z", "spark": "M10 0C13 5 17 8 16 13 15 17 12 20 10 20 8 20 5 17 4 13 4 10 7 8 8 4 10 8 11 10 10 0Z", "gem": "M10 1 15 8 12 19 8 19 5 8Z", "emblem": "<circle cx=\"50\" cy=\"50\" r=\"45\"/><path d=\"M50 6C62 26 78 34 74 62 72 80 60 92 50 94 40 92 28 80 26 62 24 46 36 40 40 24 46 36 44 46 50 52 58 40 56 22 50 6Z\"/><path d=\"M8 30 30 40 22 52 40 58M92 70 70 60 78 48 60 42\"/>"}, "reason": {"corner": "M1 1H14V3H3V14H1ZM5 5H11V6.6H6.6V11H5ZM9 9H12V12H9Z", "spark": "M10 1 18 5.5V14.5L10 19 2 14.5V5.5ZM10 5 5.5 7.5V12.5L10 15 14.5 12.5V7.5Z", "gem": "M10 2 18 10 10 18 2 10ZM10 6 6 10 10 14 14 10Z", "emblem": "<circle cx=\"50\" cy=\"50\" r=\"45\"/><circle cx=\"50\" cy=\"50\" r=\"36\"/><path d=\"M50 14 81 68H19ZM50 86 19 32H81Z\"/><circle cx=\"50\" cy=\"50\" r=\"10\"/><path d=\"M50 5V14M50 86V95M5 50H14M86 50H95\"/>"}, "vitality": {"corner": "M1 19C1 9 5 3 13 1 10 4 9 7 9 10 12 7 16 6 19 7 15 9 12 12 11 16 8 14 5 15 1 19Z", "spark": "M3 17C3 8 9 3 18 2 18 11 12 17 3 17ZM5 15 15 5 14.4 4.4 4.4 14.4Z", "gem": "M10 1C16 5 17 12 10 19 3 12 4 5 10 1Z", "emblem": "<circle cx=\"50\" cy=\"50\" r=\"45\"/><path d=\"M50 8C78 26 84 60 50 92 16 60 22 26 50 8Z\"/><path d=\"M50 14V88M50 40 68 28M50 54 72 42M50 68 66 58M50 40 32 28M50 54 28 42M50 68 34 58\"/>"}, "abyss": {"corner": "M1 1 19 5 9 6 12 9 7 8 6 12 5 7 4 19Z", "spark": "M1 10Q10 1 19 10 10 19 1 10ZM10 6.5Q12.2 10 10 13.5 7.8 10 10 6.5Z", "gem": "M10 1 13 8 19 10 13 12 10 19 7 12 1 10 7 8Z", "emblem": "<path d=\"M8 50Q50 12 92 50 50 88 8 50Z\"/><circle cx=\"50\" cy=\"50\" r=\"15\"/><path d=\"M50 37Q57 50 50 63 43 50 50 37Z\"/><path d=\"M22 68Q12 84 28 94M78 68Q88 84 72 94M50 78Q44 90 54 98M30 30Q18 18 24 6M70 30Q82 18 76 6\"/>"}, "neutral": {"corner": "M10 3A7 7 0 1 1 9.99 3ZM10 6A4 4 0 1 0 10.01 6Z", "spark": "M10 3A7 7 0 1 1 9.99 3Z", "gem": "M10 4A6 6 0 1 1 9.99 4Z", "emblem": "<circle cx=\"50\" cy=\"50\" r=\"44\"/><circle cx=\"50\" cy=\"50\" r=\"30\"/><path d=\"M50 22 78 50 50 78 22 50Z\"/><circle cx=\"50\" cy=\"8\" r=\"3\"/><circle cx=\"50\" cy=\"92\" r=\"3\"/><circle cx=\"8\" cy=\"50\" r=\"3\"/><circle cx=\"92\" cy=\"50\" r=\"3\"/>"}};
// Fast-forward survives across the batches of one computer turn and resets when the player's turn begins.
let fastForward = 1;
export function resetFastForward() { fastForward = 1; }
const t = zh => (globalThis.document?.documentElement?.lang === 'en' && EN[zh]) || zh;

// Present only public engine events. Keep the old board until this batch completes.
// `fx` adds optional decorative effects (particles, shake, banners) that are never awaited for long.
export function bindFeedback(root, sound = null, fx = null) {
    let layer = null, disposed = false, priorInert = false, locked = false, speed = 1, pauser = null, banner = null, showcase = null;
    const animations = new Set(), hidden = new Map(), ghosts = new Map();
    let publicUnits = new Map(), priorUnits = new Map(), nextUnits = new Map();
    const shielded = (units, id) => !!units.get(id)?.status?.some(x => String(x).startsWith('聖盾'));
    // A holy shield that was up before this batch and is gone afterwards (unit still alive) shattered in combat.
    const shieldBroke = id => !!id && shielded(priorUnits, id) && nextUnits.has(id) && !shielded(nextUnits, id);
    const scope = root.getAttributeNames().find(name => name.startsWith('b-'));
    function node(tag, className, text = '') {
        const el = document.createElement(tag); el.className = className; el.textContent = text;
        if (scope) el.setAttribute(scope, '');
        return el;
    }
    function clear() {
        for (const animation of animations) animation.cancel(); animations.clear();
        for (const [el, visibility] of hidden) el.style.visibility = visibility;
        hidden.clear(); ghosts.clear(); layer?.remove(); layer = null; pauser = null; banner = null; showcase = null;
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
        const animation = el.animate(frames, { duration: reduced ? Math.min(duration, 100) : duration / (speed * fastForward), easing, fill: 'forwards' });
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
    // Full card face for the spell showcase; mirrors Components/Common/CardFace.razor (styles in card-face.css).
    function faceCard(card) {
        const svg = (cls, view, inner) => { const el = document.createElementNS?.('http://www.w3.org/2000/svg', 'svg') ?? node('i', cls); if (el.setAttribute) { el.setAttribute('class', cls); el.setAttribute('viewBox', view); el.setAttribute('aria-hidden', 'true'); el.innerHTML = inner; } return el; };
        const will = WILL_KEYS[card.will] ?? 'neutral', theme = FACE_THEMES[will];
        const star = cls => svg(`face-star ${cls}`, '0 0 20 20', `<path d="${['tl', 'tr', 'bl', 'br'].includes(cls) ? theme.corner : theme.gem}"/>`);
        const outer = node('article', 'wcg-face'), nameLen = [...String(card.name ?? '')].reduce((n, c) => n + (c.charCodeAt(0) > 0x2e80 ? 2 : 1), 0);
        if (nameLen > 15) outer.className += nameLen > 24 ? ' name-xl' : ' name-l'; outer.dataset.will = will; outer.dataset.cardId = card.cardId ?? '';
        const body = node('div', 'face-card'); outer.append(body);
        body.append(svg('face-frame', '0 0 63 88', '<rect x="2.2" y="2.2" width="58.6" height="83.6" rx="1.8" class="frame-inner"/>'));
        for (const c of ['tl', 'tr', 'bl', 'br', 'top', 'bottom', 'head']) body.append(star(c));
        const head = node('header', 'face-head'), name = node('h3', 'face-name'), cost = node('span', 'face-cost');
        name.append(svg('face-spark', '0 0 20 20', `<path d="${theme.spark}"/>`), node('span', '', card.name ?? ''));
        cost.append(node('b', '', String(card.cost ?? 0)), node('small', '', t('費'))); head.append(name, cost);
        const band = node('div', 'face-band'); band.append(node('span', '', t(card.will ?? '')), node('i', '', '・'), node('span', '', t(card.type ?? '')));
        const art = node('div', 'face-art');
        if (/^WCG-\d{3}$/.test(card.cardId ?? '')) { const img = node('img', 'card-illustration'); img.src = `card-art/${card.cardId}.webp`; img.alt = ''; img.draggable = false; art.append(img); }
        for (const a of card.arrows ?? []) art.append(node('span', `face-arrow ${a}`));
        const text = node('div', 'face-text'); text.append(svg('face-compass face-emblem', '0 0 100 100', theme.emblem)); const plain = String(card.text ?? '').replace(/【箭頭：[^】]*】\s*|\[Arrows?:[^\]]*\]\s*/g, '').trim();
        text.append(node('p', '', plain || t('無異能'))); if (plain.length > 60) outer.className += plain.length > 110 ? ' text-l' : ' text-m';
        body.append(head, band, art, text);
        if (card.pp != null) {
            const stats = node('div', 'face-stats'), pp = node('span', 'pp'), dp = node('span', 'dp');
            pp.append(node('small', '', t('力量')), node('b', '', String(card.pp)), node('small', '', 'PP'));
            dp.append(node('small', '', t('傷害')), node('b', '', String(card.dp ?? 1)), node('small', '', 'DP'));
            stats.append(pp, star('mid'), dp); body.append(stats);
        }
        const foot = node('footer', 'face-foot'); foot.append(node('span', '', t('魂誓')), node('span', '', card.cardId ?? '')); body.append(foot);
        return outer;
    }
    function publicCard(card) {
        const el = node('div', `event-card ${card ? '' : 'event-card-back'}`);
        // Card data comes exclusively from public BattleEvent payloads; never inspect AI hand data.
        if (!card) { el.append(node('strong', '', t('魂 誓')), node('small', '', t('背面卡片'))); return el; }
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
        void sound?.play(heal ? 'heal' : face ? 'faceHit' : 'damage');
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
        for (const id of [ev.targetId, ev.instanceId]) if (shieldBroke(id)) void sound?.play('shieldBreak', ev.side === 'player' ? 0 : .3);
        if (ev.side === 'player') { await flash(destination); return; } // Player already lunged (with contact sparks) on release.
        const from = source.getBoundingClientRect(), to = destination.getBoundingClientRect();
        const moving = place(source.cloneNode(true), from); moving.dataset.wcgEffect = 'attack'; hide(source);
        const dx = to.x + to.width / 2 - from.x - from.width / 2, dy = to.y + to.height / 2 - from.y - from.height / 2;
        void sound?.play('swing'); void sound?.play('attack', .23);
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
    // ---- sequenced effect presentation -------------------------------------------------------------
    function center(el) { const r = el.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2, r }; }
    async function pause(ms) {
        if (disposed || motionReduced() || speed * fastForward >= 8 || !layer) return;
        if (!pauser) { pauser = node('i', 'event-pause'); layer.append(pauser); }
        await animate(pauser, [{ opacity: 0 }, { opacity: 0 }], ms);
    }
    async function badge(el, text, tone = 'gold') {
        if (!el || !text) return;
        const c = center(el), b = boardRect(), x = Math.max(b.x + 4, Math.min(c.x - 80, b.x + b.width - 164));
        const pill = place(node('div', 'event-badge', text), { x, y: c.r.y - 30, width: 160, height: 28 });
        pill.dataset.tone = tone; pill.dataset.wcgEffect = 'badge';
        await animate(pill, [
            { opacity: 0, transform: 'translateY(10px) scale(.6)' },
            { opacity: 1, transform: 'translateY(-4px) scale(1.12)', offset: .2 },
            { opacity: 1, transform: 'translateY(-6px) scale(1)', offset: .75 },
            { opacity: 0, transform: 'translateY(-16px) scale(.96)' }], 900, 'cubic-bezier(.2,.8,.3,1)');
        pill.remove();
    }
    // A beam from the effect source to what it affects; `orb` sends a projectile instead (damage / attach).
    async function beam(from, to, color = '#ffd77a', orb = false) {
        if (!from || !to || from === to) return;
        const a = center(from), b = center(to), dx = b.x - a.x, dy = b.y - a.y, length = Math.hypot(dx, dy);
        if (length < 4) return;
        if (orb) {
            const ball = place(node('div', 'event-orb'), { x: a.x - 14, y: a.y - 14, width: 28, height: 28 });
            ball.style.background = `radial-gradient(circle, #fff 0 18%, ${color} 45%, transparent 72%)`; ball.dataset.wcgEffect = 'projectile';
            await animate(ball, [{ transform: 'translate(0,0) scale(.6)', opacity: .4 }, { transform: `translate(${dx * .5}px,${dy * .5 - 40}px) scale(1.15)`, opacity: 1, offset: .55 }, { transform: `translate(${dx}px,${dy}px) scale(.9)`, opacity: 1 }], 420, 'cubic-bezier(.4,0,.6,1)');
            ball.remove(); void fx?.burst(to, { color, count: 14, spread: 70, size: 6 });
            return;
        }
        const ray = place(node('div', 'event-beam'), { x: a.x, y: a.y - 3, width: length, height: 6 });
        const angle = Math.atan2(dy, dx) * 180 / Math.PI;
        ray.style.transformOrigin = '0 50%'; ray.style.background = `linear-gradient(90deg, ${color}00, ${color} 30%, #fff 50%, ${color} 70%, ${color}cc)`; ray.dataset.wcgEffect = 'beam';
        await animate(ray, [{ transform: `rotate(${angle}deg) scaleX(0)`, opacity: 1 }, { transform: `rotate(${angle}deg) scaleX(1)`, opacity: 1, offset: .55 }, { transform: `rotate(${angle}deg) scaleX(1)`, opacity: 0 }], 460, 'cubic-bezier(.3,.7,.3,1)');
        ray.remove();
    }
    function boardRect() { const r = root.querySelector('.battlefield')?.getBoundingClientRect?.() ?? root.getBoundingClientRect(); return r; }
    // Spells: the card is shown large in the centre, then shrinks to a corner and stays as the source of its effects.
    async function showSpell(ev) {
        const r = boardRect(), w = Math.min(250, r.width * .56), h = w * 88 / 63;
        const card = ev.card ? faceCard(ev.card) : publicCard(null); card.className += ' event-showcase'; card.dataset.wcgEffect = 'showcase';
        if (ev.side === 'computer') card.className += ' enemy-card';
        place(card, { x: r.x + r.width / 2 - w / 2, y: r.y + r.height / 2 - h / 2, width: w, height: h });
        void sound?.play('spell');
        const hero0 = hero(ev.side), from = hero0 ? center(hero0) : { x: r.x + r.width / 2, y: r.y + r.height };
        const ox = from.x - (r.x + r.width / 2), oy = from.y - (r.y + r.height / 2);
        await animate(card, [{ opacity: 0, transform: `translate(${ox * .6}px,${oy * .6}px) scale(.35)` }, { opacity: 1, transform: 'translate(0,0) scale(1.06)', offset: .7 }, { opacity: 1, transform: 'translate(0,0) scale(1)' }], 460, 'cubic-bezier(.2,.8,.3,1)');
        void fx?.ring(card, '#ffe2a6', 1.1); void fx?.burst(card, { color: '#ffe8b0', count: 18, spread: 110, size: 6 });
        await pause(720);
        const dx = (ev.side === 'player' ? 1 : -1) * (r.width / 2 - w * .35), dy = (ev.side === 'player' ? 1 : -1) * r.height * .18;
        await animate(card, [{ transform: 'translate(0,0) scale(1)' }, { transform: `translate(${dx}px,${dy}px) scale(.48)` }], 300, 'cubic-bezier(.4,0,.2,1)');
        showcase?.remove(); showcase = card;
    }
    function dropShowcase() { showcase?.remove(); showcase = null; }
    function sourceOf(step) { return step.fromShowcase ? showcase : visual(step.from); }
    function handZone(side) { return side === 'player' ? root.querySelector('.v06-hand') ?? hero('player') : hero('computer'); }
    async function cardTo(side, card, destination, faceDown = false) {
        const start = hero(side); if (!start || !destination) return;
        const r = start.getBoundingClientRect(), el = publicCard(faceDown ? null : card);
        place(el, { x: r.x + r.width / 2 - 45, y: r.y + r.height / 2 - 60, width: 90, height: 120 });
        await fly(el, destination);
    }
    async function runEvent(step, state) {
        const ev = step.ev, from = step.from ? sourceOf(step) : null;
        const { destination, played } = state;
        if (ev.type === 'attack') await attack(ev);
        else if (ev.type === 'damage' && ev.amount > 0) { if (from) await beam(from, hero(ev.side), '#ff8a5c', true); await number(hero(ev.side), `−${ev.amount}`); }
        else if (ev.type === 'heal' && ev.amount > 0) { if (from) await beam(from, hero(ev.side), '#8dffbd'); await number(hero(ev.side), `+${ev.amount}`, true); }
        else if (ev.type === 'death') {
            const target = visual(ev.instanceId);
            if (from && target) { await beam(from, target, '#ff7a6a', true); void fx?.impact(target, from, .7); }
            if (keywordOf(ev.label)) await badge(target, t(keywordOf(ev.label)), keywordOf(ev.label));
            await death(ev);
        }
        else if (ev.type === 'play') {
            hide(hand(ev.instanceId));
            if (ev.side === 'computer') {
                const isField = destination(ev.instanceId), moving = originCard(ev.side, ev.card);
                void sound?.play(isField ? 'place' : String(ev.card?.type ?? '').includes('結界') ? 'field' : 'spell', .35);
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
                void sound?.play(ev.type === 'set' ? 'set' : 'summon');
                if (ev.type === 'summon' && nextUnits.get(ev.instanceId)?.status?.includes('嘲諷')) void sound?.play('taunt', .15);
                await flash(target, '#93ffc0');
            }
        } else if (ev.type === 'energy') {
            if (from) await beam(from, ev.side === 'player' ? root.querySelector('.energy-zone') : hero(ev.side), '#8ad7ff');
            if (ev.side === 'computer') { void sound?.play('energy', .35); void sound?.play('energyFill', .6); await fly(originCard(ev.side, null), hero(ev.side)); }
            else { const zone = root.querySelector('.energy-zone'); void sound?.play('energyFill'); void fx?.burst(zone, { color: '#8ad7ff', count: 12, spread: 55, size: 6, rise: 20 }); await flash(zone, '#8ad7ff'); }
        } else if (ev.type === 'status' || ev.type === 'reveal') {
            const target = visual(ev.instanceId) ?? hero(ev.side);
            if (from) await beam(from, target, ev.label === '沉默' ? '#c49bff' : '#ffe5a1');
            void sound?.play(ev.type === 'reveal' ? 'flip' : 'trigger');
            void fx?.ring(target, ev.label === '沉默' ? '#c49bff' : '#ffe5a1', .7);
            await Promise.all([flash(target, ev.label === '沉默' ? '#c49bff' : '#ffe5a1'), keywordOf(ev.label) ? badge(target, t(keywordOf(ev.label)), keywordOf(ev.label)) : null]);
        } else if (ev.type === 'attach') {
            const target = visual(ev.targetId);
            void sound?.play('glass');
            if (from ?? hero(ev.side)) await beam(from ?? hero(ev.side), target, '#ffe08a', true);
            void fx?.ring(target, '#ffe08a', .8);
            await Promise.all([flash(target, '#ffe08a'), badge(target, t('附著'), 'attach')]);
        } else if (ev.type === 'bounce') {
            const source = visual(ev.instanceId);
            if (from && source) await beam(from, source, '#9fd8ff');
            if (source) { const moving = place(source.cloneNode(true), source.getBoundingClientRect()); hide(source); void sound?.play('draw'); await fly(moving, handZone(ev.side)); }
        } else if (ev.type === 'turn') {
            if (ev.side === 'player') { void sound?.play('turn'); fastForward = 1; }
            void fx?.banner(t(ev.side === 'player' ? '你的回合' : '電腦回合'), ev.side);
            await flash(hero(ev.side), '#9edcff');
        }
        else if (ev.type === 'draw' || ev.type === 'take' || ev.type === 'recover') {
            if (from) void fx?.glow(from, '#9edcff', 260);
            void sound?.play('draw');
            await cardTo(ev.side, ev.card, handZone(ev.side), ev.type === 'draw' && !ev.card);
        }
        else if (ev.type === 'discard') {
            const inHand = hand(ev.instanceId), el = inHand ? place(inHand.cloneNode(true), inHand.getBoundingClientRect()) : null;
            if (inHand) hide(inHand);
            void sound?.play('draw');
            if (el) { el.dataset.wcgEffect = 'discard'; await animate(el, [{ opacity: 1, transform: 'translateY(0) rotate(0)', filter: 'none' }, { opacity: 0, transform: 'translateY(-60px) rotate(-14deg) scale(.8)', filter: 'grayscale(1) brightness(.6)' }], 520); el.remove(); }
            else if (hero(ev.side)) {
                // The computer's hand is hidden: the discarded card is revealed beside its portrait, then greys out.
                const c = center(hero(ev.side)), shown = place(publicCard(ev.card), { x: c.x + 70, y: c.y - 50, width: 84, height: 112 });
                shown.dataset.wcgEffect = 'discard';
                await animate(shown, [{ opacity: 0, transform: 'scale(.6)' }, { opacity: 1, transform: 'scale(1.05)', offset: .3 }, { opacity: 1, transform: 'scale(1)', filter: 'none', offset: .65 }, { opacity: 0, transform: 'translateY(30px) rotate(10deg) scale(.85)', filter: 'grayscale(1) brightness(.6)' }], 760);
                shown.remove();
            }
        }
        else if (ev.type === 'gameover') {
            void sound?.play(ev.side === 'player' ? 'defeat' : 'victory');
            // The gameover event names the losing side.
            void fx?.banner(t(ev.side === 'player' ? '敗北' : '勝利'), ev.side === 'player' ? 'defeat' : 'victory', 1800);
            // Portrait finish: the loser's frame cracks with dust, the winner's sparkles gold.
            const loser = hero(ev.side), winner = hero(ev.side === 'player' ? 'computer' : 'player');
            const loserFace = loser?.querySelector?.('.hero-face') ?? loser, winnerFace = winner?.querySelector?.('.hero-face') ?? winner;
            if (loserFace) { void fx?.impact(loserFace, null, .9); void fx?.burst(loserFace, { color: '#9aa3ad', count: 16, spread: 70, size: 14, kind: 'dust', origin: 'bottom' }); }
            if (winnerFace) { void fx?.ring(winnerFace, '#ffd65a', 1.4); void fx?.burst(winnerFace, { color: '#ffe08a', count: 22, spread: 90, size: 6, rise: 50, duration: 900 }); }
            await flash(hero(ev.side), '#ffe5a1');
        }
        else if (ev.type === 'pay') await flash(ev.side === 'player' ? root.querySelector('.energy-zone') : hero(ev.side), '#8ad7ff');
    }
    async function runStep(step, state) {
        if (step.kind === 'showcase') { banner.textContent = `${t(step.ev.side === 'player' ? '我方' : '電腦')} · ${step.ev.label || ''} · ${step.ev.card?.name ?? ''}`; hide(hand(step.ev.instanceId)); state.played.add(step.ev.instanceId); await showSpell(step.ev); return; }
        if (step.kind === 'source') {
            const ev = step.ev, el = visual(ev.instanceId) ?? hero(ev.side);
            banner.textContent = `${t(ev.side === 'player' ? '我方' : '電腦')} · ${ev.label || ev.type}${ev.card ? ` · ${ev.card.name}` : ''}`;
            void sound?.play('trigger'); void fx?.ring(el, '#ffe5a1', .9); void fx?.glow(el, '#ffd77a', 360);
            await Promise.all([flash(el, '#ffe5a1'), badge(el, ev.label, 'source')]);
            return;
        }
        if (step.kind === 'badge') {
            const el = visual(step.target); if (!el) return;
            if (step.from) await beam(visual(step.from), el, step.keyword === '聖盾' ? '#ffe08a' : '#ffd77a');
            if (step.keyword === '嘲諷') void sound?.play('taunt', .1); else if (step.keyword === '聖盾') void sound?.play('glass'); else void sound?.play('trigger');
            void fx?.ring(el, step.keyword === '沉默' ? '#c49bff' : '#ffe08a', .6);
            await badge(el, t(step.keyword), step.keyword);
            return;
        }
        if (step.kind === 'stat') {
            const el = visual(step.target); if (!el) return;
            const up = step.delta > 0, c = center(el);
            const pop = place(node('div', `event-stat ${up ? 'up' : 'down'}`, `${up ? '+' : '−'}${Math.abs(step.delta)} PP`), { x: c.x - 70, y: c.r.y + c.r.height * .35, width: 140, height: 34 });
            pop.dataset.wcgEffect = 'stat'; void sound?.play(up ? 'heal' : 'damage', 0);
            void fx?.glow(el, up ? '#9dffc9' : '#ff8a7a', 320);
            await animate(pop, [{ opacity: 0, transform: 'scale(.5)' }, { opacity: 1, transform: 'scale(1.15)', offset: .25 }, { opacity: 1, transform: 'translateY(-10px) scale(1)', offset: .7 }, { opacity: 0, transform: 'translateY(-28px)' }], 760);
            pop.remove(); return;
        }
        if (step.kind === 'tap') {
            const el = visual(step.target); if (!el) return;
            void sound?.play('flip');
            await Promise.all([animate(el, [{ transform: 'rotate(0)' }, { transform: `rotate(${step.tapped ? 16 : -10}deg)`, offset: .5 }, { transform: 'rotate(0)' }], 420), badge(el, t(step.tapped ? '橫置' : '直立'), 'tap')]);
            return;
        }
        const ev = step.ev;
        banner.textContent = `${t(ev.side === 'player' ? '我方' : '電腦')} · ${ev.label || ev.type}${ev.card ? ` · ${ev.card.name}` : ''}`;
        if (ev.type === 'turn' || ev.type === 'attack' || (ev.type === 'play' && !isSpellCard(ev.card))) dropShowcase();
        await runEvent(step, state);
    }
    function skipControls() {
        const r = root.getBoundingClientRect();
        const catcher = place(node('button', 'event-skip'), { x: r.x, y: r.y, width: r.width, height: r.height });
        catcher.type = 'button'; catcher.setAttribute('aria-label', t('點擊快轉')); catcher.title = t('點擊快轉');
        const b = boardRect(), w = b.width < 600 ? 92 : 120, h = b.width < 600 ? 26 : 32;
        const ff = place(node('button', 'event-ff', `⏩ ${t('快轉')}`), { x: b.x + b.width - w - 8, y: b.y + 6, width: w, height: h });
        ff.type = 'button';
        // Each tap speeds the rest of this batch (and the computer's turn) up further; animations never change state.
        const faster = event => { event?.stopPropagation?.(); fastForward = fastForward < 3 ? 3 : 12; layer?.setAttribute?.('data-fast', String(fastForward)); };
        catcher.addEventListener('click', faster); ff.addEventListener('click', faster);
    }
    async function present(previous, next, events) {
        clear(); if (disposed || !events.length) return;
        priorInert = root.inert; root.inert = true; locked = true;
        layer = node('div', 'battle-feedback-layer'); layer.setAttribute('aria-hidden', 'true'); document.body.append(layer);
        const messageRect = root.querySelector('.battle-message').getBoundingClientRect();
        banner = place(node('div', 'event-banner'), messageRect);
        const played = new Set();
        publicUnits = new Map([...previous.player.field, ...previous.computer.field, ...next.player.field, ...next.computer.field].map(m => [m.card.instanceId, m]));
        priorUnits = new Map([...previous.player.field, ...previous.computer.field].map(m => [m.card.instanceId, m]));
        nextUnits = new Map([...next.player.field, ...next.computer.field].map(m => [m.card.instanceId, m]));
        // Next public field provides fixed destinations; old public field preserves departing sources.
        const locations = new Map([...previous.player.field.map(m => [m.card.instanceId, ['player', m.slot]]), ...previous.computer.field.map(m => [m.card.instanceId, ['computer', m.slot]]), ...next.player.field.map(m => [m.card.instanceId, ['player', m.slot]]), ...next.computer.field.map(m => [m.card.instanceId, ['computer', m.slot]])]);
        const destination = id => { const location = locations.get(id); return location ? slot(...location) : null; };
        const steps = planSteps(previous, next, events);
        speed = batchSpeed(steps.length);
        if (steps.length > 1 || steps.some(s => s.kind !== 'event')) skipControls();
        try {
            for (const [index, step] of steps.entries()) {
                if (disposed) break;
                layer.dataset.wcgEvent = step.ev?.type ?? step.kind; layer.dataset.wcgEventSide = step.ev?.side ?? '';
                layer.dataset.wcgStep = String(index);
                await runStep(step, { destination, played });
                if (index < steps.length - 1) await pause(step.kind === 'event' && step.ev.type === 'pay' ? 60 : 170);
            }
            dropShowcase();
        } catch (error) { clear(); throw error; }
        // C# commits the new snapshot and renders it before clearing these transient cards.
    }
    return { present, clear, fastForward() { fastForward = fastForward < 3 ? 3 : 12; }, dispose() { disposed = true; clear(); fx?.clear(); } };
}
