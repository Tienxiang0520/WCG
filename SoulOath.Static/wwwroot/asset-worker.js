'use strict';
const ASSETS = 'wcg-assets-v1', META = 'wcg-asset-manifests-v1';
const versions = new Map();
const root = self.registration.scope;
const key = path => new URL(path, root).href;
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
// Older cache generations (renamed stores) are never read again.
self.addEventListener('activate', event => event.waitUntil((async () => {
    try { for (const name of await caches.keys()) if (name.startsWith('wcg-') && name !== ASSETS && name !== META) await caches.delete(name); } catch { /* best effort */ }
    await self.clients.claim();
})()));
// After a tab binds a fully verified version, drop what no open tab and no unfinished download still needs:
// bindings of closed tabs, their manifests, and asset entries listed by none of the kept manifests.
// Downloads in progress register a pending manifest (asset-preload.js) until their tab binds, so their files are kept too.
const PENDING_TTL = 6 * 60 * 60 * 1000;
async function prune(current) {
    const meta = await caches.open(META), assets = await caches.open(ASSETS);
    if (!meta.keys || !assets.keys || !self.clients?.matchAll) return;
    const live = new Set((await self.clients.matchAll({ includeUncontrolled: true, type: 'all' })).map(client => client.id));
    const clientPrefix = key('__wcg/client/'), manifestPrefix = key('__wcg/manifest/'), pendingPrefix = key('__wcg/pending/');
    const keep = new Set([current]), manifests = [];
    for (const request of await meta.keys()) {
        const url = request.url ?? String(request);
        if (url.startsWith(clientPrefix)) {
            if (live.has(url.slice(clientPrefix.length))) keep.add(await (await meta.match(url)).text());
            else { await meta.delete(url); versions.delete(url.slice(clientPrefix.length)); }
        } else if (url.startsWith(pendingPrefix)) {
            // A download in progress, or finished but not yet bound by its tab: keep its manifest and files.
            const started = Number((await meta.match(url))?.headers.get('X-WCG-Started'));
            if (Date.now() - started < PENDING_TTL) { manifests.push(url); keep.add(url.slice(pendingPrefix.length)); } else await meta.delete(url);
        } else if (url.startsWith(manifestPrefix)) manifests.push(url);
    }
    if (!(await meta.match(key(`__wcg/manifest/${current}`)))) return; // never prune without the bound version
    const wanted = new Set();
    for (const url of manifests) {
        if (url.startsWith(manifestPrefix) && !keep.has(url.slice(manifestPrefix.length))) { await meta.delete(url); continue; }
        const manifest = await (await meta.match(url)).json();
        for (const asset of manifest.assets) {
            const cacheKey = new URL(asset.path, root);
            cacheKey.searchParams.set('wcg-hash', asset.sha256);
            wanted.add(cacheKey.href);
        }
    }
    for (const request of await assets.keys()) {
        const url = request.url ?? String(request);
        if (!wanted.has(url)) await assets.delete(url);
    }
}
// A hard-reloaded tab starts without a controller; the already-active worker takes it over on request (asset-preload.js).
self.addEventListener('message', event => {
    if (event.data?.type === 'WCG_CLAIM') event.waitUntil(self.clients.claim());
});
self.addEventListener('message', event => {
    if (event.data?.type !== 'WCG_BIND' || !event.source?.id || !/^[a-f0-9]{64}$/.test(event.data.version)) return;
    event.waitUntil((async () => {
        try {
            const meta = await caches.open(META);
            const response = await meta.match(key(`__wcg/manifest/${event.data.version}`));
            if (!response) throw new Error('Missing manifest');
            const manifest = await response.json();
            await meta.put(key(`__wcg/client/${event.source.id}`), new Response(event.data.version));
            versions.set(event.source.id, manifest);
            event.ports[0]?.postMessage({ ok: true });
        } catch { event.ports[0]?.postMessage({ ok: false }); return; }
        try { await (await caches.open(META)).delete(key(`__wcg/pending/${event.data.version}`)); await prune(event.data.version); } catch { /* storage cleanup is best effort */ }
    })());
});
async function cachedAsset(event) {
    const meta = await caches.open(META);
    let manifest = versions.get(event.clientId);
    if (!manifest) {
        const binding = await meta.match(key(`__wcg/client/${event.clientId}`));
        if (binding) {
            const response = await meta.match(key(`__wcg/manifest/${await binding.text()}`));
            if (response) { manifest = await response.json(); versions.set(event.clientId, manifest); }
        }
    }
    const url = new URL(event.request.url);
    const asset = manifest?.assets.find(asset => new URL(asset.path, root).pathname === url.pathname);
    if (asset) {
        const cacheKey = new URL(asset.path, root);
        cacheKey.searchParams.set('wcg-hash', asset.sha256);
        const response = await (await caches.open(ASSETS)).match(cacheKey.href);
        if (response) return response;
    }
    return fetch(event.request);
}
self.addEventListener('fetch', event => {
    // Never intercept navigations, external services, writes, or preload requests.
    // Fresh HTML/manifest still see updates; player saves are never stored here.
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' || event.request.mode === 'navigate'
        || url.origin !== self.location.origin || !event.clientId
        || event.request.headers.has('X-WCG-Preload')) return;
    event.respondWith(cachedAsset(event));
});
