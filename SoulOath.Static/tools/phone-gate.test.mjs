import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../wwwroot/phone-gate.js', import.meta.url), 'utf8');
const css = await readFile(new URL('../../WcgWeb/wwwroot/wcg.css', import.meta.url), 'utf8');
const startup = await readFile(new URL('../wwwroot/startup.js', import.meta.url), 'utf8');
const index = await readFile(new URL('../wwwroot/index.html', import.meta.url), 'utf8');

function run({ matches, language = null }) {
    const children = [];
    const app = { innerHTML: 'loader', append: (...x) => children.push(...x) };
    const el = () => ({ setAttribute() {}, append() {}, textContent: '' });
    let listener = null, reloaded = false;
    const context = {
        localStorage: { getItem: () => language }, navigator: { languages: ['zh-TW'] },
        matchMedia: () => ({ matches, addEventListener: (_, f) => { listener = f; } }),
        location: { reload: () => { reloaded = true; } },
        document: { getElementById: () => app, createElement: el, documentElement: { lang: 'zh-Hant' }, title: '魂誓' },
    };
    vm.createContext(context); vm.runInContext(source, context);
    return { gate: context.wcgPhoneGate, app, children, context, change: m => listener?.({ matches: m }), reloaded: () => reloaded };
}

test('rule: coarse pointer and shorter side under 600 px', () => {
    const { gate } = run({ matches: false });
    assert.equal(gate.isPhone({ width: 390, height: 844, coarse: true }), true);   // phone portrait
    assert.equal(gate.isPhone({ width: 844, height: 390, coarse: true }), true);   // phone landscape
    assert.equal(gate.isPhone({ width: 744, height: 1133, coarse: true }), false); // iPad mini
    assert.equal(gate.isPhone({ width: 600, height: 960, coarse: true }), false);  // 7–8" tablet
    assert.equal(gate.isPhone({ width: 500, height: 400, coarse: false }), false); // small desktop window
});
test('phone shows the notice and blocks the game download', () => {
    const r = run({ matches: true });
    assert.equal(r.gate.blocked, true);
    assert.equal(r.app.innerHTML, '');
    assert.equal(r.children.length, 1);
    assert.equal(r.gate.message, '本遊戲目前只支援平板與電腦，請改用大螢幕遊玩');
    r.change(false); assert.equal(r.reloaded(), true);
});
test('English notice follows the saved language', () => {
    const r = run({ matches: true, language: 'en' });
    assert.match(r.gate.message, /tablets and computers only/);
    assert.equal(r.context.document.documentElement.lang, 'en');
});
test('tablets and computers are not blocked', () => {
    const r = run({ matches: false });
    assert.equal(r.gate.blocked, false); assert.equal(r.app.innerHTML, 'loader');
});
test('CSS gate, startup and bootstrap use the same rule', () => {
    const { gate } = run({ matches: false });
    assert.ok(css.includes(`@media ${gate.query}`), 'wcg.css media query must match phone-gate.js');
    assert.match(startup, /wcgPhoneGate\?\.blocked\) return/);
    assert.ok(index.indexOf('phone-gate.js') < index.indexOf('startup.js'));
    assert.match(index, /if \(!window\.wcgPhoneGate\?\.blocked\) Promise\.all/);
});
