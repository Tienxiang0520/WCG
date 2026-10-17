// Site/API version check: the cloud save API validates saves against its own card catalog. Its /health
// reports the SHA-256 of those data files; this tab compares them with the data files it actually runs.
// true = same catalog, false = different (saves may be rejected), null = unknown (older API or offline).
export const catalogFiles = { cards: 'data/cards.json', presets: 'data/preset_decks.json', rankedDecks: 'data/ranked_decks.json' };
export async function sha256Of(url, fetchFile = fetch) {
    const response = await fetchFile(url);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const digest = await crypto.subtle.digest('SHA-256', await response.arrayBuffer());
    return [...new Uint8Array(digest)].map(x => x.toString(16).padStart(2, '0')).join('');
}
export async function compareCatalog(health, hashOf) {
    const data = health?.data;
    if (!data || Object.keys(catalogFiles).some(key => !/^[a-f0-9]{64}$/.test(data[key] ?? ''))) return null;
    for (const [key, path] of Object.entries(catalogFiles))
        if (data[key] !== await hashOf(path)) return false;
    return true;
}
export async function checkCloudCatalog(apiUrl, { fetchFile = fetch, base = document.baseURI } = {}) {
    try {
        const response = await fetchFile(apiUrl + '/health', { cache: 'no-store', signal: AbortSignal.timeout(10000) });
        if (!response.ok) return null;
        return await compareCatalog(await response.json(), path => sha256Of(new URL(path, base).href, fetchFile));
    } catch { return null; }
}
