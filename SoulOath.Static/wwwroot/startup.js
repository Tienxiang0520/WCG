(() => {
    'use strict';
    const app = document.getElementById('app');
    const progress = document.getElementById('startup-progress');
    const percentage = document.getElementById('startup-percentage');
    const status = document.getElementById('startup-status');
    if (!app || !progress || !percentage || !status) return;

    let stopped = false;
    function stop() {
        if (stopped) return;
        stopped = true;
        resourceObserver.disconnect();
        renderObserver.disconnect();
    }
    function update() {
        if (stopped) return;
        if (!progress.isConnected) { stop(); return; }
        // Blazor reports completed boot files, including cached files. No timer estimates.
        const raw = Number.parseFloat(document.documentElement.style.getPropertyValue('--blazor-load-percentage'));
        const value = Number.isFinite(raw) ? Math.floor(Math.max(0, Math.min(100, raw))) : 0;
        progress.value = value;
        percentage.textContent = `${value}%`;
        status.textContent = value === 100 ? '下載完成，正在準備卡牌與對戰…' : '正在下載遊戲資料…';
    }
    const resourceObserver = new MutationObserver(update);
    resourceObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
    const renderObserver = new MutationObserver(update);
    renderObserver.observe(app, { childList: true });
    window.wcgStartup = {
        fail() {
            stop();
            if (!progress.isConnected) return;
            progress.closest('.startup')?.classList.add('failed');
            progress.setAttribute('aria-valuetext', '載入失敗');
            status.textContent = '下載或啟動失敗，請重新整理再試。';
        }
    };
    update();
})();
