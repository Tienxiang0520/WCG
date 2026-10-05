(() => {
    'use strict';
    const key = 'soul-oath.player.v1';
    const preferenceKeys = ['wcg.confirmEnergy', 'soul-oath.sidebar-width'];
    const legacyEnergyKey = 'lcg.confirmEnergy';
    const legacyCardPrefix = 'LCG-';
    function canonicalData(data) {
        return Object.fromEntries(Object.entries(data).map(([name, value]) => [name,
            JSON.stringify(JSON.parse(value), (field, item) => field === 'CardIds' && Array.isArray(item)
                ? item.map(id => typeof id === 'string' && id.startsWith(legacyCardPrefix) ? 'WCG-' + id.slice(legacyCardPrefix.length) : id)
                : item)
        ]));
    }
    function preference(name) {
        return localStorage.getItem(name) ?? (name === 'wcg.confirmEnergy' ? localStorage.getItem(legacyEnergyKey) : null);
    }
    const allowed = new Set(['decks', 'ranked']);
    let canWrite = false;
    let release;
    const ready = new Promise(resolve => {
        if (!navigator.locks?.request) { resolve(); return; }
        navigator.locks.request('soul-oath-player-write', { ifAvailable: true }, async lock => {
            canWrite = Boolean(lock);
            resolve();
            if (lock) await new Promise(done => { release = done; });
        }).catch(() => resolve());
    });
    window.addEventListener('pagehide', () => { canWrite = false; release?.(); }, { once: true });
    function requireWriter() {
        if (!navigator.locks?.request) throw new Error('這個瀏覽器不支援安全存檔，請使用新版瀏覽器。');
        if (!canWrite) throw new Error('另一個魂誓分頁正在使用存檔。請關閉該分頁後重新整理。');
    }
    function readRoot() {
        const raw = localStorage.getItem(key);
        if (raw === null) return { version: 1, data: {} };
        const root = JSON.parse(raw);
        if (root?.version !== 1 || !root.data || typeof root.data !== 'object' || Array.isArray(root.data) ||
            Object.entries(root.data).some(([name, value]) => !allowed.has(name) || typeof value !== 'string'))
            throw new Error('本機存檔格式不正確，原資料保留。');
        return root;
    }
    function parseBackup(text) {
        if (typeof text !== 'string' || text.length > 5 * 1024 * 1024) throw new Error('備份過大。');
        const backup = JSON.parse(text);
        if (backup?.format !== 'soul-oath-local' || backup.version !== 1 || !backup.data ||
            typeof backup.data !== 'object' || Array.isArray(backup.data) || !backup.preferences ||
            typeof backup.preferences !== 'object' || Array.isArray(backup.preferences)) throw new Error('這不是魂誓存檔。');
        for (const [name, value] of Object.entries(backup.data)) {
            if (!allowed.has(name) || typeof value !== 'string') throw new Error('備份項目不正確。');
            JSON.parse(value);
        }
        const normalizedPreferences = {};
        for (const [originalName, value] of Object.entries(backup.preferences)) {
            const name = originalName === legacyEnergyKey ? 'wcg.confirmEnergy' : originalName;
            if (!preferenceKeys.includes(name) || typeof value !== 'string') throw new Error('備份設定不正確。');
            if (name === 'wcg.confirmEnergy' && !['true', 'false'].includes(value)) throw new Error('能量確認設定不正確。');
            if (name === 'soul-oath.sidebar-width' && (!Number.isFinite(Number(value)) || Number(value) < 200 || Number(value) > 360)) throw new Error('側邊欄寬度不正確。');
            if (normalizedPreferences[name] !== undefined && normalizedPreferences[name] !== value) throw new Error('備份設定互相衝突。');
            normalizedPreferences[name] = value;
        }
        backup.preferences = normalizedPreferences;
        backup.data = canonicalData(backup.data);
        return backup;
    }
    window.soulOathStorage = {
        ready,
        read(name) {
            if (!allowed.has(name)) throw new Error('不支援的存檔項目。');
            return readRoot().data[name] ?? null;
        },
        write(name, value, expected) {
            requireWriter();
            if (!allowed.has(name) || typeof value !== 'string') throw new Error('不支援的存檔項目。');
            const root = readRoot();
            if ((root.data[name] ?? null) !== expected) throw new Error('另一個分頁已更新存檔，請重新整理後再操作。');
            root.data[name] = value;
            localStorage.setItem(key, JSON.stringify(root));
        },
        exportBackup() {
            const root = readRoot();
            const preferences = Object.fromEntries(preferenceKeys.flatMap(name => {
                const value = preference(name);
                return value === null ? [] : [[name, value]];
            }));
            const backup = { format: 'soul-oath-local', version: 1, exportedAt: new Date().toISOString(), data: canonicalData(root.data), preferences };
            const url = URL.createObjectURL(new Blob([JSON.stringify(backup, null, 2)], { type: 'application/json' }));
            const a = document.createElement('a');
            a.href = url; a.download = `魂誓存檔-${new Date().toISOString().slice(0, 10)}.json`;
            document.body.append(a); a.click(); a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        inspectBackup(text) {
            const backup = parseBackup(text);
            const decks = backup.data.decks ? JSON.parse(backup.data.decks) : [];
            if (!Array.isArray(decks)) throw new Error('牌組備份格式不正確。');
            return `${decks.length} 套自訂牌組${backup.data.ranked ? '、天梯進度' : ''}與本機設定。`;
        },
        importBackup(text) {
            requireWriter();
            const backup = parseBackup(text);
            const before = new Map([key, ...preferenceKeys, legacyEnergyKey].map(name => [name, localStorage.getItem(name)]));
            try {
                for (const name of preferenceKeys) {
                    if (backup.preferences[name] === undefined) localStorage.removeItem(name);
                    else localStorage.setItem(name, backup.preferences[name]);
                }
                localStorage.removeItem(legacyEnergyKey);
                localStorage.setItem(key, JSON.stringify({ version: 1, data: backup.data }));
            } catch (error) {
                for (const [name, value] of before) {
                    try { if (value === null) localStorage.removeItem(name); else localStorage.setItem(name, value); } catch { }
                }
                throw error;
            }
            location.reload();
        }
    };
    // Optional read-only tools share the same save adapter as the visible game.
    if (document.modelContext?.registerTool) {
        const lifecycle = new AbortController();
        for (const tool of [
            { name: 'read_saved_decks', title: '讀取本機牌組', description: '讀取這個瀏覽器已儲存的魂誓自訂牌組。', field: 'decks', fallback: [] },
            { name: 'read_ranked_progress', title: '讀取本機天梯進度', description: '讀取這個瀏覽器的魂誓天梯存檔，不修改進度。', field: 'ranked', fallback: null }
        ]) {
            try {
                Promise.resolve(document.modelContext.registerTool({
                    name: tool.name, title: tool.title, description: tool.description,
                    inputSchema: { type: 'object', properties: {}, additionalProperties: false },
                    annotations: { readOnlyHint: true, untrustedContentHint: true },
                    execute(input) {
                        if (!input || typeof input !== 'object' || Array.isArray(input) || Object.keys(input).length) throw new Error('不接受額外參數。');
                        const value = window.soulOathStorage.read(tool.field);
                        return value === null ? tool.fallback : JSON.parse(value);
                    }
                }, { signal: lifecycle.signal })).catch(() => {});
            } catch { }
        }
        window.addEventListener('pagehide', () => lifecycle.abort(), { once: true });
    }
})();
