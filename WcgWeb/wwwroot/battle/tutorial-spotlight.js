// Tutorial spotlight: dims the page around the elements a lesson step points at and rings them.
// The layer never takes clicks or drags (pointer-events: none); the lesson itself decides which actions are accepted.
const NS = 'http://www.w3.org/2000/svg';
let layer = null, svg = null, mask = null, shade = null, rings = [], selectors = [], frame = 0, lastKey = '';

function ensureLayer() {
    if (layer) return;
    layer = document.createElement('div');
    layer.className = 'tutorial-spotlight';
    layer.setAttribute('aria-hidden', 'true');
    svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('class', 'tutorial-spotlight-shade');
    const defs = document.createElementNS(NS, 'defs');
    mask = document.createElementNS(NS, 'mask');
    mask.setAttribute('id', 'tutorial-spotlight-mask');
    defs.appendChild(mask);
    svg.appendChild(defs);
    shade = document.createElementNS(NS, 'rect');
    shade.setAttribute('width', '100%'); shade.setAttribute('height', '100%');
    shade.setAttribute('mask', 'url(#tutorial-spotlight-mask)'); shade.setAttribute('class', 'tutorial-spotlight-fill');
    svg.appendChild(shade);
    layer.appendChild(svg);
    document.body.appendChild(layer);
    // Follows layout changes (animations, scrolling, resizing) at a modest rate.
    frame = setInterval(() => requestAnimationFrame(draw), 90);
}

function visible(el) {
    const r = el.getBoundingClientRect();
    if (r.width < 2 || r.height < 2) return null;
    const style = getComputedStyle(el);
    if (style.visibility === 'hidden' || style.display === 'none') return null;
    return r;
}

function targets() {
    const found = [];
    for (const selector of selectors) {
        let el = null;
        try { el = [...document.querySelectorAll(selector)].find(x => visible(x)); } catch { el = null; }
        if (el && !found.includes(el)) found.push(el);
    }
    return found;
}

function draw() {
    if (!layer) return;
    const els = targets();
    // Board dialogs (choices, energy confirm, card detail) and drags must stay fully visible.
    const blocked = document.querySelector('.v06-board .modal-shade, .v06-board.dragging, .card-detail-backdrop, .modal.show');
    const dim = els.length > 0 && !blocked;
    const pad = 6;
    const rects = els.map(el => el.getBoundingClientRect());
    dodge(rects);
    const key = (dim ? 'd' : 'n') + rects.map(r => [r.left, r.top, r.width, r.height].map(Math.round).join(',')).join(';');
    if (key !== lastKey) {
        lastKey = key;
        while (mask.firstChild) mask.removeChild(mask.firstChild);
        const base = document.createElementNS(NS, 'rect');
        base.setAttribute('width', '100%'); base.setAttribute('height', '100%'); base.setAttribute('fill', 'white');
        mask.appendChild(base);
        rects.forEach(r => {
            const hole = document.createElementNS(NS, 'rect');
            hole.setAttribute('x', r.left - pad); hole.setAttribute('y', r.top - pad);
            hole.setAttribute('width', r.width + pad * 2); hole.setAttribute('height', r.height + pad * 2);
            hole.setAttribute('rx', 14); hole.setAttribute('fill', 'black');
            mask.appendChild(hole);
        });
        layer.classList.toggle('dim', dim);
        while (rings.length > rects.length) rings.pop().remove();
        while (rings.length < rects.length) { const ring = document.createElement('div'); ring.className = 'tutorial-spotlight-ring'; layer.appendChild(ring); rings.push(ring); }
        rects.forEach((r, i) => {
            const ring = rings[i];
            ring.style.left = `${r.left - pad}px`; ring.style.top = `${r.top - pad}px`;
            ring.style.width = `${r.width + pad * 2}px`; ring.style.height = `${r.height + pad * 2}px`;
        });
    }
}

// A page tour card sits in a corner; when the element it explains is under that corner it moves to the top.
// Measured against the card's bottom position so the choice never flips back and forth.
function dodge(rects) {
    const card = document.querySelector('.tour-card');
    if (!card) return;
    const c = card.getBoundingClientRect();
    const overlap = (top, bottom) => rects.reduce((sum, r) =>
        sum + Math.max(0, Math.min(r.right, c.right) - Math.max(r.left, c.left)) * Math.max(0, Math.min(r.bottom, bottom) - Math.max(r.top, top)), 0);
    const low = overlap(window.innerHeight - 20 - c.height, window.innerHeight - 8);
    const high = overlap(8, 20 + c.height);
    card.classList.toggle('at-top', low > 0 && high < low);
}

export function spotlight(list, scroll) {
    selectors = Array.isArray(list) ? list.filter(x => typeof x === 'string' && x) : [];
    ensureLayer();
    lastKey = '';
    if (scroll) {
        const first = targets()[0];
        if (first) first.scrollIntoView({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    }
    draw();
}

export function clear() {
    selectors = [];
    if (frame) clearInterval(frame);
    frame = 0; lastKey = '';
    rings = [];
    layer?.remove(); layer = null; svg = null; mask = null; shade = null;
}
