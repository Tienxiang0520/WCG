import test from 'node:test';
import assert from 'node:assert/strict';
test('site/API catalog check: same, different and unknown (older API or offline)', async () => {
    const { compareCatalog, checkCloudCatalog, catalogFiles } = await import('../wwwroot/cloud-version.js');
    const local = { cards: 'a'.repeat(64), presets: 'b'.repeat(64), rankedDecks: 'c'.repeat(64) };
    const byPath = Object.fromEntries(Object.entries(catalogFiles).map(([k, p]) => [p, local[k]]));
    const hashOf = async path => byPath[path];
    assert.equal(await compareCatalog({ data: local }, hashOf), true);
    assert.equal(await compareCatalog({ data: { ...local, cards: 'd'.repeat(64) } }, hashOf), false);
    assert.equal(await compareCatalog({ service: 'WCG Cloud Save', version: 1 }, hashOf), null);
    assert.equal(await compareCatalog({ data: { cards: 'x' } }, hashOf), null);
    const bytes = new TextEncoder().encode('[]');
    const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(x => x.toString(16).padStart(2, '0')).join('');
    const fetchFile = async url => url.endsWith('/health')
        ? new Response(JSON.stringify({ data: { cards: digest, presets: digest, rankedDecks: digest } }))
        : new Response(bytes);
    assert.equal(await checkCloudCatalog('https://api.test/api', { fetchFile, base: 'https://site.test/' }), true);
    assert.equal(await checkCloudCatalog('https://api.test/api', { fetchFile: async () => { throw new Error('offline'); }, base: 'https://site.test/' }), null);
});
