// Player avatar upload: validate a local image, let the player pan/zoom it inside a circle and export a small square.
// Nothing leaves the browser; the cropped picture is handed back to the app as a data URL.
export const ACCEPTED = ['image/png', 'image/jpeg', 'image/webp', 'image/gif'];
export const MAX_FILE_BYTES = 8 * 1024 * 1024;
export const MAX_SIDE = 8192;
export const OUTPUT = 256;
export const MAX_STORED_CHARS = 300 * 1024;
export const MAX_ZOOM = 4;

// Identify the real format from the first bytes; the browser-reported type or file name alone is not trusted.
export function sniff(bytes) {
    const b = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    const ascii = (from, to) => String.fromCharCode(...b.subarray(from, to));
    if (b.length >= 8 && b[0] === 0x89 && ascii(1, 4) === 'PNG' && b[4] === 0x0d && b[5] === 0x0a && b[6] === 0x1a && b[7] === 0x0a) return 'image/png';
    if (b.length >= 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return 'image/jpeg';
    if (b.length >= 6 && (ascii(0, 6) === 'GIF87a' || ascii(0, 6) === 'GIF89a')) return 'image/gif';
    if (b.length >= 12 && ascii(0, 4) === 'RIFF' && ascii(8, 12) === 'WEBP') return 'image/webp';
    return null;
}
export function checkFile(file, head) {
    if (!file) return '沒有選擇檔案。';
    if (!file.size) return '檔案是空的。';
    if (file.size > MAX_FILE_BYTES) return '圖片超過 8 MB，請換一張較小的圖片。';
    const kind = sniff(head);
    if (!kind) return '只支援 PNG、JPEG、WebP 或 GIF 圖片。';
    if (file.type && file.type !== kind && !(file.type === 'image/jpg' && kind === 'image/jpeg')) return '圖片副檔名與內容不符。';
    return null;
}
export function checkSize(width, height) {
    if (!(width >= 16 && height >= 16)) return '圖片太小（至少 16×16）。';
    if (width > MAX_SIDE || height > MAX_SIDE) return `圖片太大（單邊最多 ${MAX_SIDE} 像素）。`;
    return null;
}
// View: zoom >= 1 relative to "cover the circle", offset of the picture centre from the circle centre (in box pixels).
export function clampView(view, width, height, box) {
    const zoom = Math.min(MAX_ZOOM, Math.max(1, Number.isFinite(view.zoom) ? view.zoom : 1));
    const scale = box / Math.min(width, height) * zoom;
    const limitX = Math.max(0, (width * scale - box) / 2), limitY = Math.max(0, (height * scale - box) / 2);
    const clamp = (value, limit) => Math.min(limit, Math.max(-limit, Number.isFinite(value) ? value : 0));
    return { zoom, x: clamp(view.x, limitX), y: clamp(view.y, limitY) };
}
export function sourceRect(view, width, height, box) {
    const v = clampView(view, width, height, box), scale = box / Math.min(width, height) * v.zoom, size = box / scale;
    return { sx: width / 2 - (box / 2 + v.x) / scale, sy: height / 2 - (box / 2 + v.y) / scale, sw: size, sh: size };
}
// Zoom around a point (box coordinates) so the spot under the fingers stays put.
export function zoomAt(view, zoom, px, py, width, height, box) {
    const v = clampView(view, width, height, box), ratio = Math.min(MAX_ZOOM, Math.max(1, zoom)) / v.zoom;
    const cx = px - box / 2, cy = py - box / 2;
    return clampView({ zoom: v.zoom * ratio, x: cx - (cx - v.x) * ratio, y: cy - (cy - v.y) * ratio }, width, height, box);
}

async function decode(file) {
    if (globalThis.createImageBitmap) {
        try { return await createImageBitmap(file, { imageOrientation: 'from-image' }); } catch { }
    }
    const url = URL.createObjectURL(file);
    try {
        const img = new Image(); img.decoding = 'async'; img.src = url;
        await img.decode();
        return img;
    } finally { URL.revokeObjectURL(url); }
}
const dims = image => ({ width: image.naturalWidth ?? image.width, height: image.naturalHeight ?? image.height });
function canvasBlob(canvas, type, quality) { return new Promise(resolve => canvas.toBlob(resolve, type, quality)); }
function readDataUrl(blob) {
    return new Promise((resolve, reject) => { const r = new FileReader(); r.onload = () => resolve(r.result); r.onerror = () => reject(r.error); r.readAsDataURL(blob); });
}
export async function encode(image, view, box) {
    const { width, height } = dims(image), r = sourceRect(view, width, height, box);
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = OUTPUT;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#0c1a22'; ctx.fillRect(0, 0, OUTPUT, OUTPUT);
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(image, r.sx, r.sy, r.sw, r.sh, 0, 0, OUTPUT, OUTPUT);
    for (const quality of [.86, .72, .55, .4]) {
        let blob = await canvasBlob(canvas, 'image/webp', quality);
        if (!blob || blob.type !== 'image/webp') blob = await canvasBlob(canvas, 'image/jpeg', quality);
        if (!blob) break;
        const url = await readDataUrl(blob);
        if (url.length <= MAX_STORED_CHARS) return url;
    }
    throw new Error('圖片壓縮後仍然過大。');
}

export function attach(root, dotnet) {
    const input = root.querySelector('[data-avatar-file]'), stage = root.querySelector('[data-crop-stage]');
    const canvas = stage.querySelector('canvas'), slider = root.querySelector('[data-crop-zoom]');
    let image = null, view = { zoom: 1, x: 0, y: 0 }, pointers = new Map(), pinch = null, disposed = false;
    const box = () => stage.clientWidth || 240;
    function draw() {
        if (!image) return;
        const size = box(), dpr = Math.min(2, devicePixelRatio || 1), { width, height } = dims(image);
        if (canvas.width !== Math.round(size * dpr)) { canvas.width = canvas.height = Math.round(size * dpr); }
        view = clampView(view, width, height, size);
        const r = sourceRect(view, width, height, size), ctx = canvas.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0); ctx.fillStyle = '#0c1a22'; ctx.fillRect(0, 0, size, size);
        ctx.drawImage(image, r.sx, r.sy, r.sw, r.sh, 0, 0, size, size);
        slider.value = String(view.zoom);
    }
    const fail = message => dotnet.invokeMethodAsync('AvatarError', message).catch(() => { });
    async function picked() {
        const file = input.files?.[0]; input.value = '';
        if (!file) return;
        try {
            const problem = checkFile(file, await file.slice(0, 16).arrayBuffer());
            if (problem) return fail(problem);
            const next = await decode(file), size = dims(next), tooBig = checkSize(size.width, size.height);
            if (tooBig) { next.close?.(); return fail(tooBig); }
            image?.close?.(); image = next; view = { zoom: 1, x: 0, y: 0 };
            await dotnet.invokeMethodAsync('CropOpened');
            requestAnimationFrame(() => { draw(); stage.scrollIntoView?.({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' }); stage.focus?.({ preventScroll: true }); });
        } catch { fail('無法讀取這張圖片，檔案可能已損壞。'); }
    }
    const local = e => { const r = stage.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    function down(e) {
        if (!image) return;
        stage.setPointerCapture?.(e.pointerId); pointers.set(e.pointerId, local(e));
        if (pointers.size === 2) { const [a, b] = [...pointers.values()]; pinch = { distance: Math.hypot(a.x - b.x, a.y - b.y), zoom: view.zoom }; }
        e.preventDefault();
    }
    function move(e) {
        if (!pointers.has(e.pointerId)) return;
        const previous = pointers.get(e.pointerId), now = local(e); pointers.set(e.pointerId, now);
        const { width, height } = dims(image);
        if (pointers.size >= 2 && pinch) {
            const [a, b] = [...pointers.values()];
            view = zoomAt(view, pinch.zoom * Math.hypot(a.x - b.x, a.y - b.y) / Math.max(1, pinch.distance), (a.x + b.x) / 2, (a.y + b.y) / 2, width, height, box());
        } else view = clampView({ ...view, x: view.x + now.x - previous.x, y: view.y + now.y - previous.y }, width, height, box());
        draw(); e.preventDefault();
    }
    function up(e) { pointers.delete(e.pointerId); if (pointers.size < 2) pinch = null; }
    function wheel(e) {
        if (!image) return;
        e.preventDefault(); const p = local(e), { width, height } = dims(image);
        view = zoomAt(view, view.zoom * Math.exp(-e.deltaY / 400), p.x, p.y, width, height, box()); draw();
    }
    function zoomInput() {
        if (!image) return; const size = box(), { width, height } = dims(image);
        view = zoomAt(view, Number(slider.value), size / 2, size / 2, width, height, size); draw();
    }
    function key(e) {
        if (!image) return;
        const step = e.shiftKey ? 24 : 8, moves = { ArrowLeft: [step, 0, 0], ArrowRight: [-step, 0, 0], ArrowUp: [0, step, 0], ArrowDown: [0, -step, 0], '+': [0, 0, .1], '=': [0, 0, .1], '-': [0, 0, -.1] };
        const m = moves[e.key]; if (!m) return;
        e.preventDefault(); const size = box(), { width, height } = dims(image);
        view = zoomAt({ ...view, x: view.x + m[0], y: view.y + m[1] }, view.zoom + m[2], size / 2, size / 2, width, height, size); draw();
    }
    const resize = globalThis.ResizeObserver ? new ResizeObserver(() => draw()) : null;
    resize?.observe(stage);
    input.addEventListener('change', picked); stage.addEventListener('keydown', key);
    stage.addEventListener('pointerdown', down); stage.addEventListener('pointermove', move);
    stage.addEventListener('pointerup', up); stage.addEventListener('pointercancel', up);
    stage.addEventListener('wheel', wheel, { passive: false }); slider.addEventListener('input', zoomInput);
    return {
        async confirm() {
            if (!image) throw new Error('尚未選擇圖片。');
            const url = await encode(image, view, box());
            return url;
        },
        cancel() { image?.close?.(); image = null; pointers.clear(); },
        dispose() {
            if (disposed) return; disposed = true; image?.close?.(); image = null; resize?.disconnect(); stage.removeEventListener('keydown', key);
            input.removeEventListener('change', picked); stage.removeEventListener('pointerdown', down); stage.removeEventListener('pointermove', move);
            stage.removeEventListener('pointerup', up); stage.removeEventListener('pointercancel', up); stage.removeEventListener('wheel', wheel); slider.removeEventListener('input', zoomInput);
        }
    };
}
