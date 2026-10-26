// 訓練場 saved scenes. Stored only in this browser, separate from player saves, ranked records and replays.
// v2 keeps many named scenes; the old single v1 slot is copied in once (and left in place) as 「舊保存場面」.
const legacyKey = 'wcg.trainingScene.v1';
const key = 'wcg.trainingScenes.v2';
const maxScenes = 30;
function load() {
    const raw = localStorage.getItem(key);
    if (raw != null) { try { const list = JSON.parse(raw); if (Array.isArray(list)) return list; } catch { } }
    const legacy = localStorage.getItem(legacyKey);
    return legacy == null ? [] : [{ id: 'legacy-v1', name: '舊保存場面', savedAt: 0, scene: legacy }];
}
function store(list) { localStorage.setItem(key, JSON.stringify(list)); }
function cleanName(name) { const n = String(name ?? '').trim().slice(0, 40); if (!n) throw new Error('請輸入場面名稱。'); return n; }
export function listScenes() { return load().map(s => ({ id: s.id, name: s.name, savedAt: s.savedAt })); }
export function readScene(id) { return load().find(s => s.id === id)?.scene ?? null; }
// Saves a new scene, or overwrites the scene with this id. Returns the scene id.
export function saveScene(name, scene, id) {
    const list = load(); const now = Date.now();
    const existing = id ? list.find(s => s.id === id) : null;
    if (existing) { existing.scene = scene; existing.savedAt = now; if (name) existing.name = cleanName(name); }
    else {
        if (list.length >= maxScenes) throw new Error(`最多保存 ${maxScenes} 個場面，請先刪除不需要的場面。`);
        list.push({ id: 's' + now.toString(36) + Math.random().toString(36).slice(2, 8), name: cleanName(name), savedAt: now, scene });
    }
    store(list); return existing ? existing.id : list[list.length - 1].id;
}
export function renameScene(id, name) { const list = load(); const s = list.find(x => x.id === id); if (!s) throw new Error('找不到這個場面。'); s.name = cleanName(name); store(list); }
export function deleteScene(id) { const list = load(); const next = list.filter(x => x.id !== id); if (next.length === list.length) throw new Error('找不到這個場面。'); store(next); }
