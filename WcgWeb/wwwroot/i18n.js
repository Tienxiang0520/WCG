// Interface language helpers: the browser's preferred languages (only used while no choice is saved)
// and the <html lang> attribute, mirrored to localStorage so the static loading screen can follow it.
export function browserLanguages() {
    return Array.isArray(navigator.languages) && navigator.languages.length ? [...navigator.languages] : [navigator.language || ''];
}
export function apply(language) {
    const lang = language === 'en' ? 'en' : 'zh-Hant';
    document.documentElement.lang = lang;
    try { localStorage.setItem('wcg.language', lang); } catch { /* Storage may be unavailable; the attribute is enough. */ }
    // The static build's loading-error bar lives outside Blazor (index.html), so it is relabelled here.
    const [text, reload] = lang === 'en' ? ['Something went wrong while loading. ', 'Reload'] : ['載入發生錯誤。', '重新整理'];
    for (const bar of document.querySelectorAll('[data-loading-error]')) {
        if (bar.firstChild?.nodeType === Node.TEXT_NODE) bar.firstChild.textContent = text;
        const link = bar.querySelector('.reload'); if (link) link.textContent = reload;
    }
}
