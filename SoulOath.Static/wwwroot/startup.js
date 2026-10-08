(() => {
    'use strict';
    const progress = document.getElementById('startup-progress');
    const percentage = document.getElementById('startup-percentage');
    const status = document.getElementById('startup-status');
    const detail = document.getElementById('startup-detail');
    if (!progress || !percentage || !status) return;
    let failed = false;
    window.wcgStartup = {
        ready: import(new URL('asset-preload.js', document.baseURI).href).then(module => module.prepareGame(state => {
            if (failed) return;
            // Bytes actually received or previously verified, never a timer estimate.
            const value = state.completed === state.count ? 100 : Math.min(99, Math.floor(state.bytes / state.total * 100));
            progress.value = value; progress.setAttribute('value', value);
            percentage.textContent = `${value}%`;
            status.textContent = '正在下載完整遊戲資源…';
            if (detail) detail.textContent = `${(state.bytes / 1000000).toFixed(1)} / ${(state.total / 1000000).toFixed(1)} MB · ${state.completed} / ${state.count} 項`;
        })).then(() => { if (!failed) status.textContent = '下載完成，正在啟動遊戲…'; }),
        fail(error) {
            failed = true;
            if (!progress.isConnected) return;
            progress.closest('.startup')?.classList.add('failed');
            progress.setAttribute('aria-valuetext', '載入失敗');
            status.textContent = error?.message || '下載或啟動失敗，請重新整理接續下載。';
        }
    };
    // Inline bootstrap attaches the rejection handler immediately.
})();
