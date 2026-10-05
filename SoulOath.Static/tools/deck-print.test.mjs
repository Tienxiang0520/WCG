import test from 'node:test';
import assert from 'node:assert/strict';
import { printDeck } from '../../LcgWeb/wwwroot/deck-print.js';

function fixture(context, images = [], text = []) {
    const oldDocument = globalThis.document, oldWindow = globalThis.window;
    context.after(() => { globalThis.document = oldDocument; globalThis.window = oldWindow; });
    let prints = 0;
    globalThis.document = { fonts: { ready: Promise.resolve() } };
    globalThis.window = { print() { prints++; } };
    return { root: { querySelectorAll(selector) { return selector === 'img' ? images : text; } }, prints: () => prints };
}
test('printing waits for fonts and decodes every card image', async context => {
    let decoded = 0;
    const f = fixture(context, [{ naturalWidth: 1024, async decode() { decoded++; } }, { naturalWidth: 1024, async decode() { decoded++; } }]);
    await printDeck(f.root); assert.equal(decoded, 2); assert.equal(f.prints(), 1);
});
test('missing card art prevents an incomplete color print', async context => {
    const f = fixture(context, [{ alt: '熔岩地精', async decode() { throw new Error('not loaded'); } }]);
    await assert.rejects(printDeck(f.root), /熔岩地精.*載入失敗/); assert.equal(f.prints(), 0);
});
test('overflowing text prevents silently truncated printed rules', async context => {
    const f = fixture(context, [], [{ scrollHeight: 90, clientHeight: 40, scrollWidth: 60, clientWidth: 60 }]);
    await assert.rejects(printDeck(f.root), /文字超出/); assert.equal(f.prints(), 0);
});
test('economy mode prints without loading images', async context => {
    const f = fixture(context);
    await printDeck(f.root); assert.equal(f.prints(), 1);
});
