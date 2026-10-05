const bindings = new WeakMap();
export function attach(sidebar, handle) {
    detach(handle);
    const controller = new AbortController();
    bindings.set(handle, controller);
    const options = { signal: controller.signal };
    const desktop = () => window.matchMedia('(min-width: 641px)').matches;
    const limit = () => Math.min(360, window.innerWidth * .45);
    const setWidth = value => {
        const width = Math.round(Math.max(200, Math.min(limit(), value)));
        sidebar.style.setProperty('--sidebar-width', `${width}px`);
        handle.setAttribute('aria-valuenow', String(width));
        handle.setAttribute('aria-valuemax', String(Math.floor(limit())));
        try { localStorage.setItem('soul-oath.sidebar-width', String(width)); } catch { }
    };
    try {
        const saved = Number(localStorage.getItem('soul-oath.sidebar-width'));
        if (saved >= 200 && saved <= 360) setWidth(saved);
    } catch { }
    let drag = null;
    handle.addEventListener('pointerdown', event => {
        if (!desktop() || event.button !== 0) return;
        event.preventDefault();
        drag = { id: event.pointerId, x: event.clientX, width: sidebar.getBoundingClientRect().width };
        handle.setPointerCapture(event.pointerId);
        sidebar.classList.add('resizing');
    }, options);
    handle.addEventListener('pointermove', event => {
        if (drag?.id === event.pointerId) setWidth(drag.width + event.clientX - drag.x);
    }, options);
    const finish = () => { drag = null; sidebar.classList.remove('resizing'); };
    handle.addEventListener('pointerup', finish, options);
    handle.addEventListener('pointercancel', finish, options);
    handle.addEventListener('lostpointercapture', finish, options);
    handle.addEventListener('keydown', event => {
        if (!desktop() || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const width = sidebar.getBoundingClientRect().width;
        setWidth(event.key === 'Home' ? 200 : event.key === 'End' ? limit() : width + (event.key === 'ArrowRight' ? 10 : -10));
    }, options);
}
export function detach(handle) {
    bindings.get(handle)?.abort();
    bindings.delete(handle);
}
