// Keep keyboard and scrolling inside the modal; restore the page on close/navigation.
export function attach(dialog) {
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    function focusables() { return [...dialog.querySelectorAll('button:not(:disabled), a[href]')]; }
    function focus(event) {
        if (!dialog.contains(event.target)) (focusables()[0] ?? dialog).focus();
    }
    function key(event) {
        if (event.key !== 'Tab') return;
        const items = focusables(), first = items[0], last = items.at(-1);
        if (!first) { event.preventDefault(); dialog.focus(); return; }
        if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialog)) { event.preventDefault(); first.focus(); }
    }
    document.addEventListener('focusin', focus);
    dialog.addEventListener('keydown', key);
    return { dispose() {
        document.body.style.overflow = previous;
        document.removeEventListener('focusin', focus);
        dialog.removeEventListener('keydown', key);
    } };
}
