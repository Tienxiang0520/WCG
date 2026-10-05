const previews = new Map();
export function attach(root) {
    const id = crypto.randomUUID();
    const active = document.activeElement;
    const overflow = document.body.style.overflow;
    const background = [...root.parentElement.children, ...document.querySelectorAll('.sidebar')]
        .filter(element => element !== root).map(element => [element, element.inert]);
    background.forEach(([element]) => { element.inert = true; });
    document.body.style.overflow = 'hidden';
    const controller = new AbortController();
    root.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); root.querySelector('[data-print-close]')?.click(); }
        if (event.key !== 'Tab') return;
        const controls = [...root.querySelectorAll('button:not([disabled]),input:not([disabled]),a[href]')];
        if (!controls.length) { event.preventDefault(); root.focus(); return; }
        const first = controls[0], last = controls.at(-1);
        if (event.shiftKey && (document.activeElement === first || document.activeElement === root)) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }, { signal: controller.signal });
    previews.set(id, { background, overflow, active, controller });
    root.querySelector('[data-print-close]')?.focus();
    return id;
}
export function detach(id) {
    const preview = previews.get(id);
    if (!preview) return;
    preview.controller.abort();
    preview.background.forEach(([element, inert]) => { element.inert = inert; });
    document.body.style.overflow = preview.overflow;
    if (preview.active?.isConnected) preview.active.focus();
    previews.delete(id);
}
export async function printDeck(root) {
    await document.fonts.ready;
    await Promise.all([...root.querySelectorAll('img')].map(async image => {
        try { await image.decode(); }
        catch { throw new Error(`卡圖「${image.alt}」載入失敗，請稍後再試或選擇省墨文字版。`); }
        if (!image.naturalWidth) throw new Error('卡圖尚未載入。');
    }));
    const overflow = [...root.querySelectorAll('.print-card-title,.print-card-text')]
        .find(element => element.scrollHeight > element.clientHeight + 1 || element.scrollWidth > element.clientWidth + 1);
    if (overflow) throw new Error('卡牌文字超出列印範圍，請選擇省墨文字版後再試。');
    window.print();
}
