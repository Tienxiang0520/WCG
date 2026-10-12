// Presentation only: the engine still validates and pays for every released card.
export function freeCastReady({ x, y, startY, handTop, board, blocked = false }) {
    const distance = Math.max(40, Math.min(72, board.height * .08));
    return !blocked && x >= board.x && x <= board.x + board.width
        && y >= board.y && y < handTop - 12 && startY - y >= distance;
}

export function releaseZone(mode, point, destination) {
    // Filling energy is an explicit drop and always takes precedence over casting.
    if (mode !== 'free' || destination === 'energy') return destination;
    return freeCastReady(point) ? 'cast' : null;
}
