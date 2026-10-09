(() => {
    'use strict';
    const progress = document.getElementById('startup-progress');
    const percentage = document.getElementById('startup-percentage');
    const status = document.getElementById('startup-status');
    const detail = document.getElementById('startup-detail');
    if (!progress || !percentage || !status) return;
    // Loading-screen language: the last language the app applied (mirrored by i18n.js), otherwise the same
    // browser rule as Localizer.PickFromBrowser (English only when the first preference is en/en-*). Default zh-Hant.
    const english = (() => {
        try { const saved = globalThis.localStorage?.getItem('wcg.language'); if (saved === 'en' || saved === 'zh-Hant') return saved === 'en'; } catch { /* storage blocked */ }
        const first = String(globalThis.navigator?.languages?.find(x => String(x).trim()) ?? globalThis.navigator?.language ?? '').trim().toLowerCase();
        return first === 'en' || first.startsWith('en-');
    })();
    const EN = {
        '魂誓': 'Soul Oath', '正在檢查遊戲資源…': 'Checking game assets…', '遊戲下載進度': 'Game download progress',
        '首次開啟會下載全部卡圖與音效，完成後自動進入。': 'The first visit downloads all card art and sounds, then starts automatically.',
        '正在下載完整遊戲資源…': 'Downloading all game assets…', '下載完成，正在啟動遊戲…': 'Download complete, starting the game…',
        '載入失敗': 'Loading failed', '下載或啟動失敗，請重新整理接續下載。': 'Download or startup failed. Refresh to resume the download.',
        '資源清單不完整': 'The asset list is incomplete.', '資源清單格式錯誤': 'The asset list is malformed.', '檔案大小不符': 'File size mismatch.',
        '檔案完整性檢查失敗': 'File integrity check failed.', '瀏覽器儲存空間不足，請釋放空間後重試。': 'Not enough browser storage. Free some space and try again.',
        '資源下載未完成，請重新整理接續下載。': 'The asset download did not finish. Refresh to resume.',
        '請使用支援遊戲資源儲存的瀏覽器，並允許此網站儲存資料。': 'Use a browser that supports storing game assets, and allow this site to store data.',
        '無法取得遊戲資源清單，請重新整理再試。': 'Could not load the asset list. Refresh and try again.',
        '遊戲資源儲存未啟動，請重新整理再試。': 'Game asset storage did not start. Refresh and try again.',
        '資源檢查未完成，請重新整理再試。': 'The asset check did not finish. Refresh and try again.',
        '載入發生錯誤。': 'Something went wrong while loading. ', '重新整理': 'Reload'
    };
    const t = zh => english && EN[zh] || zh;
    if (english) {
        try {
            const doc = globalThis.document;
            doc.documentElement.lang = 'en'; doc.title = 'Soul Oath';
            const h1 = doc.querySelector('.startup h1'); if (h1) h1.textContent = t(h1.textContent);
            status.textContent = t(status.textContent); if (detail) detail.textContent = t(detail.textContent);
            progress.setAttribute('aria-label', t('遊戲下載進度'));
            const error = doc.getElementById('blazor-error-ui');
            if (error?.firstChild) { error.firstChild.textContent = t('載入發生錯誤。'); const reload = error.querySelector('.reload'); if (reload) reload.textContent = t('重新整理'); }
        } catch { /* Partial DOM (tests, very old browsers): keep the zh-Hant defaults. */ }
    }
    let failed = false;
    window.wcgStartup = {
        ready: import(new URL('asset-preload.js', document.baseURI).href).then(module => module.prepareGame(state => {
            if (failed) return;
            // Bytes actually received or previously verified, never a timer estimate.
            const value = state.completed === state.count ? 100 : Math.min(99, Math.floor(state.bytes / state.total * 100));
            progress.value = value; progress.setAttribute('value', value);
            percentage.textContent = `${value}%`;
            status.textContent = t('正在下載完整遊戲資源…');
            if (detail) detail.textContent = `${(state.bytes / 1000000).toFixed(1)} / ${(state.total / 1000000).toFixed(1)} MB · ${state.completed} / ${state.count} ${english ? 'files' : '項'}`;
        })).then(() => { if (!failed) status.textContent = t('下載完成，正在啟動遊戲…'); }),
        fail(error) {
            failed = true;
            if (!progress.isConnected) return;
            progress.closest('.startup')?.classList.add('failed');
            progress.setAttribute('aria-valuetext', t('載入失敗'));
            status.textContent = t(error?.message || '下載或啟動失敗，請重新整理接續下載。');
        }
    };
    // Inline bootstrap attaches the rejection handler immediately.
})();
