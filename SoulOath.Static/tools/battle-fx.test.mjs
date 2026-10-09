import test from 'node:test';
import assert from 'node:assert/strict';
import { createFx, readMotionSetting, saveMotionSetting, motionReduced, MOTION_KEY } from '../../WcgWeb/wwwroot/battle/battle-fx.js';
import { bindFeedback } from '../../WcgWeb/wwwroot/battle/battle-feedback.js';

function storage(initial = {}) { const m = new Map(Object.entries(initial)); return { m, getItem: k => m.get(k) ?? null, setItem: (k, v) => m.set(k, String(v)), removeItem: k => m.delete(k) }; }
function dom(t, { reduce = false, store = storage() } = {}) {
    const running = [];
    class El {
        constructor(tag = 'div') { this.tag = tag; this.style = {}; this.attributes = {}; this.dataset = {}; this.children = []; this.textContent = ''; this.className = ''; this.inert = false; }
        getAttributeNames() { return ['b-test']; }
        setAttribute(k, v) { this.attributes[k] = v; } removeAttribute(k) { delete this.attributes[k]; }
        addEventListener() { }
        append(...els) { for (const el of els) { el.parent = this; this.children.push(el); } }
        remove() { if (this.parent) { this.parent.children = this.parent.children.filter(x => x !== this); this.parent = null; } }
        getBoundingClientRect() { return { x: 10, y: 20, width: 160, height: 200 }; }
        cloneNode() { const el = new El(); el.className = this.className; el.textContent = this.textContent; el.style = { ...this.style }; return el; }
        querySelector(selector) { return this.selectors?.[selector] ?? null; }
        querySelectorAll(selector) { return this.lists?.[selector] ?? []; }
        animate(frames, options) {
            let resolve, reject;
            const a = { el: this, frames, options, done: false, finished: new Promise((y, n) => { resolve = y; reject = n; }),
                finish() { if (!this.done) { this.done = true; resolve(); } }, cancel() { if (!this.done) { this.done = true; reject(new Error('cancelled')); } } };
            running.push(a); return a;
        }
    }
    const body = new El('body'), html = new El('html'), root = new El('section');
    const old = { document: globalThis.document, window: globalThis.window, localStorage: globalThis.localStorage };
    const created = [];
    globalThis.document = { body, documentElement: html, createElement: tag => { const el = new El(tag); created.push(el); return el; } };
    globalThis.window = { matchMedia: () => ({ matches: reduce }) };
    Object.defineProperty(globalThis, 'localStorage', { value: store, configurable: true, writable: true });
    t.after(() => { globalThis.document = old.document; globalThis.window = old.window; Object.defineProperty(globalThis, 'localStorage', { value: old.localStorage, configurable: true, writable: true }); });
    const finishAll = async () => { for (let i = 0; i < 20; i++) { for (const a of running) a.finish(); await new Promise(setImmediate); } };
    return { El, body, html, root, running, store, finishAll, created };
}
const all = el => [el, ...el.children.flatMap(all)];

test('motion setting follows the system until the player chooses, and is stored per device', t => {
    const d = dom(t, { reduce: true });
    assert.equal(readMotionSetting(), 'system'); assert.equal(motionReduced(), true);
    saveMotionSetting('full'); assert.equal(d.store.m.get(MOTION_KEY), 'full'); assert.equal(motionReduced(), false);
    assert.equal(d.html.attributes['data-wcg-motion'], 'full');
    saveMotionSetting('reduced'); assert.equal(motionReduced(), true); assert.equal(d.html.attributes['data-wcg-motion'], 'reduced');
    saveMotionSetting('system'); assert.equal(d.store.m.has(MOTION_KEY), false);
});

test('decorative particles, rings and shake are skipped entirely in reduced motion', async t => {
    const d = dom(t, { store: storage({ [MOTION_KEY]: 'reduced' }) });
    const fx = createFx(d.root, { random: () => .5 });
    await Promise.all([fx.burst(d.root), fx.ring(d.root), fx.shake(1), fx.shatter(d.root), fx.recoil(d.root)]);
    fx.slam(d.root, 7); fx.impact(d.root, null, 1);
    assert.equal(d.running.length, 0); assert.equal(d.body.children.length, 0);
});

test('effects never take input, clean themselves up and are bounded in count', async t => {
    const d = dom(t);
    const fx = createFx(d.root, { random: () => .3 });
    void fx.burst(d.root, { count: 500 }); void fx.slam(d.root, 7); void fx.banner('你的回合', 'player');
    const layer = d.body.children[0];
    assert.equal(layer.className, 'wcg-fx-layer'); assert.equal(layer.attributes['aria-hidden'], 'true');
    assert.ok(layer.children.length <= 92, 'particle budget (plus one ring and one banner)');
    assert.ok(d.running.some(a => a.el === d.root), 'heavy card landings shake the board');
    await d.finishAll();
    assert.equal(d.body.children.length, 0, 'finished effects remove their layer');
    assert.equal(d.root.style.transform, undefined, 'shake leaves no lasting transform');
});

test('disposing mid-effect cancels everything immediately', async t => {
    const d = dom(t);
    const fx = createFx(d.root, { random: () => .7 });
    const pending = Promise.all([fx.shatter(d.root), fx.banner('勝利', 'victory'), fx.shake(.8)]);
    fx.dispose(); await pending;
    assert.equal(d.body.children.length, 0); assert.equal(fx.active, 0);
    await fx.burst(d.root); assert.equal(d.body.children.length, 0, 'no effects after dispose');
});

test('feedback with effects still releases the input lock on commit and keeps hidden cards hidden', async t => {
    const d = dom(t);
    const own = new d.El(), enemy = new d.El(), status = new d.El(), field = new d.El(), unit = new d.El(), zone = new d.El();
    zone.dataset.target = 'dead'; zone.selectors = { '.field-card': unit };
    d.root.selectors = { '.player.hero': own, '.opponent.hero': enemy, '.battle-message': status, '.battlefield': field, '.energy-zone': own };
    d.root.lists = { '[data-target]': [zone], '.v06-hand-card': [] };
    const fx = createFx(d.root, { random: () => .4 });
    const feedback = bindFeedback(d.root, null, fx);
    const state = { player: { field: [] }, computer: { field: [] } };
    const events = [
        { type: 'turn', side: 'player', order: 0, label: '第 2 回合' },
        { type: 'energy', side: 'computer', instanceId: 'secret', order: 1 },
        { type: 'death', side: 'computer', instanceId: 'dead', order: 2 },
        { type: 'damage', side: 'player', amount: 3, order: 3 },
        { type: 'gameover', side: 'computer', order: 4 }];
    const done = feedback.present(state, state, events);
    for (let i = 0; i < 60; i++) { for (const a of d.running) a.finish(); await new Promise(setImmediate); }
    await done;
    assert.equal(d.root.inert, true, 'board stays locked until the snapshot is committed');
    const texts = d.created.map(el => el.textContent).join(' ');
    assert.match(texts, /你的回合/); assert.doesNotMatch(texts, /secret/);
    feedback.clear(); assert.equal(d.root.inert, false);
    feedback.dispose(); assert.equal(d.body.children.length, 0);
});
