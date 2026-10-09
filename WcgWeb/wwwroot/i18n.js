// Interface language helpers: the browser's preferred languages (only used while no choice is saved)
// and the <html lang> attribute, mirrored to localStorage so the static loading screen can follow it.
export function browserLanguages() {
    return Array.isArray(navigator.languages) && navigator.languages.length ? [...navigator.languages] : [navigator.language || ''];
}
export function apply(language) {
    const lang = language === 'en' ? 'en' : 'zh-Hant';
    document.documentElement.lang = lang;
    try { localStorage.setItem('wcg.language', lang); } catch { /* Storage may be unavailable; the attribute is enough. */ }
}
