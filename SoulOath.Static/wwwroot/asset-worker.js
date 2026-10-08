'use strict';
const ASSETS = 'wcg-assets-v1', META = 'wcg-asset-manifests-v1';
const versions = new Map();
const root = self.registration.scope;
const key = path => new URL(path, root).href;
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
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
        } catch { event.ports[0]?.postMessage({ ok: false }); }
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
