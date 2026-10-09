// Turns one batch of public engine events (plus the public snapshots before and after it) into an ordered list of
// presentation steps. Pure data in, data out: no DOM, so it is unit tested in node and never touches game state.
//
// Step kinds
//   event     the engine event itself (attack, summon, death, damage …); `from` names the effect source, if any
//   showcase  a spell is enlarged in the centre before its effects resolve
//   source    a passive / trigger / deploy ability: the source card flashes with a badge
//   badge     a keyword or status appears on a unit (嘲諷, 聖盾, 沉默 …), optionally with a beam from an aura source
//   stat      a unit's PP changed (buff / debuff)
//   tap       a unit was tapped or readied by an effect (not by attacking)

export const BADGE_KEYWORDS = ['聖盾', '嘲諷', '沉默', '劇毒', '貫穿', '衝鋒', '反擊'];
const SOURCE_TYPES = new Set(['effect', 'trigger']);
const TARGETED = new Set(['death', 'bounce', 'status', 'attach', 'reveal', 'damage', 'heal', 'draw', 'take', 'recover', 'discard', 'energy']);

export const isSpellCard = card => !!card && /^法術/.test(String(card.type ?? ''));
export function keywordOf(status) { return BADGE_KEYWORDS.find(k => String(status).startsWith(k)) ?? null; }
function units(snapshot) {
    const map = new Map();
    for (const side of ['player', 'computer']) for (const m of snapshot?.[side]?.field ?? []) map.set(m.card?.instanceId, { ...m, side });
    return map;
}
function keywords(unit) { return new Set((unit?.status ?? []).map(keywordOf).filter(Boolean)); }

// Arrow auras use the engine rule: ← / → reach the same side's neighbouring slot, ↑ reaches the facing enemy slot.
export function auraSource(target, all, keyword = '聖盾') {
    for (const source of all.values()) {
        if (source === target || source.card?.instanceId === target.card?.instanceId) continue;
        if (!String(source.card?.text ?? '').includes(keyword) || (source.status ?? []).some(s => String(s).startsWith('沉默'))) continue;
        for (const arrow of source.card?.arrows ?? []) {
            const same = source.side === target.side;
            if (arrow === 'left' && same && target.slot === source.slot - 1) return source.card.instanceId;
            if (arrow === 'right' && same && target.slot === source.slot + 1) return source.card.instanceId;
            if (arrow === 'up' && !same && target.slot === 4 - source.slot) return source.card.instanceId;
        }
    }
    return null;
}

export function planSteps(previous, next, events) {
    const steps = [], before = units(previous), after = units(next);
    const ordered = [...(events ?? [])].sort((a, b) => a.order - b.order);
    const badged = new Set(), attacked = new Set(), turnStart = ordered.some(ev => ev.type === 'turn');
    let source = null;
    const target = ev => ev.type === 'attach' ? ev.targetId : ev.instanceId;
    for (const ev of ordered) {
        if (ev.type === 'play' && isSpellCard(ev.card)) {
            steps.push({ kind: 'showcase', ev });
            source = { id: ev.instanceId, showcase: true };
            continue;
        }
        if (SOURCE_TYPES.has(ev.type)) { steps.push({ kind: 'source', ev }); source = { id: ev.instanceId }; continue; }
        if (ev.type === 'attack') { attacked.add(ev.instanceId); source = null; }
        if (ev.type === 'turn' || ev.type === 'play') source = null;
        const step = { kind: 'event', ev };
        if (source && TARGETED.has(ev.type) && source.id !== target(ev)) { step.from = source.id; step.fromShowcase = !!source.showcase; }
        steps.push(step);
        if (ev.type === 'death' && keywordOf(ev.label)) badged.add(`${ev.instanceId}:${keywordOf(ev.label)}`);
        if (ev.type === 'status' && keywordOf(ev.label)) badged.add(`${ev.instanceId}:${keywordOf(ev.label)}`);
        if (ev.type === 'summon') {
            source = { id: ev.instanceId };
            const unit = after.get(ev.instanceId);
            for (const k of keywords(unit)) {
                badged.add(`${ev.instanceId}:${k}`);
                steps.push({ kind: 'badge', target: ev.instanceId, keyword: k, from: k === '聖盾' && unit ? auraSource(unit, after) : null });
            }
        }
    }
    // Results that have no engine event of their own: auras, buffs, debuffs and taps, read from the public snapshots.
    for (const [id, unit] of after) {
        const old = before.get(id);
        if (!old) continue;
        const was = keywords(old);
        for (const k of keywords(unit)) {
            if (was.has(k) || badged.has(`${id}:${k}`)) continue;
            steps.push({ kind: 'badge', target: id, keyword: k, from: k === '聖盾' ? auraSource(unit, after) : null });
        }
        const delta = (Number(unit.pp) || 0) - (Number(old.pp) || 0);
        if (delta && Number.isFinite(delta)) steps.push({ kind: 'stat', target: id, delta });
        // Units ready themselves at the start of a turn; only taps or readies caused mid-turn are effects.
        if (!turnStart && !!unit.tapped !== !!old.tapped && !attacked.has(id)) steps.push({ kind: 'tap', target: id, tapped: !!unit.tapped });
    }
    return steps;
}

// Long batches (a whole AI combo) speed up a little so a turn stays readable without dragging on.
export function batchSpeed(stepCount) { return stepCount > 14 ? 1.8 : stepCount > 8 ? 1.35 : 1; }
