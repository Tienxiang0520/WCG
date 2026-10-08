// Public game files only. Content-addressed entries let interrupted downloads resume
// and let different open game versions share unchanged artwork without mixing rules.
export const ASSET_CACHE = 'wcg-assets-v1';
export const META_CACHE = 'wcg-asset-manifests-v1';
export function assetKey(base, asset) {
    const url = new URL(asset.path, base);
    url.searchParams.set('wcg-hash', asset.sha256);
    return url.href;
}
export function manifestKey(base, version) { return new URL(`__wcg/manifest/${version}`, base).href; }
export function validateManifest(manifest) {
    if (!manifest || !/^[a-f0-9]{64}$/.test(manifest.version) || !Array.isArray(manifest.assets) || !manifest.assets.length)
        throw new Error('資源清單不完整');
    const paths = new Set();
    for (const asset of manifest.assets) {
        if (!/^[\w./-]+$/.test(asset.path) || asset.path.startsWith('/') || asset.path.split('/').includes('..')
            || !/\.(?:js|css|json|wasm|dat|blat|webp|jpg|png|svg|ico|ogg|mp3|wav|woff2?|ttf)$/.test(asset.path)
            || !/^[a-f0-9]{64}$/.test(asset.sha256) || !Number.isSafeInteger(asset.size) || asset.size <= 0 || paths.has(asset.path))
            throw new Error('資源清單格式錯誤');
        paths.add(asset.path);
    }
    return manifest;
}
export async function preloadAssets(manifest, { base, cacheStorage = caches, fetchFile = fetch, onProgress = () => {}, concurrency = 6 }) {
    validateManifest(manifest);
    const cache = await cacheStorage.open(ASSET_CACHE);
    const total = manifest.assets.reduce((sum, asset) => sum + asset.size, 0);
    const received = new Map();
    let cursor = 0, completed = 0, failed = false;
    function report(asset, size) {
        received.set(asset.path, Math.min(size, asset.size));
        onProgress({ bytes: [...received.values()].reduce((a, b) => a + b, 0), total, completed, count: manifest.assets.length });
    }
    async function download(asset) {
        const key = assetKey(base, asset);
        if (await cache.match(key)) { completed++; report(asset, asset.size); return; }
        // Retry transient connections without allowing a broken asset to open the game.
        for (let attempt = 0; attempt < 3; attempt++) {
            try {
                const response = await fetchFile(new URL(asset.path, base).href, {
                    cache: 'no-store', headers: { 'X-WCG-Preload': '1' }, signal: AbortSignal.timeout(180000)
                });
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const reader = response.body?.getReader();
                let bytes;
                if (reader) {
                    const chunks = []; let size = 0;
                    while (true) {
                        const chunk = await reader.read();
                        if (chunk.done) break;
                        size += chunk.value.byteLength;
                        if (size > asset.size) { await reader.cancel(); throw new Error('檔案大小不符'); }
                        chunks.push(chunk.value); report(asset, size);
                    }
                    bytes = new Uint8Array(size); let offset = 0;
                    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
                } else bytes = new Uint8Array(await response.arrayBuffer());
                const digest = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map(x => x.toString(16).padStart(2, '0')).join('');
                if (bytes.byteLength !== asset.size || digest !== asset.sha256) throw new Error('檔案完整性檢查失敗');
                const headers = new Headers(response.headers);
                // Fetch bodies are already decompressed; retaining encoding headers breaks cached decoding.
                headers.delete('content-encoding'); headers.delete('content-length');
                await cache.put(key, new Response(bytes, { headers, status: 200 }));
                completed++; report(asset, asset.size); return;
            } catch (error) {
                report(asset, 0);
                if (error.name === 'QuotaExceededError') throw new Error('瀏覽器儲存空間不足，請釋放空間後重試。');
                if (attempt === 2 || failed) throw new Error('資源下載未完成，請重新整理接續下載。', { cause: error });
            }
        }
    }
    await Promise.all(Array.from({ length: Math.min(concurrency, manifest.assets.length) }, async () => {
        try { while (!failed && cursor < manifest.assets.length) await download(manifest.assets[cursor++]); }
        catch (error) { failed = true; throw error; }
    }));
    await (await cacheStorage.open(META_CACHE)).put(manifestKey(base, manifest.version), new Response(JSON.stringify(manifest), { headers: { 'Content-Type': 'application/json' } }));
}
export async function prepareGame(onProgress) {
    if (!navigator.serviceWorker || !globalThis.caches) throw new Error('請使用支援遊戲資源儲存的瀏覽器，並允許此網站儲存資料。');
    const base = document.baseURI;
    const response = await fetch(new URL('asset-manifest.json', base), { cache: 'no-store', headers: { 'X-WCG-Preload': '1' } });
    if (!response.ok) throw new Error('無法取得遊戲資源清單，請重新整理再試。');
    const manifest = validateManifest(await response.json());
    await navigator.serviceWorker.register(new URL('asset-worker.js', base), { scope: new URL('.', base).pathname, updateViaCache: 'none' });
    await navigator.serviceWorker.ready;
    if (!navigator.serviceWorker.controller) await new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { navigator.serviceWorker.removeEventListener('controllerchange', changed); reject(new Error('遊戲資源儲存未啟動，請重新整理再試。')); }, 15000);
        function changed() { if (navigator.serviceWorker.controller) { clearTimeout(timeout); navigator.serviceWorker.removeEventListener('controllerchange', changed); resolve(); } }
        navigator.serviceWorker.addEventListener('controllerchange', changed); changed();
    });
    await preloadAssets(manifest, { base, onProgress });
    // Bind this tab only after every file is verified and available. Other open tabs
    // keep their own rules/assets version, including after the worker restarts.
    await new Promise((resolve, reject) => {
        const channel = new MessageChannel();
        const timeout = setTimeout(() => { channel.port1.close(); reject(new Error('資源檢查未完成，請重新整理再試。')); }, 15000);
        channel.port1.onmessage = event => { clearTimeout(timeout); channel.port1.close(); event.data?.ok ? resolve() : reject(new Error('資源檢查未完成，請重新整理再試。')); };
        navigator.serviceWorker.controller.postMessage({ type: 'WCG_BIND', version: manifest.version }, [channel.port2]);
    });
}
