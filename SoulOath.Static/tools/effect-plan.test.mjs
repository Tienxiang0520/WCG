import test from 'node:test';
import assert from 'node:assert/strict';
import { planSteps, auraSource, batchSpeed, keywordOf, isSpellCard } from '../../WcgWeb/wwwroot/battle/effect-plan.js';

const card = (id, extra = {}) => ({ instanceId: id, cardId: 'WCG-101', name: id, type: '怪物', text: '', arrows: [], ...extra });
const unit = (id, slot, extra = {}, cardExtra = {}) => ({ card: card(id, cardExtra), slot, pp: 1000, dp: 1, status: ['直立'], tapped: false, ...extra });
const snap = (player = [], computer = []) => ({ player: { field: player }, computer: { field: computer } });
const kinds = steps => steps.map(s => s.kind === 'event' ? `${s.ev.type}${s.from ? '<' + s.from : ''}` : `${s.kind}:${s.keyword ?? s.ev?.type ?? s.delta ?? s.tapped ?? ''}${s.from ? '<' + s.from : ''}`);

test('a spell is showcased first and becomes the source of the effects it causes', () => {
    const spell = card('bolt', { type: '法術', text: '消滅 1 隻敵怪，抽 1 張。' });
    const steps = planSteps(snap([], [unit('wolf', 2)]), snap(), [
        { type: 'pay', side: 'player', order: 0 }, { type: 'play', side: 'player', instanceId: 'bolt', card: spell, order: 1 },
        { type: 'death', side: 'computer', instanceId: 'wolf', label: 'bolt', order: 2 }, { type: 'draw', side: 'player', order: 3 }]);
    assert.deepEqual(kinds(steps), ['pay', 'showcase:play', 'death<bolt', 'draw<bolt']);
    assert.equal(steps[2].fromShowcase, true);
});
test('deploy abilities and triggers badge their source, then beam to what they affect', () => {
    const steps = planSteps(snap(), snap([unit('mage', 0)], [unit('foe', 1)]), [
        { type: 'summon', side: 'player', instanceId: 'mage', card: card('mage'), order: 0 },
        { type: 'effect', side: 'player', instanceId: 'mage', label: '進場能力', order: 1 },
        { type: 'bounce', side: 'computer', instanceId: 'foe', order: 2 },
        { type: 'trigger', side: 'computer', instanceId: 'foe', label: '離場能力', order: 3 },
        { type: 'heal', side: 'computer', amount: 1, order: 4 }]);
    assert.deepEqual(kinds(steps), ['summon', 'source:effect', 'bounce<mage', 'source:trigger', 'heal<foe']);
});
test('attacks and new turns end the previous source so combat results are not attributed to a spell', () => {
    const steps = planSteps(snap([unit('a', 0)], [unit('b', 0)]), snap([unit('a', 0)]), [
        { type: 'trigger', side: 'player', instanceId: 'a', order: 0 }, { type: 'attack', side: 'player', instanceId: 'a', targetId: 'b', order: 1 },
        { type: 'death', side: 'computer', instanceId: 'b', label: '交戰', order: 2 }]);
    assert.deepEqual(kinds(steps), ['source:trigger', 'attack', 'death']);
});
test('summoned keywords badge once; an arrow aura names the neighbour that grants holy shield', () => {
    const guard = unit('guard', 1, {}, { text: '嘲諷；【箭頭：← →】左右兩側相鄰格的己方怪物具有聖盾。', arrows: ['left', 'right'] });
    const before = snap([guard, unit('ally', 2)]);
    const after = snap([{ ...guard, status: ['嘲諷', '直立'] }, unit('ally', 2, { status: ['聖盾', '直立'] }), unit('new', 0, { status: ['聖盾', '直立'] })]);
    const steps = planSteps(before, after, [{ type: 'summon', side: 'player', instanceId: 'new', card: card('new'), order: 0 }]);
    assert.deepEqual(kinds(steps), ['summon', 'badge:聖盾<guard', 'badge:嘲諷', 'badge:聖盾<guard']);
});
test('silence status events are not badged twice; pp changes and effect taps come from the snapshot diff', () => {
    const before = snap([unit('x', 0), unit('y', 1)]);
    const after = snap([unit('x', 0, { status: ['沉默', '直立'], pp: 1500 }), unit('y', 1, { tapped: true })]);
    const steps = planSteps(before, after, [{ type: 'status', side: 'player', instanceId: 'x', label: '沉默', order: 0 }]);
    assert.deepEqual(kinds(steps), ['status', 'stat:500', 'tap:true']);
});
test('a unit tapped by its own attack is not shown as an effect tap', () => {
    const steps = planSteps(snap([unit('a', 0)]), snap([unit('a', 0, { tapped: true })]), [{ type: 'attack', side: 'player', instanceId: 'a', order: 0 }]);
    assert.deepEqual(kinds(steps), ['attack']);
});
test('helpers: aura rule, keyword parsing, spell detection and long-batch pacing', () => {
    const up = { card: card('s', { text: '正對面同列格的敵方怪物具有聖盾', arrows: ['up'] }), slot: 1, side: 'player', status: [] };
    const all = new Map([['s', up]]);
    assert.equal(auraSource({ card: card('t'), slot: 3, side: 'computer' }, all), 's');
    assert.equal(auraSource({ card: card('t'), slot: 2, side: 'computer' }, all), null);
    assert.equal(auraSource({ card: card('t'), slot: 3, side: 'computer' }, new Map([['s', { ...up, status: ['沉默'] }]])), null);
    assert.equal(keywordOf('聖盾（附著）'), '聖盾'); assert.equal(keywordOf('直立'), null);
    assert.equal(isSpellCard({ type: '法術（反擊）' }), true); assert.equal(isSpellCard({ type: '結界' }), false);
    assert.equal(batchSpeed(3), 1); assert.ok(batchSpeed(20) > batchSpeed(10));
});
